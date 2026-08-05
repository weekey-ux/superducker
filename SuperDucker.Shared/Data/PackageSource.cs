using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

/// <summary>
/// 软件包来源抽象。本地 localshop 与远程商店服务（RepoSource）都实现此接口，
/// 上层（ShopPanel）以统一方式扫描并合流，远程源不可达时静默回退本地源。
/// </summary>
public interface IPackageSource
{
    /// <summary>来源类型。</summary>
    PackageSourceKind Kind { get; }

    /// <summary>来源展示名（本地为 null，远程为服务地址）。</summary>
    string? Label { get; }

    /// <summary>扫描此来源提供的全部软件包。失败时抛出异常，由调用方决定是否静默忽略。</summary>
    Task<List<ShopPackage>> ScanAsync(DatabaseManager db);
}

/// <summary>
/// 本地 localshop 来源：直接复用现有 ScanPackages 逻辑。
/// </summary>
public sealed class LocalShopSource : IPackageSource
{
    public PackageSourceKind Kind => PackageSourceKind.Local;
    public string? Label => null;

    public Task<List<ShopPackage>> ScanAsync(DatabaseManager db)
    {
        var list = ShopManager.ScanPackages(db);
        foreach (var p in list)
            p.SourceKind = PackageSourceKind.Local;
        return Task.FromResult(list);
    }
}

/// <summary>
/// 远程商店服务来源：通过 HTTP 拉取 /api/index.json，解析为 ShopPackage 列表，
/// 并将远程图标下载到本地缓存目录回填 IconPath。
/// </summary>
public sealed class RepoSource : IPackageSource
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl;

    static RepoSource()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SuperDucker");
    }

    public RepoSource(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public PackageSourceKind Kind => PackageSourceKind.Repo;
    public string? Label => _baseUrl;

    public async Task<List<ShopPackage>> ScanAsync(DatabaseManager db)
    {
        var indexUrl = $"{_baseUrl}/api/index.json";
        using var resp = await _http.GetAsync(indexUrl);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        var index = JsonSerializer.Deserialize<RepoIndex>(json, _jsonOpts);
        if (index?.Packages is null)
            return new List<ShopPackage>();

        var iconsDir = WebHelper.GetIconsDirectory();
        if (!Directory.Exists(iconsDir))
            Directory.CreateDirectory(iconsDir);

        var result = new List<ShopPackage>();
        foreach (var p in index.Packages)
        {
            var pkg = new ShopPackage
            {
                SourceKind = PackageSourceKind.Repo,
                SdzipPath = string.Empty, // 远程包尚未下载
                PackageId = p.PackageId ?? string.Empty,
                Name = p.Name ?? p.PackageId ?? "(未知)",
                Abbreviation = p.Abbreviation ?? string.Empty,
                Description = p.Description,
                Version = p.Version ?? "1.0.0",
                Author = p.Author,
                Category = p.Category,
                DownloadUrl = ToAbsolute(p.DownloadUrl),
                IconUrl = ToAbsolute(p.IconUrl),
                SourceUrl = _baseUrl
            };

            // 兜底：服务端 index.json 缺 version 字段时（旧服务端/manifest 解析失败等），
            // 主动 GET 一次 manifest 修正版本。否则去重/升级判断会全部退化为 "1.0.0"。
            if (string.IsNullOrEmpty(p.Version))
            {
                try
                {
                    var m = await GetManifestAsync(pkg.PackageId);
                    if (m != null && !string.IsNullOrWhiteSpace(m.Version))
                    {
                        pkg.Version = m.Version;
                        // 顺便补上其他缺失字段
                        if (string.IsNullOrEmpty(pkg.Name) || pkg.Name == "(未知)")
                            pkg.Name = m.Name ?? pkg.Name;
                        if (string.IsNullOrEmpty(pkg.Abbreviation))
                            pkg.Abbreviation = m.Abbreviation ?? pkg.Abbreviation;
                        if (string.IsNullOrEmpty(pkg.Description))
                            pkg.Description = m.Description;
                        if (string.IsNullOrEmpty(pkg.Author))
                            pkg.Author = m.Author;
                        if (string.IsNullOrEmpty(pkg.Category))
                            pkg.Category = m.Categories.FirstOrDefault();
                    }
                }
                catch
                {
                    // manifest 也拉不到就保留默认 1.0.0，不阻断列表展示
                }
            }

            // 远程图标的本地缓存：以 packageId 为文件名，按扩展名落盘
            if (!string.IsNullOrEmpty(pkg.IconUrl))
            {
                try
                {
                    var localIcon = await DownloadIconAsync(pkg.IconUrl, iconsDir, pkg.PackageId);
                    if (localIcon != null)
                        pkg.IconPath = localIcon;
                }
                catch
                {
                    // 图标下载失败不阻断列表展示
                }
            }

            result.Add(pkg);
        }

        // 远程包无法得知本地安装状态，交由调用方（ShopPanel）比对已安装应用补全
        return result;
    }

    /// <summary>
    /// 将远程 .sdzip 下载到本地临时文件，并返回其路径。调用方应负责安装后的清理。
    /// </summary>
    public async Task<string> DownloadSdzipAsync(ShopPackage pkg, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(pkg.DownloadUrl))
            throw new InvalidOperationException($"软件包 {pkg.Name} 缺少下载地址");

        var url = pkg.DownloadUrl!;
        var tempDir = Path.Combine(Path.GetTempPath(), "SuperDucker", "repo");
        if (!Directory.Exists(tempDir))
            Directory.CreateDirectory(tempDir);

        var safeName = string.IsNullOrEmpty(pkg.PackageId)
            ? Guid.NewGuid().ToString("N")
            : pkg.PackageId;
        var localPath = Path.Combine(tempDir, $"{safeName}.sdzip");

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        using var src = await resp.Content.ReadAsStreamAsync();
        using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total is > 0)
                progress?.Report((double)read / total.Value);
        }

        return localPath;
    }

    /// <summary>将索引中可能是相对路径的 URL 补全为基于 baseUrl 的绝对地址。</summary>
    private string ToAbsolute(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url!;
        return $"{_baseUrl}/{url!.TrimStart('/')}";
    }

    /// <summary>阶段3：从远程仓库移除某个包（服务端仅内网可达）。</summary>
    public async Task DeleteRemoteAsync(string packageId)
    {
        var url = $"{_baseUrl}/api/packages/{Uri.EscapeDataString(packageId)}";
        using var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));
        // 404（已被删）/成功均视为已移除；其他错误抛出交由上层提示
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    /// <summary>阶段4：读取远程包的 manifest.json（安装参数：mainExe / extractSubDir / installActions 等）。</summary>
    public async Task<PackageManifest?> GetManifestAsync(string packageId)
    {
        var url = $"{_baseUrl}/api/manifest/{Uri.EscapeDataString(packageId)}";
        using var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PackageManifest>(json, _jsonOpts);
    }

    private static async Task<string?> DownloadIconAsync(string iconUrl, string iconsDir, string packageId)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(iconUrl).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                ext = ".png";

            var safeName = string.IsNullOrEmpty(packageId) ? Guid.NewGuid().ToString("N") : packageId;
            var localPath = Path.Combine(iconsDir, $"{safeName}{ext}");

            // 复用已存在的有效缓存：服务端提取到 wwwroot/icons/ 后，所有客户端会拉到同一张图，
            // 没必要每个客户端每次都重下。
            if (File.Exists(localPath))
            {
                try
                {
                    var fi = new FileInfo(localPath);
                    if (fi.Length >= 64) return localPath;
                    File.Delete(localPath); // 损坏缓存（< 64 字节通常是 3 字节占位/错误页），重下
                }
                catch { /* 重下兜底 */ }
            }

            using var resp = await _http.GetAsync(iconUrl);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 64) return null;

            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>远程索引的 DTO（与 SuperDucker.Repo 的 RepoIndex 对齐）。</summary>
    private sealed class RepoIndex
    {
        public List<RepoPackageDto>? Packages { get; set; }
    }

    private sealed class RepoPackageDto
    {
        public string? PackageId { get; set; }
        public string? Name { get; set; }
        public string? Abbreviation { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public string? Category { get; set; }
        public List<string>? Tags { get; set; }
        public long Size { get; set; }
        public string? DownloadUrl { get; set; }
        public string? IconUrl { get; set; }
    }
}

/// <summary>
/// 来源工厂：根据设置表构造本地源 + 所有已配置的远程商店服务源。
/// </summary>
public static class ShopSourceFactory
{
    /// <summary>设置表中存储远程服务地址列表的键（多行，每行一个 base URL）。</summary>
    public const string RepoUrlsSettingKey = "shop_repo_urls";

    /// <summary>
    /// 返回所有来源：始终包含本地 localshop 源，外加每个配置可达的远程源。
    /// 远程 URL 仅做基本规范化，不做连通性探测（扫描时才会真正请求）。
    /// </summary>
    public static List<IPackageSource> GetSources(DatabaseManager db)
    {
        var sources = new List<IPackageSource> { new LocalShopSource() };
        foreach (var url in ParseRepoUrls(db.GetSetting(RepoUrlsSettingKey)))
            sources.Add(new RepoSource(url));
        return sources;
    }

    /// <summary>
    /// 将设置中的多行地址文本解析为规范化后的 base URL 列表（每行一个，自动补 http://）。
    /// 供 GetSources 与 UI「测试连接」等场景复用。
    /// </summary>
    public static List<string> ParseRepoUrls(string? raw)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return urls;

        foreach (var line in raw.Split('\n'))
        {
            var url = line.Trim();
            if (url.Length == 0) continue;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }
            urls.Add(url);
        }
        return urls;
    }

    /// <summary>将远程服务地址列表序列化进设置表（每行一个）。</summary>
    public static void SaveRepoUrls(DatabaseManager db, IEnumerable<string> urls)
    {
        var normalized = urls
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        db.SetSetting(RepoUrlsSettingKey, string.Join('\n', normalized));
    }
}
