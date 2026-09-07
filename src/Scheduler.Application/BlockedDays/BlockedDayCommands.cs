using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.BlockedDays;

/// <summary>契約 <c>BlockedDayMutationResult</c>：寫入後該人本月與該日的統計。</summary>
public sealed record BlockedDayMutation(int StaffCount, int StaffRemaining, int DateCount);

/// <summary>
/// 不可排班日的寫入路徑：筆刷式點格登記與清除。不要求值班表存在（ADR-0001）。
/// 上限與 <see cref="BlockedDayQueries"/> 讀同一個常數。
/// </summary>
public sealed class BlockedDayCommands
{
    private readonly IBlockedDayRepository _blockedDays;
    private readonly IStaffRepository _staff;
    private readonly IUnitOfWork _unitOfWork;

    public BlockedDayCommands(IBlockedDayRepository blockedDays, IStaffRepository staff, IUnitOfWork unitOfWork)
    {
        _blockedDays = blockedDays;
        _staff = staff;
        _unitOfWork = unitOfWork;
    }

    /// <summary>登記一格。已登記時冪等；超過每人每月上限回 <c>BLOCKED_DAY_CAP_EXCEEDED</c>。</summary>
    public async Task<BlockedDayMutation> SetAsync(YearMonth month, string staffId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await EnsureValidAsync(month, staffId, date, cancellationToken);
        var entry = new BlockedDay(staffId, date);
        const int cap = DefaultPointRules.BlockedDayMonthlyCap;

        if (!await _blockedDays.ExistsAsync(entry, cancellationToken))
        {
            var count = await _blockedDays.CountAsync(staffId, month, cancellationToken);
            if (count >= cap)
            {
                throw new SchedulerException(
                    ErrorCode.BlockedDayCapExceeded,
                    $"{staffId} 在 {month} 的不可排班日已達上限 {cap} 天",
                    new Dictionary<string, object?> { ["staffId"] = staffId, ["cap"] = cap });
            }

            await _blockedDays.AddAsync(entry, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        return await TotalsAsync(month, staffId, date, cancellationToken);
    }

    /// <summary>清除一格。未登記時冪等。</summary>
    public async Task<BlockedDayMutation> ClearAsync(YearMonth month, string staffId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await EnsureValidAsync(month, staffId, date, cancellationToken);
        var entry = new BlockedDay(staffId, date);
        if (await _blockedDays.ExistsAsync(entry, cancellationToken))
        {
            await _blockedDays.RemoveAsync(entry, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }

        return await TotalsAsync(month, staffId, date, cancellationToken);
    }

    /// <summary>人員與日期都在路徑上：人員不存在是 404，日期不在該月是 422。</summary>
    private async Task EnsureValidAsync(YearMonth month, string staffId, DateOnly date, CancellationToken cancellationToken)
    {
        if (!month.Contains(date))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"{date:yyyy-MM-dd} 不在 {month} 裡");
        }

        _ = await _staff.FindAsync(staffId, cancellationToken)
            ?? throw SchedulerException.NotFound($"找不到人員 {staffId}");
    }

    private async Task<BlockedDayMutation> TotalsAsync(YearMonth month, string staffId, DateOnly date, CancellationToken cancellationToken)
    {
        var staffCount = await _blockedDays.CountAsync(staffId, month, cancellationToken);
        var entries = await _blockedDays.ListAsync(month, cancellationToken);
        return new BlockedDayMutation(
            staffCount,
            Math.Max(0, DefaultPointRules.BlockedDayMonthlyCap - staffCount),
            entries.Count(e => e.Date == date));
    }
}
