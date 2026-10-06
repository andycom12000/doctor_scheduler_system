namespace Scheduler.Application.Persistence;

public sealed record SyncedYear(int Year, string Source, DateTimeOffset SyncedAt);

/// <summary>行事曆自動更新跨啟動保存的狀態（<c>app_meta</c>，#112）。</summary>
public sealed record CalendarSyncState(
    DateTimeOffset? LastSuccessAt,
    IReadOnlyList<SyncedYear> Years,
    string? LastError)
{
    public static CalendarSyncState Empty { get; } = new(null, Array.Empty<SyncedYear>(), null);
}

public interface ICalendarSyncStateRepository
{
    Task<CalendarSyncState> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>登記變更、不落盤（與其他 repository 一致，由 <see cref="IUnitOfWork"/> 提交）。</summary>
    Task SaveAsync(CalendarSyncState state, CancellationToken cancellationToken = default);
}
