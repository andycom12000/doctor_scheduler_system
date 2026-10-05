namespace Scheduler.Persistence.Entities;

/// <summary>
/// 應用程式自己的一次性標記（key/value）。目前只有 <see cref="Seed.RosterImporter.ImportedMetaKey"/>：
/// 名冊檔匯入過就記下，之後不論人員表變成怎樣都不再匯入（#82）。
/// </summary>
public sealed class AppMetaEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
