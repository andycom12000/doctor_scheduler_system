using Scheduler.Application.Schedules;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Persistence;

/// <summary>
/// 值班表、格子與月結轉的讀寫。介面刻意淺：只回答「某月的 duty」「某段日期的 duty」，
/// 跨月與跨年的撈取規則在 Application 的 <c>SchedulingContextLoader</c>，不在這裡。
/// </summary>
public interface IScheduleRepository
{
    Task<ScheduleHeader?> FindAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    /// <summary>所有月份的標頭，依年月遞增。</summary>
    Task<IReadOnlyList<ScheduleHeader>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>新增或更新標頭。</summary>
    Task UpsertAsync(ScheduleHeader header, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Duty>> GetDutiesAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    /// <summary><paramref name="from"/> 到 <paramref name="to"/>（含）之間的值班，不分月份。給上月月尾用。</summary>
    Task<IReadOnlyList<Duty>> GetDutiesInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>某人是否有任何值班紀錄（任何月份）。<c>STAFF_HAS_DUTIES</c> 用。</summary>
    Task<bool> AnyDutyForStaffAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>某區域是否被任何值班引用。<c>AREA_IN_USE</c> 用。</summary>
    Task<bool> AnyDutyForAreaAsync(string areaId, CancellationToken cancellationToken = default);

    /// <summary>設定一格。<paramref name="staffId"/> 為 null 即清空。標頭必須已存在。</summary>
    Task SetDutyAsync(YearMonth yearMonth, string areaId, DateOnly date, string? staffId, CancellationToken cancellationToken = default);

    /// <summary>整月的格子全部換成 <paramref name="duties"/>。套用變體用。</summary>
    Task ReplaceDutiesAsync(YearMonth yearMonth, IReadOnlyList<Duty> duties, CancellationToken cancellationToken = default);

    /// <summary>某月發布時結算出的月結轉（該月的輸出）。未發布或未結算為空。</summary>
    Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    /// <summary>重複發布整份覆寫。</summary>
    Task ReplaceCarryOverAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// 某月第一次發布時凍結的上月月結轉（該月的輸入，ADR-0004）。
    /// 是否已凍結看標頭的 <see cref="ScheduleHeader.PublishedAt"/>，不看這裡有沒有列——
    /// 上月未發布時凍結的就是空清單。
    /// </summary>
    Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAppliedAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    /// <summary>寫入凍結的上月月結轉。只該在第一次發布時呼叫；要不要重拍由呼叫端決定。</summary>
    Task ReplaceCarryOverAppliedAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken cancellationToken = default);
}
