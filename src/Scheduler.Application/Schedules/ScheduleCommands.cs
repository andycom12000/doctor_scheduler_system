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
public sealed record PublishResult(ScheduleStatus Status, DateTimeOffset PublishedAt, int Revision, int PublishedVersion, IReadOnlyList<CarryOverEntry> CarryOver);

/// <summary>
/// 值班表的寫入路徑：指派／清空一格、對調兩格、發布。
/// 打破硬約束不拒絕（契約 <c>setDuty</c>），包括同一人同一天排在兩區（X1，#68）：多步調整的中間狀態要能存。
/// 把關在發布：X1 不能用 acknowledgeViolations 略過。
/// 違規清單一律由 Domain 的檢查器重算，這裡不重寫任何約束語義。
/// </summary>
public sealed class ScheduleCommands
{
    private readonly IScheduleRepository _schedules;
    private readonly ISolverJobRepository _jobs;
    private readonly SchedulingContextLoader _loader;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ScheduleCommands(IScheduleRepository schedules, ISolverJobRepository jobs, SchedulingContextLoader loader, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _schedules = schedules;
        _jobs = jobs;
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
            after.Add(new Duty(cell.AreaId, cell.Date, staffId));
        }

        // 回應內容在 commit 之前算好：落盤之後就不該再有任何會失敗的步驟
        var violations = Recheck(loaded, after);
        var header = loaded.Header ?? ScheduleHeader.NewDraft(month);
        header = header with { Revision = header.Revision + 1 };
        await _schedules.UpsertAsync(header, cancellationToken);
        await _schedules.SetDutyAsync(month, cell.AreaId, cell.Date, staffId, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        return new MutationResult(header.Revision, new[] { CellOf(cell, staffId) }, violations);
    }

    /// <summary>對調兩格。該月尚無值班表時 404，不會憑空建表。同人同日兩區照常寫入，由違規清單的 X1 回報。</summary>
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
        var after = others.ToList();
        if (staffB is not null)
        {
            after.Add(new Duty(a.AreaId, a.Date, staffB));
        }

        if (staffA is not null)
        {
            after.Add(new Duty(b.AreaId, b.Date, staffA));
        }

        var violations = Recheck(loaded, after);
        var header = loaded.Header! with { Revision = loaded.Header!.Revision + 1 };
        await _schedules.UpsertAsync(header, cancellationToken);
        await _schedules.SetDutyAsync(month, a.AreaId, a.Date, staffB, cancellationToken);
        await _schedules.SetDutyAsync(month, b.AreaId, b.Date, staffA, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        return new MutationResult(header.Revision, new[] { CellOf(a, staffB), CellOf(b, staffA) }, violations);
    }

    /// <summary>
    /// 發布：同人同日兩區（X1，<see cref="DoubleBookingGuard"/>）一律 409 <c>DOUBLE_BOOKING_PRESENT</c>、不能確認略過；
    /// 其他硬違規未確認時 409。結算本月的月結轉（重複發布整份覆寫）；
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
        DoubleBookingGuard.EnsureNone(validation, "發布");

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
        var published = header with { Status = ScheduleStatus.Published, Revision = header.Revision + 1, PublishedAt = now, PublishedVersion = header.PublishedVersion + 1 };
        var carryOver = CarryOverSettlement.Settle(loaded.Context);

        await _schedules.UpsertAsync(published, cancellationToken);
        await _schedules.ReplaceCarryOverAsync(month, carryOver, cancellationToken);
        if (firstPublish)
        {
            await _schedules.ReplaceCarryOverAppliedAsync(month, loaded.Context.CarryOver, cancellationToken);
        }

        await _unitOfWork.CommitAsync(cancellationToken);
        return new PublishResult(published.Status, now, published.Revision, published.PublishedVersion, carryOver);
    }

    /// <summary>
    /// 套用變體為草稿：整月的格子全部換成變體的值班清單；該月尚無值班表時建一份。
    /// 已發布的不可整份套用（幾乎一定是誤操作）409；job 或變體不存在 404；變體不是這個月的 422。
    /// </summary>
    public async Task<ScheduleView> ApplyVariantAsync(YearMonth month, string jobId, string variantId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.FindAsync(jobId, cancellationToken)
            ?? throw SchedulerException.NotFound($"找不到求解工作 {jobId}");
        var variant = await _jobs.FindVariantAsync(jobId, variantId, cancellationToken)
            ?? throw SchedulerException.NotFound($"求解工作 {jobId} 沒有變體 {variantId}");
        if (job.YearMonth != month)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"變體 {variantId} 是 {job.YearMonth} 的，不能套用到 {month}");
        }

        var loaded = await _loader.LoadAsync(month, cancellationToken);
        if (loaded.Header?.Status == ScheduleStatus.Published)
        {
            throw new SchedulerException(ErrorCode.ScheduleAlreadyPublished, $"{month} 的值班表已發布，不可整份套用變體；要改請逐格改");
        }

        // 變體是求解當下的快照，人員或區域之後可能被刪（變體裡的值班不算「有值班紀錄」，刪得掉）。
        // 帶著找不到的 id 落盤會讓該月之後每個讀取都 500、連清空格子都做不到，所以套用時重新驗一次
        var staffIds = loaded.Context.Staff.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var areaIds = loaded.Context.Areas.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var stale = variant.Duties.FirstOrDefault(d => !staffIds.Contains(d.StaffId) || !areaIds.Contains(d.AreaId));
        if (stale is not null)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest,
                $"變體 {variantId} 含已不存在的人員或區域（{stale.StaffId} / {stale.AreaId}），請重新求解");
        }

        var header = loaded.Header ?? ScheduleHeader.NewDraft(month);
        header = header with { Revision = header.Revision + 1 };
        await _schedules.UpsertAsync(header, cancellationToken);
        await _schedules.ReplaceDutiesAsync(month, variant.Duties, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        var ctx = loaded.Context;
        return new ScheduleView(
            month, header.Status, header.Revision, header.PublishedVersion, header.PublishedAt, month.DayCount,
            ctx.Staff.Count(s => s.Status == StaffStatus.Active),
            ctx.Areas,
            variant.Duties
                .OrderBy(d => d.Date).ThenBy(d => d.AreaId, StringComparer.Ordinal)
                .Select(d => new DutyView(d.AreaId, d.Date, d.StaffId, CellKey.Area(d)))
                .ToArray());
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
