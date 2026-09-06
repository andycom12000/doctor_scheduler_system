namespace Scheduler.Application.Persistence;

/// <summary>
/// 一次用例裡所有 repository 的變更一起提交。Repository 的寫入方法只登記變更、不落盤，
/// 由 handler 在最後呼叫 <see cref="CommitAsync"/>；發布（值班表標頭 + 月結轉 + 凍結的上月月結轉）
/// 這種跨表寫入因此天然是一個交易。
/// </summary>
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
