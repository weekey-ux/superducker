namespace SuperDucker.Shared.Models;

/// <summary>
/// 标签页实体：用于对应用/网址进行分组展示。
/// </summary>
public class TabEntry
{
    public int Id { get; set; }

    /// <summary>
    /// 标签页名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 标签页显示排序。
    /// </summary>
    public int SortOrder { get; set; }
}
