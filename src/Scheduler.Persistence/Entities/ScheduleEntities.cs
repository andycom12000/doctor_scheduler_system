namespace Scheduler.Persistence.Entities;

// 值班表相關的 entity。與 Domain record 互轉的程式碼在 Mapping/，這裡只有形狀。
// 表名與欄名由 SchedulerDbContext 的 snake_case 慣例產生：ScheduleEntity → schedule、PublishedAt → published_at。

/// <summary>一列一個月。<c>(year, month)</c> 為自然主鍵，無代理鍵（ARCHITECTURE §5）。</summary>
public sealed class ScheduleEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; set; } = "";
    public int Revision { get; set; }
    public int PublishedVersion { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>一列一格值班。<c>(year, month, area_id, date)</c> 唯一。</summary>
public sealed class DutyEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string AreaId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string StaffId { get; set; } = "";
}

/// <summary>不可排班日。不外鍵到 schedule（ADR-0001）。</summary>
public sealed class BlockedDayEntity
{
    public string StaffId { get; set; } = "";
    public DateOnly Date { get; set; }
}

/// <summary>某月發布時結算出的月結轉（該月的輸出）。重複發布整份覆寫。</summary>
public sealed class CarryOverEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string StaffId { get; set; } = "";
    public int Points { get; set; }
}

/// <summary>某月第一次發布時凍結的上月月結轉（該月的輸入）。與 <see cref="CarryOverEntity"/> 分表，理由見 ADR-0004。</summary>
public sealed class CarryOverAppliedEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string StaffId { get; set; } = "";
    public int Points { get; set; }
}
