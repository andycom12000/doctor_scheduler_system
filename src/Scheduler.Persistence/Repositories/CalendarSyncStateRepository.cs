using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Persistence.Entities;

namespace Scheduler.Persistence.Repositories;

/// <summary>
/// 行事曆自動更新的狀態，整份 JSON 存在 <c>app_meta</c> 的一個 key（#112）。
/// 不另開資料表：這是「上次同步的結果」而不是領域資料，沒有查詢需求，也就不必動 schema。
/// </summary>
internal sealed class CalendarSyncStateRepository : ICalendarSyncStateRepository
{
    public const string MetaKey = "calendar_sync_state";

    private readonly SchedulerDbContext _db;

    public CalendarSyncStateRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<CalendarSyncState> GetAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync(_db, cancellationToken);

    public async Task SaveAsync(CalendarSyncState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new Stored(
            state.LastSuccessAt?.UtcDateTime.ToString("O"),
            state.Years.Select(y => new StoredYear(y.Year, y.Source, y.SyncedAt.UtcDateTime.ToString("O"))).ToArray(),
            state.LastError));
        var row = await _db.AppMeta.FindAsync(new object[] { MetaKey }, cancellationToken);
        if (row is null)
        {
            _db.AppMeta.Add(new AppMetaEntity { Key = MetaKey, Value = json });
        }
        else
        {
            row.Value = json;
        }
    }

    /// <summary>種子的逐日補缺要知道哪些年已經由官方資料同步過（那些年不再補內建值）。</summary>
    public static async Task<IReadOnlySet<int>> ReadSyncedYearsAsync(SchedulerDbContext db, CancellationToken cancellationToken = default) =>
        (await ReadAsync(db, cancellationToken)).Years.Select(y => y.Year).ToHashSet();

    private static async Task<CalendarSyncState> ReadAsync(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.AppMeta.AsNoTracking().SingleOrDefaultAsync(m => m.Key == MetaKey, cancellationToken);
        if (row is null)
        {
            return CalendarSyncState.Empty;
        }

        try
        {
            var s = JsonSerializer.Deserialize<Stored>(row.Value);
            if (s is null)
            {
                return CalendarSyncState.Empty;
            }

            return new CalendarSyncState(
                s.LastSuccessAt is null ? null : Parse(s.LastSuccessAt),
                (s.Years ?? Array.Empty<StoredYear>()).Select(y => new SyncedYear(y.Year, y.Source, Parse(y.SyncedAt))).ToArray(),
                s.LastError);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // 狀態只是輔助資訊；壞了就當沒有，下次同步會重寫
            return CalendarSyncState.Empty;
        }
    }

    private static DateTimeOffset Parse(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record Stored(string? LastSuccessAt, StoredYear[]? Years, string? LastError);

    private sealed record StoredYear(int Year, string Source, string SyncedAt);
}
