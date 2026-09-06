namespace Scheduler.Persistence.Entities;

// 設定類正規化拆表，不存 JSON blob（ARCHITECTURE §5）。列舉一律存字串，資料不綁死在本程式的序列化格式。
// SortOrder 保留文件裡的順序：契約的陣列順序有意義（區域 A、B、C、ICU、總值；約束 H1..H7）。

public sealed class StaffEntity
{
    public string Id { get; set; } = "";
    public string EmployeeNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string RankCode { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class AreaTypeEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class AreaEntity
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string AreaTypeCode { get; set; } = "";
    public int RequiredPerDay { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RankGroupEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class RankEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupCode { get; set; } = "";
    public int? QuotaCap { get; set; }
    public string? PointType { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>資格矩陣的一格。true 與 false 都存，矩陣才能原樣讀回。</summary>
public sealed class EligibilityEntity
{
    public string RankCode { get; set; } = "";
    public string AreaTypeCode { get; set; } = "";
    public bool Eligible { get; set; }
}

/// <summary>點數規則的純量部分。永遠只有一列，<see cref="Id"/> 固定為 1。</summary>
public sealed class PointRuleEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int QuotaWeekday { get; set; }
    public int QuotaHoliday { get; set; }
    public int SaturdayBonusPoints { get; set; }
    public int SaturdayBonusWindowDays { get; set; }
}

/// <summary>公平性點數查表的一列：點數類型 × 當日 × 隔日 → 點數。</summary>
public sealed class FairnessPointTableEntity
{
    public string PointType { get; set; } = "";
    public string Today { get; set; } = "";
    public string Tomorrow { get; set; } = "";
    public int Points { get; set; }
}

/// <summary>一條約束的身分與參數。不叫 <c>constraint</c>——那是 SQL 關鍵字，手查資料庫會踩到。</summary>
public sealed class ConstraintDefinitionEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Primitive { get; set; } = "";
    public string Severity { get; set; } = "";
    public bool Enabled { get; set; }
    public int Weight { get; set; }
    public string? Metric { get; set; }
    public int? ParamDays { get; set; }
    public int? ParamCap { get; set; }
    public string? ParamDirection { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>約束範圍的一個值。<see cref="Dimension"/> 是 <see cref="ScopeDimension"/> 之一。</summary>
public sealed class ConstraintScopeEntryEntity
{
    public string ConstraintCode { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary><see cref="ConstraintScopeEntryEntity.Dimension"/> 的合法值。</summary>
public static class ScopeDimension
{
    public const string Rank = "rank";
    public const string ExemptRank = "exempt_rank";
    public const string AreaType = "area_type";
    public const string DayKind = "day_kind";
}

/// <summary>逐月覆寫，一列一個身分。目前只有額度上限。</summary>
public sealed class MonthlyOverrideEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string RankCode { get; set; } = "";
    public int QuotaCap { get; set; }
}

/// <summary>行事曆例外日：國定假日、補班日、使用者覆寫。週六日不存。</summary>
public sealed class CalendarDayEntity
{
    public DateOnly Date { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicHoliday { get; set; }
    public bool IsMakeUpWorkday { get; set; }
    public string? HolidayName { get; set; }
    public bool Overridden { get; set; }
}
