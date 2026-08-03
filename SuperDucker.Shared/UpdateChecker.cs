using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperDucker.Shared;

/// <summary>
/// 更新检查结果。
/// </summary>
/// <param name="HasUpdate">是否存在比当前版本更新的发行版。</param>
/// <param name="CurrentVersion">本地当前版本字符串（如 "1.1.0"，不带 v 前缀）。</param>
/// <param name="LatestVersion">远端最新版本字符串（如 "1.2.0"，不带 v 前缀）。</param>
/// <param name="ReleaseUrl">最新发行版的 Release 页面 URL（用于打开浏览器下载）。</param>
/// <param name="ReleaseNotes">发行版正文（Markdown 原文），可能为 null 或空。</param>
/// <param name="ErrorMessage">出错时的错误信息；成功时为 null。</param>
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? ReleaseNotes,
    string? ErrorMessage)
{
    /// <summary>是否因为网络/解析问题导致检查失败（区别于"无新版本"）。</summary>
    public bool Failed => ErrorMessage != null;
}

/// <summary>
/// 通过 GitHub Release API 检查 SuperDucker 是否有新版本。
///
/// 设计要点：
/// 1. 仅做"检查 + 比对"，不下载、不自动替换（方案 A：轻量、绿色软件升级 = 覆盖 exe）。
/// 2. 静默失败：网络/解析异常一律被吞掉并以 <see cref="UpdateCheckResult.Failed"/> = true 返回，
///    由调用方决定是否提示用户，绝不让"更新检查"功能把程序整崩。
/// 3. 版本比较使用 SemVer 规范（主.次.修订），支持 "v" 前缀，预发布标签在比较时被忽略
///    （绿色单文件软件无预发布概念，简化处理）。
/// 4. 仓库地址 <see cref="DefaultRepoOwner"/>/<see cref="DefaultRepoName"/> 可由调用方覆盖，
///    便于将来 fork 或私有部署时复用本类。
/// </summary>
public static class UpdateChecker
{
    /// <summary>默认仓库所有者。</summary>
    public const string DefaultRepoOwner = "weekey-ux";

    /// <summary>默认仓库名。</summary>
    public const string DefaultRepoName = "SuperDucker";

    /// <summary>Release 页面 URL（用于"下载页"按钮直接打开）。</summary>
    public const string DefaultRepoUrl = "https://github.com/" + DefaultRepoOwner + "/" + DefaultRepoName;

    private const string ApiAccept = "application/vnd.github+json";
    private const string UserAgent = "SuperDucker-Updater";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8) // 比 WebHelper 短一些：检查更新失败应尽快放弃
    };

    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ApiAccept));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// 异步检查 GitHub Release 是否比本地版本更新。
    /// </summary>
    /// <param name="currentVersion">当前本地版本字符串（如 "1.1.0" 或 "v1.1.0"）。允许为 null/空（视为 "0.0.0"）。</param>
    /// <param name="repoOwner">仓库所有者，默认 <see cref="DefaultRepoOwner"/>。</param>
    /// <param name="repoName">仓库名，默认 <see cref="DefaultRepoName"/>。</param>
    public static async Task<UpdateCheckResult> CheckAsync(
        string? currentVersion,
        string repoOwner = DefaultRepoOwner,
        string repoName = DefaultRepoName)
    {
        var current = NormalizeVersion(currentVersion) ?? "0.0.0";
        var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

        try
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                // 404 通常意味着仓库还没有任何 release（首次发行的常见情况），按"无新版本"处理而非错误。
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult(false, current, current, DefaultRepoUrl, null, null);
                }

                return new UpdateCheckResult(false, current, current, DefaultRepoUrl, null,
                    $"GitHub 返回 {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

            var latest = NormalizeVersion(tag);
            if (latest == null)
            {
                return new UpdateCheckResult(false, current, current,
                    htmlUrl ?? DefaultRepoUrl, body, "无法解析远端版本号");
            }

            var hasUpdate = CompareSemVer(latest, current) > 0;
            return new UpdateCheckResult(hasUpdate, current, latest,
                htmlUrl ?? DefaultRepoUrl, body, null);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(false, current, current, DefaultRepoUrl, null, $"网络错误：{ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(false, current, current, DefaultRepoUrl, null, "连接超时（8秒）");
        }
        catch (Exception ex)
        {
            // 任何其他异常（JSON 解析、属性缺失等）一律吞掉，避免影响主程序
            return new UpdateCheckResult(false, current, current, DefaultRepoUrl, null, $"检查失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 规范化版本字符串：去前后空白、去掉前导 'v' 或 'V'、切掉预发布/构建元数据。
    /// 若完全无法解析为 SemVer 则返回 null。
    /// </summary>
    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
        // 切掉预发布（如 "1.2.0-beta"）和构建元数据（如 "1.2.0+abc"）
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s.Substring(0, dash);
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);
        if (!Regex.IsMatch(s, @"^\d+(\.\d+){0,3}$")) return null;
        return s;
    }

    /// <summary>
    /// SemVer 比较：a &gt; b 返回 1；a &lt; b 返回 -1；相等返回 0。
    /// 缺失的次版本/修订号按 0 补齐。
    /// </summary>
    public static int CompareSemVer(string a, string b)
    {
        var va = ToTuple(a);
        var vb = ToTuple(b);
        for (int i = 0; i < 3; i++)
        {
            if (va[i] > vb[i]) return 1;
            if (va[i] < vb[i]) return -1;
        }
        return 0;
    }

    private static int[] ToTuple(string v)
    {
        var parts = v.Split('.');
        var arr = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i < parts.Length && int.TryParse(parts[i], out var n)) arr[i] = n;
            else arr[i] = 0;
        }
        return arr;
    }
}
