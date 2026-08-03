using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperDucker.Shared.Models;

/// <summary>
/// 为 PackageManifest 及相关类型生成的源生成 JSON 序列化上下文。
/// 在裁剪（trimmed）构建中必须此上下文，因为反射式序列化已被禁用。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(InstallActions))]
[JsonSerializable(typeof(UninstallActions))]
[JsonSerializable(typeof(PackageRequirements))]
internal partial class ManifestJsonContext : JsonSerializerContext { }

/// <summary>
/// .sdzip 绿色软件包的清单（manifest）模型。
/// </summary>
public class PackageManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("abbreviation")]
    public string? Abbreviation { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mainExe")]
    public string MainExe { get; set; } = string.Empty;

    [JsonPropertyName("extractSubDir")]
    public string ExtractSubDir { get; set; } = "app";

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("licenseFile")]
    public string? LicenseFile { get; set; }

    [JsonPropertyName("installActions")]
    public InstallActions? InstallActions { get; set; }

    [JsonPropertyName("uninstallActions")]
    public UninstallActions? UninstallActions { get; set; }

    [JsonPropertyName("requirements")]
    public PackageRequirements? Requirements { get; set; }

    /// <summary>
    /// 序列化为带缩进的 JSON 字符串。
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, ManifestJsonContext.Default.PackageManifest);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化。
    /// </summary>
    public static PackageManifest? FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.PackageManifest);
    }
}

public class InstallActions
{
    [JsonPropertyName("createDesktopShortcut")]
    public bool CreateDesktopShortcut { get; set; }

    [JsonPropertyName("registerToPath")]
    public bool RegisterToPath { get; set; }

    [JsonPropertyName("postInstall")]
    public string? PostInstall { get; set; }
}

public class UninstallActions
{
    [JsonPropertyName("preserveUserData")]
    public List<string>? PreserveUserData { get; set; }

    [JsonPropertyName("removeDir")]
    public bool RemoveDir { get; set; } = true;
}

public class PackageRequirements
{
    [JsonPropertyName("minWindows")]
    public string? MinWindows { get; set; }

    [JsonPropertyName("architecture")]
    public List<string>? Architecture { get; set; }

    [JsonPropertyName("dotnet")]
    public string? Dotnet { get; set; }
}
