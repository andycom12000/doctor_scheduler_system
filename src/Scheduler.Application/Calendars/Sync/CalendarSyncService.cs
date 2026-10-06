using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Calendars.Sync;

/// <summary>
/// 行事曆資料來源。實作在 Api（HTTP）；Application 只認介面，網路一律可以換成假的。
/// 回傳的是已驗證（365／366 列、連續、星期對得上）的整年資料；失敗丟 <see cref="CalendarSyncException"/>。
/// </summary>
public interface ICalendarSource
{
    /// <summary>寫進狀態與契約的來源代碼：<c>official</c>（人事總處）或 <c>mirror</c>（社群鏡像）。</summary>
    string Name { get; }

    /// <summary>備援來源只在主來源「失敗」時使用；主來源說「尚未公告」不算失敗。</summary>
    bool IsFallback { get; }

    Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken);
}

/// <summary>同步紀錄（<c>data/calendar-sync.log</c>）。實作必須吞掉自己的例外。</summary>
public interface ICalendarSyncLog
{
    void Write(string message);
}

/// <summary>
/// 本次程式執行期間的同步進度（singleton，記憶體內）。前端啟動後輪詢它，等 <see cref="Running"/> 變 false 再決定要不要 toast。
/// <see cref="Enabled"/> 為 false（測試、e2e、未開自動更新）時一開始就不是 running。
/// </summary>
public sealed class CalendarSyncProgress
{
    private readonly object _gate = new();
    private bool _running;
    private DateTimeOffset? _finishedAt;
    private IReadOnlyList<int> _updatedYears = Array.Empty<int>();

    public CalendarSyncProgress(bool enabled)
    {
        Enabled = enabled;
        _running = enabled;
    }

    public bool Enabled { get; }

    public (bool Running, DateTimeOffset? FinishedAt, IReadOnlyList<int> UpdatedYears) Snapshot()
    {
        lock (_gate)
        {
            return (_running, _finishedAt, _updatedYears);
        }
    }

    public void Finish(DateTimeOffset at, IReadOnlyList<int> updatedYears)
    {
        lock (_gate)
        {
            _running = false;
            _finishedAt = at;
            _updatedYears = updatedYears;
        }
    }
}

public enum YearSyncOutcome
{
    Updated,
    Unchanged,
    NotPublished,
    Failed,
}

public sealed record CalendarSyncRunResult(IReadOnlyDictionary<int, YearSyncOutcome> Years)
{
    public IReadOnlyList<int> UpdatedYears => Years.Where(p => p.Value == YearSyncOutcome.Updated).Select(p => p.Key).OrderBy(y => y).ToArray();
}

/// <summary>
/// 自動更新的用例：今年、明年與資料庫裡已有資料的年份（不含更晚的），逐年抓、驗證、和現有資料逐日比對、只寫有差異的。
///
/// 寫入規則：
/// <list type="bullet">
/// <item>使用者覆寫（<c>Overridden</c>）的列一律不動。</item>
/// <item>「差異」只看 isHoliday／isPublicHoliday／isMakeUpWorkday。名稱不同不算：內建資料的名稱
/// （例如「小年夜補假」）比官方檔的備註（「補假」）更具體，名稱互異不值得蓋掉或吵使用者。
/// 結構一致的列保留原樣；新增或結構有變的列才寫官方名稱。</item>
/// <item>官方說該日是純平日／純週末、但資料庫有一筆非覆寫例外：刪列。</item>
/// <item>整年驗證通過才寫；任何一步失敗那一年整份不動。每年一次 commit。</item>
/// <item>成功同步過的年份記進狀態，種子的逐日補缺因此不再把內建值補回該年（見 DefaultDataSeeder）。</item>
/// </list>
/// 主來源「尚未公告」不用備援（備援是主來源故障時才用，且個人維護的鏡像可能先放只有週末的空殼）。
/// </summary>
public sealed class CalendarSyncService
{
    private readonly ICalendarRepository _calendar;
    private readonly ICalendarSyncStateRepository _state;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadOnlyList<ICalendarSource> _sources;
    private readonly ICalendarSyncLog _log;
    private readonly TimeProvider _time;

    public CalendarSyncService(
        ICalendarRepository calendar,
        ICalendarSyncStateRepository state,
        IUnitOfWork unitOfWork,
        IEnumerable<ICalendarSource> sources,
        ICalendarSyncLog log,
        TimeProvider time)
    {
        _calendar = calendar;
        _state = state;
        _unitOfWork = unitOfWork;
        _sources = sources.ToArray();
        _log = log;
        _time = time;
    }

    public async Task<CalendarSyncRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var thisYear = _time.GetLocalNow().Year;
        var existing = await _calendar.GetExceptionsAsync(new DateOnly(2000, 1, 1), new DateOnly(thisYear + 1, 12, 31), cancellationToken);
        var years = existing.Select(e => e.Day.Date.Year)
            .Append(thisYear).Append(thisYear + 1)
            .Distinct().OrderBy(y => y).ToArray();

        var outcomes = new Dictionary<int, YearSyncOutcome>();
        var synced = new Dictionary<int, SyncedYear>();
        var errors = new List<string>();
        foreach (var year in years)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (outcome, source) = await SyncYearAsync(year, cancellationToken);
                outcomes[year] = outcome;
                if (source is not null)
                {
                    synced[year] = new SyncedYear(year, source, _time.GetUtcNow());
                }

                if (outcome == YearSyncOutcome.Failed)
                {
                    errors.Add($"{year} 年更新失敗");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 例如資料庫寫入失敗：這一年不動，其他年照跑
                outcomes[year] = YearSyncOutcome.Failed;
                errors.Add($"{year} 年更新失敗");
                _log.Write($"{year}: 寫入失敗：{ex.GetType().Name}: {ex.Message}");
            }
        }

        var previous = await _state.GetAsync(cancellationToken);
        var merged = previous.Years.Where(y => !synced.ContainsKey(y.Year)).Concat(synced.Values).OrderBy(y => y.Year).ToArray();
        var next = new CalendarSyncState(
            synced.Count > 0 ? _time.GetUtcNow() : previous.LastSuccessAt,
            merged,
            errors.Count > 0 ? string.Join("；", errors) : null);
        await _state.SaveAsync(next, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        return new CalendarSyncRunResult(outcomes);
    }

    private async Task<(YearSyncOutcome Outcome, string? Source)> SyncYearAsync(int year, CancellationToken cancellationToken)
    {
        IReadOnlyList<OfficialDay>? days = null;
        string? sourceName = null;
        var failedBefore = false;
        foreach (var source in _sources.OrderBy(s => s.IsFallback))
        {
            if (source.IsFallback && !failedBefore)
            {
                continue;
            }

            try
            {
                days = await source.FetchYearAsync(year, cancellationToken);
                sourceName = source.Name;
                break;
            }
            catch (CalendarSyncException ex) when (ex.Kind == CalendarSyncFailure.NotPublished)
            {
                _log.Write($"{year}: {source.Name} 尚未公告：{ex.Message}");
                if (!source.IsFallback)
                {
                    return (YearSyncOutcome.NotPublished, null);
                }

                return (YearSyncOutcome.Failed, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failedBefore = true;
                _log.Write($"{year}: {source.Name} 取得失敗：{ex.GetType().Name}: {ex.Message}");
            }
        }

        if (days is null)
        {
            return (YearSyncOutcome.Failed, null);
        }

        var target = OfficialCalendar.ToExceptions(days);
        if (target.Count == 0)
        {
            // 台灣每年 1/1 一定放假，真正的年度檔不可能一個例外日都沒有。這是只有週末的空殼（例如備援鏡像在
            // 官方公告前先放的骨架）：照單全收會把該年全部非覆寫的內建假日刪光，所以整份拒絕。
            _log.Write($"{year}: {sourceName} 的資料沒有任何國定假日或補班日，視為空殼、整份不寫");
            return (YearSyncOutcome.Failed, null);
        }

        var rows = (await _calendar.GetExceptionsAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken))
            .ToDictionary(e => e.Day.Date);

        var changed = 0;
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            rows.TryGetValue(d, out var row);
            if (row is { Overridden: true })
            {
                continue;
            }

            target.TryGetValue(d, out var want);
            if (want is null && row is null)
            {
                continue;
            }

            if (want is null)
            {
                await _calendar.RemoveAsync(d, cancellationToken);
                changed++;
            }
            else if (row is null || !SameFacts(row.Day, want))
            {
                await _calendar.UpsertAsync(new CalendarException(want, Overridden: false), cancellationToken);
                changed++;
            }
        }

        if (changed > 0)
        {
            await _unitOfWork.CommitAsync(cancellationToken);
            _log.Write($"{year}: 已更新 {changed} 天（{sourceName}）");
            return (YearSyncOutcome.Updated, sourceName);
        }

        return (YearSyncOutcome.Unchanged, sourceName);
    }

    private static bool SameFacts(CalendarDay a, CalendarDay b) =>
        a.IsHoliday == b.IsHoliday && a.IsPublicHoliday == b.IsPublicHoliday && a.IsMakeUpWorkday == b.IsMakeUpWorkday;
}

/// <summary><c>GET /calendars/sync-status</c>。</summary>
public sealed record CalendarSyncStatusView(
    bool Enabled,
    bool Running,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<int> UpdatedYears,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    IReadOnlyList<SyncedYear> Years);

public sealed class CalendarSyncQueries
{
    private readonly ICalendarSyncStateRepository _state;
    private readonly CalendarSyncProgress _progress;

    public CalendarSyncQueries(ICalendarSyncStateRepository state, CalendarSyncProgress progress)
    {
        _state = state;
        _progress = progress;
    }

    public async Task<CalendarSyncStatusView> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await _state.GetAsync(cancellationToken);
        var (running, finishedAt, updated) = _progress.Snapshot();
        return new CalendarSyncStatusView(_progress.Enabled, running, finishedAt, updated, state.LastSuccessAt, state.LastError, state.Years);
    }
}
