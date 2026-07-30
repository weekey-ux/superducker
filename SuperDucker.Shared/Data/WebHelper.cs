using System.Net.Http;

namespace SuperDucker.Shared.Data;

/// <summary>
/// Helpers for URL validation and favicon fetching.
/// </summary>
public static class WebHelper
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static WebHelper()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// Validates a URL by sending HEAD first, then GET as fallback.
    /// Returns (isValid, statusCode, errorMessage).
    /// </summary>
    public static async Task<(bool IsValid, string? Error)> ValidateUrlAsync(string url)
    {
        try
        {
            // Try HEAD first (fast, no body)
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);

            if (headResp.IsSuccessStatusCode)
                return (true, null);

            // HEAD not supported or returned error — try GET
            if (headResp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                headResp.StatusCode == System.Net.HttpStatusCode.NotImplemented ||
                (int)headResp.StatusCode >= 400)
            {
                using var getResp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (getResp.IsSuccessStatusCode)
                    return (true, null);

                return (false, $"服务器返回 {(int)getResp.StatusCode} {getResp.ReasonPhrase}");
            }

            return (false, $"服务器返回 {(int)headResp.StatusCode} {headResp.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"网络错误：{ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时（10秒）");
        }
        catch (Exception ex)
        {
            return (false, $"验证失败：{ex.Message}");
        }
    }

    /// <summary>
    /// Fetches a website's favicon and saves it to the specified path.
    /// 仅使用目标网站自身的图标资源（本地化策略，不依赖任何第三方服务）：
    ///   1. 直接请求根路径 /favicon.ico；
    ///   2. 解析网页 HTML 中的 &lt;link rel="icon"&gt; 标签。
    /// 当两种方式均失败时返回 null，由调用方回退到本地默认图标，
    /// 以保证程序在完全离线环境下也能正常工作。
    /// </summary>
    public static async Task<string?> FetchFaviconAsync(string pageUrl, string savePath)
    {
        try
        {
            var uri = new Uri(pageUrl);
            var baseUrl = $"{uri.Scheme}://{uri.Host}";

            // 策略 1：直接请求网站根目录下的 /favicon.ico
            var faviconUrl = $"{baseUrl}/favicon.ico";
            if (await TryDownloadIconAsync(faviconUrl, savePath))
                return savePath;

            // 策略 2：解析网页 HTML 中的 <link rel="icon"> 或 <link rel="shortcut icon">
            var iconUrl = await FindIconFromHtmlAsync(pageUrl, baseUrl);
            if (iconUrl != null && await TryDownloadIconAsync(iconUrl, savePath))
                return savePath;

            // 两种本地策略均失败：返回 null，交由调用方使用本地默认图标（离线友好）
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryDownloadIconAsync(string iconUrl, string savePath)
    {
        try
        {
            var resp = await _http.GetAsync(iconUrl);
            if (!resp.IsSuccessStatusCode) return false;

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await resp.Content.ReadAsByteArrayAsync();

            // Must be an image type and have some content
            if (bytes.Length < 64) return false;
            if (!contentType.StartsWith("image/") &&
                !contentType.Contains("icon") &&
                !contentType.Contains("octet-stream"))
                return false;

            // Check if it's actually HTML (some servers return error pages for /favicon.ico)
            // More robust detection: check first non-whitespace character
            bool isHtml = false;
            for (int i = 0; i < bytes.Length && i < 1024; i++)
            {
                if (bytes[i] == '<')
                {
                    // Skip whitespace and comments
                    int j = i + 1;
                    while (j < bytes.Length && char.IsWhiteSpace((char)bytes[j])) j++;
                    if (j >= bytes.Length) break;
                    
                    char c1 = (char)bytes[j];
                    char c2 = j + 1 < bytes.Length ? (char)bytes[j + 1] : '\0';
                    
                    // Check for DOCTYPE, tag, or comment
                    if (c1 == '!' || c1 == '?' || char.IsLetter(c1))
                    {
                        isHtml = true;
                        break;
                    }
                }
            }
            if (isHtml)
                return false;

            var dir = Path.GetDirectoryName(savePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllBytesAsync(savePath, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> FindIconFromHtmlAsync(string pageUrl, string baseUrl)
    {
        try
        {
            var html = await _http.GetStringAsync(pageUrl);

            // Look for <link rel="icon" href="..."> or <link rel="shortcut icon" href="...">
            var patterns = new[]
            {
                @"<link[^>]+rel\s*=\s*[""'](?:shortcut\s+)?icon[""'][^>]+href\s*=\s*[""']([^""']+)[""']",
                @"<link[^>]+href\s*=\s*[""']([^""']+)[""'][^>]+rel\s*=\s*[""'](?:shortcut\s+)?icon[""']"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    html, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var href = match.Groups[1].Value;
                    if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return href;
                    if (href.StartsWith("//"))
                        return $"https:{href}";
                    if (href.StartsWith("/"))
                        return $"{baseUrl}{href}";
                    return $"{baseUrl}/{href}";
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the icons cache directory.
    /// </summary>
    public static string GetIconsDirectory()
    {
        return Path.Combine(DatabaseManager.GetRootDirectory(), "icons");
    }

    /// <summary>
    /// Gets the icon file path for a given abbreviation.
    /// </summary>
    public static string GetIconPath(string abbreviation)
    {
        return Path.Combine(GetIconsDirectory(), $"{abbreviation.ToUpperInvariant()}.ico");
    }
}
