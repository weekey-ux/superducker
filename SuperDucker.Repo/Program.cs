using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// === 配置 ===
const string RepoDirName = "localshop";
// 仓库目录解析优先级：
//   1) 环境变量 REPO_DIR（最显式，运维用：set REPO_DIR=\\GreenBox\Server\repo\localshop）
//   2) 配置文件 ShopDir（appsettings.json / 命令行 --ShopDir）
//   3) exe 同级目录下的 localshop\（单文件部署最自然：把 superducker-repo.exe 放在
//      \\server\repo\ 下，自动扫 \\server\repo\localshop\，用户上传的包与服务天然同目录）
//   4) 开发态回退：从 AppContext.BaseDirectory 向上找项目根（含 .sln 或名为 SuperDucker 的目录）
string? envDir = Environment.GetEnvironmentVariable("REPO_DIR");
string? cfgDir = builder.Configuration.GetValue<string?>("ShopDir");
string shopDir;
if (!string.IsNullOrWhiteSpace(envDir)) shopDir = envDir!;
else if (!string.IsNullOrWhiteSpace(cfgDir)) shopDir = cfgDir!;
else
{
    // 优先取 exe 同级（仅在宿主不是 dotnet 代理时；dotnet run / dotnet test 跳过）
    string? exeDir = null;
    var processPath = Environment.ProcessPath;
    if (!string.IsNullOrEmpty(processPath))
    {
        var exeName = Path.GetFileName(processPath);
        if (!exeName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            exeDir = Path.GetDirectoryName(processPath);
    }

    if (!string.IsNullOrEmpty(exeDir))
    {
        shopDir = Path.Combine(exeDir!, RepoDirName);
    }
    else
    {
        // 开发态回退：从 BaseDirectory 向上找项目根
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? root = null;
        for (int i = 0; i < 6 && probe is not null; i++, probe = probe.Parent)
        {
            if (probe.EnumerateFiles("*.sln").Any() || probe.Name.Equals("SuperDucker", StringComparison.OrdinalIgnoreCase))
            { root = probe; break; }
        }
        shopDir = Path.Combine((root ?? new DirectoryInfo(Directory.GetCurrentDirectory())).FullName, RepoDirName);
    }
}
Directory.CreateDirectory(shopDir);
Console.WriteLine($"[Repo] shopDir = {shopDir}");

// wwwroot/icons 用于从 .sdzip 内自动提取的图标（与 UseStaticFiles 协作，无需 /api/icon/* 端点也可直接访问）
var wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var iconsWebDir = Path.Combine(wwwrootDir, "icons");
Directory.CreateDirectory(iconsWebDir);
Console.WriteLine($"[Repo] iconsWebDir = {iconsWebDir}");

// 受支持的图标扩展名（小写、带点）。全仓库共用，避免各端点重复内联。
// 单一来源为文件末尾的 IconExtensionSet.Items；此处引用同一集合，
// 避免在顶级变量与静态常量之间重复维护字面量。
string[] IconExtensions = IconExtensionSet.Items.ToArray();

var port = builder.Configuration.GetValue("Port", 5180);

// 放宽 Kestrel 请求体上限（局域网包可能很大），默认仅 ~28.6MB 会触发 Failed to fetch。
// 单点 ListenAnyIP 绑定 0.0.0.0:port（已涵盖 127.0.0.1 与所有网卡），
// 避免 0.0.0.0 + 127.0.0.1 + localhost 三地址并存引发自冲突（Address already in use）。
// Kestrel 自 .NET 5 起在 ListenAnyIP 上默认启用 SO_REUSEADDR，TIME_WAIT 端口可立即复用。
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024; // 2 GiB
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.ListenAnyIP(port);
});

// 说明：上传端点用 MultipartReader 直接读 req.Body 流式解析，
// 完全绕开 ASP.NET Core 的 Form 绑定（ReadFormAsync / FormOptions），
// 因此无需配置 FormOptions.MultipartBodyLengthLimit（其默认 128MB 正是 700MB 上传 500 的旧根因）。
// 仅靠 Kestrel 层 MaxRequestBodySize=2GiB + IHttpMaxRequestBodySizeFeature 兜底即可。

var app = builder.Build();

// === 静态资源：上传管理页 ===
app.UseDefaultFiles();
app.UseStaticFiles();

// === API：仓库索引（现扫现生成） ===
app.MapGet("/api/index.json", () =>
{
    var packages = ScanShop(shopDir, iconsWebDir);
    var index = new RepoIndex
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        Count = packages.Count,
        Packages = packages
    };
    return Results.Json(index, RepoConfig.JsonOpts);
});

// === API：下载 .sdzip 包 ===
app.MapGet("/api/download/{packageId}", (string packageId) =>
{
    var file = Path.Combine(shopDir, $"{packageId}.sdzip");
    if (!File.Exists(file)) return Results.NotFound($"package not found: {packageId}");
    return Results.File(file, "application/octet-stream", $"{packageId}.sdzip");
});

// === API：下载图标（兼容老格式：仍支持 shopDir/ 同名图片） ===
app.MapGet("/api/icon/{packageId}", (string packageId) =>
{
    // 优先尝试 wwwroot/icons/（自动从 .sdzip 提取的图标）
    foreach (var ext in IconExtensions)
    {
        var icon = Path.Combine(iconsWebDir, packageId + ext);
        if (File.Exists(icon))
            return Results.File(icon, GetMime(icon));
    }
    // 兼容：用户单独上传到 shopDir/ 的图标
    foreach (var ext in IconExtensions)
    {
        var icon = Path.Combine(shopDir, packageId + ext);
        if (File.Exists(icon))
            return Results.File(icon, GetMime(icon));
    }
    return Results.NotFound();
});

// === API：上传（POST multipart，含 .sdzip + 可选 icon） ===
// 用 MultipartReader 流式解析，绕开 ASP.NET Core FormOptions 默认 128MB 上限。
// 700MB+ 大文件上传 500 的根因：ReadFormAsync 走 FormOptions，
// MultipartBodyLengthLimit=128MB / ValueLengthLimit~28.6MB，超限即抛 InvalidDataException。
// 直接读 req.Body 的 MultipartReader 无此限制。
app.MapPost("/api/upload", async (HttpRequest req) =>
{
    try
    {
        // 兜底禁用单请求体大小限制（Kestrel 层已全局放宽到 2 GiB）
        var reqSizeFeature = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (reqSizeFeature is not null) reqSizeFeature.MaxRequestBodySize = null;

        if (!req.HasFormContentType)
            return Results.Json(new { title = "bad_request", detail = "expected multipart/form-data" }, statusCode: 400);

        if (!MediaTypeHeaderValue.TryParse(req.ContentType, out var mediaType)
            || mediaType is null
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { title = "bad_request", detail = "invalid multipart content-type" }, statusCode: 400);

        // 从 content-type 提取 boundary（去掉引号），无需依赖 internal 的 MultipartRequestHelper
        var boundary = mediaType!.Boundary.ToString().Trim('"').Trim();
        if (string.IsNullOrEmpty(boundary))
            return Results.Json(new { title = "bad_request", detail = "missing multipart boundary" }, statusCode: 400);

        var ct = req.HttpContext.RequestAborted;
        string? packageId = null;
        long packageSize = 0;
        const int readBufferSize = 64 * 1024;
        var reader = new MultipartReader(boundary, req.Body);

        for (var part = await reader.ReadNextSectionAsync(ct); part is not null; part = await reader.ReadNextSectionAsync(ct))
        {
            if (!ContentDispositionHeaderValue.TryParse(part.ContentDisposition, out var contentDisposition))
            {
                await DrainStreamAsync(part.Body, ct);
                continue;
            }
            var name = contentDisposition!.Name.ToString().Trim('"').Trim().ToLowerInvariant();
            var fileName = contentDisposition.FileName.ToString().Trim('"');

            if (string.IsNullOrEmpty(name))
            {
                await DrainStreamAsync(part.Body, ct);
                continue;
            }

            if (name == "package")
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    await DrainStreamAsync(part.Body, ct);
                    return Results.Json(new { title = "bad_request", detail = "missing 'package' filename" }, statusCode: 400);
                }
                packageId = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    await DrainStreamAsync(part.Body, ct);
                    return Results.Json(new { title = "bad_request", detail = "invalid package filename" }, statusCode: 400);
                }

                var destPkg = Path.Combine(shopDir, $"{packageId}.sdzip");
                await using (var fs = new FileStream(destPkg, FileMode.Create, FileAccess.Write, FileShare.None, readBufferSize, useAsync: true))
                {
                    var buffer = new byte[readBufferSize];
                    int read;
                    while ((read = await part.Body.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                        packageSize += read;
                    }
                }
            }
            else if (name == "icon" && !string.IsNullOrEmpty(fileName) && packageId is not null)
            {
                var iconExt = Path.GetExtension(fileName);
                if (!string.IsNullOrWhiteSpace(iconExt))
                {
                    // 图标统一存到 wwwroot/icons/（与 ScanShop 自动提取的目录一致）
                    // 这样 UseStaticFiles 直接服务 /icons/{id}.{ext}，无需 /api/icon/* 端点。
                    Directory.CreateDirectory(iconsWebDir);
                    var destIcon = Path.Combine(iconsWebDir, packageId + iconExt.ToLowerInvariant());
                    await using var ifs = new FileStream(destIcon, FileMode.Create, FileAccess.Write, FileShare.None, readBufferSize, useAsync: true);
                    var buffer = new byte[readBufferSize];
                    int read;
                    while ((read = await part.Body.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        await ifs.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                else await DrainStreamAsync(part.Body, ct);
            }
            else
            {
                await DrainStreamAsync(part.Body, ct);
            }
        }

        if (packageId is null)
            return Results.Json(new { title = "bad_request", detail = "missing 'package' (.sdzip)" }, statusCode: 400);

        return Results.Ok(new { packageId, size = packageSize });
    }
    catch (Exception ex)
    {
        // 返回 JSON 错误而非中断连接，避免浏览器 "Failed to fetch"。
        // 用 Results.Json 显式序列化 detail，浏览器前端能直接看到异常原因。
        Console.Error.WriteLine($"[Repo] upload failed: {ex.GetType().Name}: {ex.Message}");
        return Results.Json(new { title = "upload_failed", detail = $"{ex.GetType().Name}: {ex.Message}" }, statusCode: 500);
    }
});

// === API：包列表（供管理页增删改查读取，含 size/版本/图标等完整信息） ===
app.MapGet("/api/packages", () =>
{
    var packages = ScanShop(shopDir, iconsWebDir);
    return Results.Json(packages, RepoConfig.JsonOpts);
});

// === API：重命名包（移动 .sdzip 及同名图标，packageId 即文件名） ===
app.MapPost("/api/packages/{packageId}/rename", (string packageId, RenameRequest body) =>
{
    var sid = SanitizeId(packageId);
    if (sid is null) return Results.BadRequest(new { error = "invalid_package_id" });
    packageId = sid;

    var newId = SanitizeId(body?.NewId);
    if (newId is null || newId == packageId)
        return Results.BadRequest(new { error = "invalid_new_id" });

    var srcPkg = Path.Combine(shopDir, packageId + ".sdzip");
    var dstPkg = Path.Combine(shopDir, newId + ".sdzip");
    if (!File.Exists(srcPkg)) return Results.NotFound(new { error = "package_not_found" });
    if (File.Exists(dstPkg)) return Results.Conflict(new { error = "target_exists" });

    try
    {
        File.Move(srcPkg, dstPkg);
        // 同步迁移同名图标（wwwroot/icons 优先，兼容 shopDir）
        MoveIconsFor(packageId, newId);
        return Results.Ok(new { packageId = newId });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// === API：替换包图标（multipart 单文件，覆盖 wwwroot/icons/{id}.ext） ===
app.MapPost("/api/packages/{packageId}/icon", async (string packageId, HttpRequest req) =>
{
    var sid = SanitizeId(packageId);
    if (sid is null) return Results.BadRequest(new { error = "invalid_package_id" });
    packageId = sid;

    var destPkg = Path.Combine(shopDir, packageId + ".sdzip");
    if (!File.Exists(destPkg)) return Results.NotFound(new { error = "package_not_found" });

    try
    {
        var reqSizeFeature = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (reqSizeFeature is not null) reqSizeFeature.MaxRequestBodySize = null;

        if (!req.HasFormContentType || !MediaTypeHeaderValue.TryParse(req.ContentType, out var mediaType) || mediaType is null)
            return Results.Json(new { title = "bad_request", detail = "expected multipart/form-data" }, statusCode: 400);

        var boundary = mediaType!.Boundary.ToString().Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(boundary))
            return Results.Json(new { title = "bad_request", detail = "missing multipart boundary" }, statusCode: 400);

        var ct = req.HttpContext.RequestAborted;
        const int readBufferSize = 64 * 1024;
        var reader = new MultipartReader(boundary, req.Body);
        bool saved = false;

        for (var part = await reader.ReadNextSectionAsync(ct); part is not null; part = await reader.ReadNextSectionAsync(ct))
        {
            if (!ContentDispositionHeaderValue.TryParse(part.ContentDisposition, out var cd)) { await DrainStreamAsync(part.Body, ct); continue; }
            var name = cd!.Name.ToString().Trim('"').Trim().ToLowerInvariant();
            var fileName = cd.FileName.ToString().Trim('"');
            if (name != "icon" || string.IsNullOrEmpty(fileName)) { await DrainStreamAsync(part.Body, ct); continue; }

            var iconExt = Path.GetExtension(fileName).ToLowerInvariant();
            if (!IsIconExtension(iconExt)) { await DrainStreamAsync(part.Body, ct); continue; }

            // 先删除旧图标，避免多扩展名并存
            DeleteIconsFor(packageId);

            Directory.CreateDirectory(iconsWebDir);
            var destIcon = Path.Combine(iconsWebDir, packageId + iconExt);
            await using (var fs = new FileStream(destIcon, FileMode.Create, FileAccess.Write, FileShare.None, readBufferSize, useAsync: true))
            {
                var buffer = new byte[readBufferSize];
                int read;
                while ((read = await part.Body.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            saved = true;
        }

        return saved ? Results.Ok(new { packageId })
                     : Results.Json(new { title = "bad_request", detail = "missing icon file" }, statusCode: 400);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Repo] icon replace failed: {ex.GetType().Name}: {ex.Message}");
        return Results.Json(new { title = "icon_failed", detail = $"{ex.GetType().Name}: {ex.Message}" }, statusCode: 500);
    }
});

// 删除指定包 id 的全部同名图标文件（优先 wwwroot/icons/，兼容老的 shopDir/）。
// 非 static：需捕获顶级局部变量 iconsWebDir / shopDir / IconExtensions。
void DeleteIconsFor(string id)
{
    foreach (var ext in IconExtensions)
    {
        var web = Path.Combine(iconsWebDir, id + ext);
        if (File.Exists(web)) File.Delete(web);
        var old = Path.Combine(shopDir, id + ext);
        if (File.Exists(old)) File.Delete(old);
    }
}

// 把指定包 id 的全部同名图标从 oldId 迁移到 newId（wwwroot/icons/ 与 shopDir/ 均处理）。
// 非 static：需捕获顶级局部变量 iconsWebDir / shopDir / IconExtensions。
void MoveIconsFor(string oldId, string newId)
{
    foreach (var ext in IconExtensions)
    {
        var webOld = Path.Combine(iconsWebDir, oldId + ext);
        if (File.Exists(webOld)) File.Move(webOld, Path.Combine(iconsWebDir, newId + ext));
        var oldOld = Path.Combine(shopDir, oldId + ext);
        if (File.Exists(oldOld)) File.Move(oldOld, Path.Combine(shopDir, newId + ext));
    }
}

/// <summary>校验并归一化 packageId：禁止路径分隔符与空值，返回不含扩展名的纯 id 或 null。</summary>
static string? SanitizeId(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    var id = Path.GetFileNameWithoutExtension(raw.Trim());
    if (string.IsNullOrWhiteSpace(id)) return null;
    if (id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar)) return null;
    return id;
}

static async Task DrainStreamAsync(System.IO.Stream s, CancellationToken ct)
{
    var buf = new byte[64 * 1024];
    while (await s.ReadAsync(buf, 0, buf.Length, ct) > 0) { }
}

// === 阶段3：从仓库删除某个包（仅内网，服务端"暂不需要认证"） ===
app.MapDelete("/api/packages/{packageId}", (string packageId) =>
{
    if (string.IsNullOrWhiteSpace(packageId)
        || packageId.Contains(Path.DirectorySeparatorChar)
        || packageId.Contains(Path.AltDirectorySeparatorChar))
        return Results.BadRequest(new { error = "invalid_package_id" });

    var destPkg = Path.Combine(shopDir, $"{packageId}.sdzip");
    if (!File.Exists(destPkg)) return Results.NotFound(new { error = "package_not_found" });

    try
    {
        File.Delete(destPkg);
        // 一并删除同名图标（优先 wwwroot/icons/，兼容老的 shopDir/）
        DeleteIconsFor(packageId);
        return Results.Ok(new { deleted = packageId });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// === 阶段4：返回单个包的完整 manifest.json（客户端安装前读取 mainExe / extractSubDir / installActions 等安装参数） ===
app.MapGet("/api/manifest/{packageId}", (string packageId) =>
{
    if (string.IsNullOrWhiteSpace(packageId)
        || packageId.Contains(Path.DirectorySeparatorChar)
        || packageId.Contains(Path.AltDirectorySeparatorChar))
        return Results.BadRequest(new { error = "invalid_package_id" });

    var destPkg = Path.Combine(shopDir, $"{packageId}.sdzip");
    if (!File.Exists(destPkg)) return Results.NotFound(new { error = "package_not_found" });

    try
    {
        using var zip = ZipFile.OpenRead(destPkg);
        var entry = zip.GetEntry("manifest.json");
        if (entry == null) return Results.NotFound(new { error = "manifest_not_found" });
        using var sr = new StreamReader(entry.Open());
        return Results.Content(sr.ReadToEnd(), "application/json");
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// === 管理页（简单 HTML，支持拖拽上传） ===
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();

// === 辅助 ===

static List<RepoPackage> ScanShop(string shopDir, string iconsWebDir)
{
    var result = new List<RepoPackage>();
    foreach (var file in Directory.GetFiles(shopDir, "*.sdzip"))
    {
        var id = Path.GetFileNameWithoutExtension(file);
        var pkg = new RepoPackage
        {
            PackageId = id,
            Size = new FileInfo(file).Length,
            DownloadUrl = $"/api/download/{id}",
            IconUrl = HasIcon(iconsWebDir, id) ? $"/icons/{id}{GetIconExt(iconsWebDir, id)}" : null
        };

        // 尝试读 manifest.json（包内）
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.GetEntry("manifest.json");
            if (entry != null)
            {
                using var sr = new StreamReader(entry.Open());
                // 以 ManifestDto 作为服务端读取契约（字段须与 Shared.PackageManifest 保持一致，
                // 详见 ManifestDto 的类型注释）。ReadOpts 忽略大小写，兼容 manifest.json 的小写键。
                var m = JsonSerializer.Deserialize<ManifestDto>(sr.ReadToEnd(), RepoConfig.ReadOpts);
                if (m != null)
                {
                    pkg.Name = m.Name ?? id;
                    pkg.Abbreviation = m.Abbreviation;
                    pkg.Version = m.Version;
                    pkg.Description = m.Description;
                    pkg.Author = m.Author;
                    pkg.Category = m.Category;
                    pkg.Tags = m.Tags;
                }

                // 自动从压缩包内提取图标：约定与本地 ShopManager.ReadPackageInfo 一致
                // （icon.{ext} 或 {abbreviation}.{ext}，且必须位于根目录）
                // 仅在 wwwroot/icons 下尚未存在时提取，避免覆盖用户独立上传的图标。
                if (!HasIcon(iconsWebDir, id))
                {
                    var iconEntry = FindIconEntry(zip, m?.Abbreviation);
                    if (iconEntry != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(iconsWebDir);
                            var iconExt = Path.GetExtension(iconEntry.Name);
                            var destIcon = Path.Combine(iconsWebDir, id + iconExt.ToLowerInvariant());
                            iconEntry.ExtractToFile(destIcon, false);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Repo] WARN: 提取 {file} 内图标失败: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                // 没有 manifest.json 时打一行警告，便于诊断"客户端拿不到版本号"
                Console.Error.WriteLine($"[Repo] WARN: {file} 中未找到 manifest.json，index 将缺少 version/name 等字段");
            }
        }
        catch (Exception ex)
        {
            // 损坏包 / 序列化失败：把异常详情打出来，方便用户重启后看到具体哪个包、什么原因
            Console.Error.WriteLine($"[Repo] WARN: 读取 {file} 的 manifest.json 失败: {ex.GetType().Name}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(pkg.Name)) pkg.Name = id;

        // 提取后再补一次 IconUrl（首次扫描时上面提前算的 IconUrl 还没考虑到新提取的图标）
        if (HasIcon(iconsWebDir, id))
            pkg.IconUrl = $"/icons/{id}{GetIconExt(iconsWebDir, id)}";

        result.Add(pkg);
    }
    return result.OrderBy(p => p.Name).ToList();
}

/// <summary>从 zip 内查找图标条目（icon.{ext} 或 {abbreviation}.{ext}），返回匹配的 ZipArchiveEntry 或 null。</summary>
static ZipArchiveEntry? FindIconEntry(ZipArchive zip, string? abbreviation)
{
    // 优先匹配 abbreviation.{ext}，其次 icon.{ext}
    ZipArchiveEntry? entry = null;
    if (!string.IsNullOrEmpty(abbreviation))
    {
        entry = zip.Entries.FirstOrDefault(e =>
            string.IsNullOrEmpty(Path.GetDirectoryName(e.FullName)) &&
            e.Name.StartsWith(abbreviation + ".", StringComparison.OrdinalIgnoreCase) &&
            IsIconExtension(Path.GetExtension(e.Name)));
    }
    if (entry == null)
    {
        entry = zip.Entries.FirstOrDefault(e =>
            string.IsNullOrEmpty(Path.GetDirectoryName(e.FullName)) &&
            e.Name.StartsWith("icon.", StringComparison.OrdinalIgnoreCase) &&
            IsIconExtension(Path.GetExtension(e.Name)));
    }
    return entry;
}

// 判断扩展名是否为受支持的图标类型。
// 本函数为 static（无法捕获顶级局部变量），故使用 IconExtensionSet 静态常量集合；
// 其内容必须与顶级变量 IconExtensions 保持一致。
static bool IsIconExtension(string ext)
{
    if (string.IsNullOrEmpty(ext)) return false;
    return IconExtensionSet.Contains(ext);
}

static bool HasIcon(string iconsWebDir, string id)
{
    return IconExtensionSet.Items.Any(ext => File.Exists(Path.Combine(iconsWebDir, id + ext)));
}

static string GetIconExt(string iconsWebDir, string id)
{
    foreach (var ext in IconExtensionSet.Items)
    {
        if (File.Exists(Path.Combine(iconsWebDir, id + ext)))
            return ext;
    }
    return ".png";
}

static string GetMime(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".ico" => "image/x-icon",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };
}

file static class RepoConfig
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>读取 manifest.json 时使用：忽略属性名大小写，并允许小驼峰。manifest 字段是手动写的 "abbreviation"/"version" 等小写键。</summary>
    public static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

// === DTO ===

public class RepoIndex
{
    public DateTimeOffset GeneratedAt { get; set; }
    public int Count { get; set; }
    public List<RepoPackage> Packages { get; set; } = new();
}

public class RepoPackage
{
    public string PackageId { get; set; } = "";
    public string? Abbreviation { get; set; }
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public long Size { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string? IconUrl { get; set; }
}

public class RenameRequest
{
    public string? NewId { get; set; }
}

/// <summary>
/// 服务端对 .sdzip 内 manifest.json 的只读投影（DTO）。
/// 字段定义必须与 <c>SuperDucker.Shared.Models.PackageManifest</c> 保持一致——
/// 它是包内清单契约在服务端的子集镜像，请勿在此独立增删字段，以免前后端字段漂移。
/// 之所以不复用 Shared 的 PackageManifest 类型，是因为 Shared 目标是 net8.0-windows（含 WPF），
/// 而本服务是纯 ASP.NET Core（net8.0），不能反向引用 Windows 专用库。
/// </summary>
public class ManifestDto
{
    public string? Name { get; set; }
    public string? Abbreviation { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
}

/// <summary>
/// 受支持的图标扩展名集合（小写、带点），供 static 本地函数使用。
/// 顶级语句中无法声明 static 字段，且 static 本地函数不能捕获顶级局部变量，
/// 因此在此以静态类型承载；其内容必须与 Program.cs 顶部的 IconExtensions 变量保持一致。
/// </summary>
internal static class IconExtensionSet
{
    public static readonly IReadOnlyCollection<string> Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".ico", ".webp", ".svg"
    };

    public static bool Contains(string ext) => Items.Contains(ext);
}
