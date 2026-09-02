namespace Scheduler.Domain.Model;

/// <summary>公平性點數要查哪一張表。NP 不計，為 null。</summary>
public enum PointType
{
    A,
    B,
}

/// <summary>
/// 身分組：公平性只在組內比較，不跨組。成員關係只由 <see cref="Rank.GroupCode"/> 表達，
/// 這裡不重複列身分代碼——雙向連結會讓整份設定送進不一致的資料。
/// </summary>
public sealed record RankGroup(string Code, string Name);

/// <summary>
/// 身分：人員的資歷別。決定可值區域類型（資格矩陣）、額度點數上限與點數類型。
/// </summary>
/// <param name="QuotaCap">
/// 額度點數上限，硬門檻。<c>null</c> 專屬 NP，代表不計。
/// R6 在這裡放預設值，當月實際值由 <see cref="MonthlyOverride"/> 覆寫。
/// </param>
public sealed record Rank(string Code, string Name, string GroupCode, int? QuotaCap, PointType? PointType);

public enum StaffStatus
{
    Active,
    Inactive,
}

/// <summary>人員。多數是醫師，也包含 NP，所以統稱用中性的「人員」。</summary>
public sealed record Staff(string Id, string EmployeeNo, string Name, string RankCode, StaffStatus Status = StaffStatus.Active);
