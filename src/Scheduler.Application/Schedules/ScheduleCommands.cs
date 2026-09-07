using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Validation;

namespace Scheduler.Application.Schedules;

/// <summary>寫入的目標格，契約 <c>CellRef</c>。</summary>
public sealed record CellRef(string AreaId, DateOnly Date);

/// <summary>一次寫入後某一格的狀態。<see cref="StaffId"/> 為 null 代表已清空。契約 <c>MutatedCell</c>。</summary>
public sealed record MutatedCell(string AreaId, DateOnly Date, string? StaffId, string CellKey);

/// <summary>契約 <c>MutationResult</c>：改動後的格子與整份值班表的全量違規。</summary>
public sealed record MutationResult(int Revision, IReadOnlyList<MutatedCell> Cells, IReadOnlyList<Violation> Violations);

/// <summary>契約 <c>PublishResult</c>。</summary>
public sealed record PublishResult(ScheduleStatus Status, DateTimeOffset PublishedAt, int Revision, IReadOnlyList<CarryOverEntry> CarryOver);

/// <summary>
/// 值班表的寫入路徑：指派／清空一格、對調兩格、發布。
/// 打破硬約束不拒絕（契約 <c>setDuty</c>），唯一擋的是結構不變式「同一人同一天已在另一區」。
/// 違規清單一律由 Domain 的檢查器重算，這裡不重寫任何約束語義。
/// </summary>
public sealed class ScheduleCommands
{
    private readonly IScheduleRepository _schedules;
    private readonly SchedulingContextLoader _loader;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ScheduleCommands(IScheduleRepository schedules, SchedulingContextLoader loader, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _schedules = schedules;
        _loader = loader;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>
    /// 該月尚無值班表時自動建一份空草稿再寫入；已發布的也可以改，<c>revision</c> 照常遞增。
    /// 把同一個人再指派到他已在的那一格是冪等的（仍遞增 revision，回同樣的格子）。
    /// </summary>
    public async Task<MutationResult> SetDutyAsync(YearMonth month, CellRef cell, string? staffId, CancellationToken cancellationToken = default)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        var ctx = loaded.Context;
        EnsureCellValid(month, ctx, cell);
        if (staffId is not null && !ctx.Staff.Any(s => s.Id == staffId))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"找不到人員 {staffId}");
        }

        var after = ctx.Duties.Where(d => !(d.AreaId == cell.AreaId && d.Date == cell.Date)).ToList();
        if (staffId is not null)
        {
            EnsureNotOnDutyElsewhere(after, staffId, cell);
            after.Add(new Duty(cell.AreaId, cell.Date, staffId));
        }

        var header = loaded.Header ?? ScheduleHeader.NewDraft(month);
        header = header with { Revision = header.Revision + 1 };
        await _schedules.UpsertAsync(header, cancellationToken);
        await _schedules.SetDutyAsync(month, cell.AreaId, cell.Date, staffId, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        var violations = Recheck(loaded, after);
        return new MutationResult(header.Revision, new[] { CellOf(cell, staffId) }, violations);
    }

    /// <summary>對調兩格。該月尚無值班表時 404，不會憑空建表。兩格互為對方的來源，檢查同日另一區時把對方排除。</summary>
    public async Task<MutationResult> SwapAsync(YearMonth month, CellRef a, CellRef b, CancellationToken cancellationToken = default)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        if (!loaded.ScheduleExists)
        {
            throw SchedulerException.ScheduleNotFound(month);
        }

        var ctx = loaded.Context;
        EnsureCellValid(month, ctx, a);
        EnsureCellValid(month, ctx, b);
        if (a == b)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "對調的兩格是同一格");
        }

        var staffA = ctx.Duties.FirstOrDefault(d => d.AreaId == a.AreaId && d.Date == a.Date)?.StaffId;
        var staffB = ctx.Duties.FirstOrDefault(d => d.AreaId == b.AreaId && d.Date == b.Date)?.StaffId;

        var others = ctx.Duties.Where(d => !IsCell(d, a) && !IsCell(d, b)).ToList();
        if (staffA is not null)
        {
            EnsureNotOnDutyElsewhere(others, staffA, b);
        }

        if (staffB is not null)
        {
            EnsureNotOnDutyElsewhere(others, staffB, a);
        }

        var after = others.ToList();
        if (staffB is not null)
        {
            after.Add(new Duty(a.AreaId, a.Date, staffB));
        }

        if (staffA is not null)
        {
            after.Add(new Duty(b.AreaId, b.Date, staffA));
        }

        var header = loaded.Header! with { Revision = loaded.Header!.Revision + 1 };
        await _schedules.UpsertAsync(header, cancellationToken);
        await _schedules.SetDutyAsync(month, a.AreaId, a.Date, staffB, cancellationToken);
        await _schedules.SetDutyAsync(month, b.AreaId, b.Date, staffA, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        var violations = Recheck(loaded, after);
        return new MutationResult(header.Revision, new[] { CellOf(a, staffB), CellOf(b, staffA) }, violations);
    }

    /// <summary>
    /// 發布：仍有硬違規且未確認時 409。結算本月的月結轉（重複發布整份覆寫）；
    /// 本月第一次發布時把當下讀到的上月月結轉凍結存下（ADR-0004），之後重新發布不重拍。
    /// </summary>
    public async Task<PublishResult> PublishAsync(YearMonth month, bool acknowledgeViolations, CancellationToken cancellationToken = default)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        if (!loaded.ScheduleExists)
        {
            throw SchedulerException.ScheduleNotFound(month);
        }

        var validation = new ViolationChecker(loaded.Context).Check(loaded.Constraints);
        if (validation.HardCount > 0 && !acknowledgeViolations)
        {
            throw new SchedulerException(
                ErrorCode.HardViolationsPresent,
                $"仍有 {validation.HardCount} 條硬約束違規，需明確確認才能發布",
                new Dictionary<string, object?> { ["hardViolationCount"] = validation.HardCount });
        }

        var header = loaded.Header!;
        var firstPublish = header.PublishedAt is null;
        var now = _clock.GetUtcNow();
        var published = header with { Status = ScheduleStatus.Published, Revision = header.Revision + 1, PublishedAt = now };
        var carryOver = CarryOverSettlement.Settle(loaded.Context);

        await _schedules.UpsertAsync(published, cancellationToken);
        await _schedules.ReplaceCarryOverAsync(month, carryOver, cancellationToken);
        if (firstPublish)
        {
            await _schedules.ReplaceCarryOverAppliedAsync(month, loaded.Context.CarryOver, cancellationToken);
        }

        await _unitOfWork.CommitAsync(cancellationToken);
        return new PublishResult(published.Status, now, published.Revision, carryOver);
    }

    // ---- helpers ----

    private static void EnsureCellValid(YearMonth month, SchedulingContext ctx, CellRef cell)
    {
        if (!month.Contains(cell.Date))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"{cell.Date:yyyy-MM-dd} 不在 {month} 裡");
        }

        if (!ctx.Areas.Any(a => a.Id == cell.AreaId))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"找不到區域 {cell.AreaId}");
        }
    }

    /// <summary>結構不變式：同一人同一天最多一格。<paramref name="duties"/> 是不含目標格的清單。</summary>
    private static void EnsureNotOnDutyElsewhere(IEnumerable<Duty> duties, string staffId, CellRef target)
    {
        var elsewhere = duties.FirstOrDefault(d => d.StaffId == staffId && d.Date == target.Date && d.AreaId != target.AreaId);
        if (elsewhere is not null)
        {
            throw new SchedulerException(
                ErrorCode.StaffAlreadyOnDuty,
                $"{staffId} 在 {target.Date:yyyy-MM-dd} 已排在 {elsewhere.AreaId}",
                new Dictionary<string, object?> { ["areaId"] = elsewhere.AreaId, ["staffId"] = staffId, ["date"] = target.Date.ToString("yyyy-MM-dd") });
        }
    }

    private static bool IsCell(Duty d, CellRef c) => d.AreaId == c.AreaId && d.Date == c.Date;

    private static MutatedCell CellOf(CellRef cell, string? staffId) =>
        new(cell.AreaId, cell.Date, staffId, CellKey.Area(cell.AreaId, cell.Date));

    /// <summary>用改動後的值班清單重跑檢查器。不能 <c>with</c> 複製 context：lazy 索引會黏在舊清單上。</summary>
    private static IReadOnlyList<Violation> Recheck(LoadedContext loaded, IReadOnlyList<Duty> duties)
    {
        var c = loaded.Context;
        var ctx = new SchedulingContext(
            c.Month, c.Calendar, c.Areas, c.Staff, c.Ranks, c.Eligibility, c.PointRules, c.Override,
            duties, c.PreviousMonthDuties, c.BlockedDays, c.CarryOver);
        return new ViolationChecker(ctx).Check(loaded.Constraints).Violations;
    }
}
