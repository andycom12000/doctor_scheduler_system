using Scheduler.Domain.Model;

namespace Scheduler.Application.Persistence;

/// <summary>不可排班日。獨立於值班表存在（ADR-0001），沒有月份標頭。</summary>
public interface IBlockedDayRepository
{
    Task<IReadOnlyList<BlockedDay>> ListAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default);

    /// <summary>某人某月已登記幾天。登記上限的驗證用。</summary>
    Task<int> CountAsync(string staffId, YearMonth yearMonth, CancellationToken cancellationToken = default);

    /// <summary>登記。已存在時不動作。</summary>
    Task AddAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default);

    /// <summary>取消登記。不存在時不動作。</summary>
    Task RemoveAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default);
}
