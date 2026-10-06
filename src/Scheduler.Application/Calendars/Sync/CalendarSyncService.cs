using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
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

    /// <summary>備援來源只在主來源「失敗」時使用，而且只准新增／修改、不准刪除。</summary>
    bool IsFallback { get; }

    Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken);
}

/// <summary>同步紀錄（<c>data/calendar-sync.log</c>）。實作必須吞掉自己的例外。</summary>
public interface ICalendarSyncLog
{
    void Write(string message);
}

/// <summary>
/// 同步用的一次性 scope：自己的 repository 與 unit of work，用完就丟。每年一個 scope、一次 commit，
/// 失敗就整個丟掉——EF 的 SaveChanges 失敗不會清 change tracker，共用一個 context 會讓殘留變更被後面的 commit 帶出去。
/// Application 零套件相依，所以仿 <c>ISolverScopeFactory</c>，由 Api 以 DI scope 實作。
/// </summary>
public interface ICalendarSyncScope : IDisposable
{
    ICalendarRepository Calendar { get; }

    ICalendarSyncStateRepository State { get; }

    IScheduleRepository Schedules { get; }

    IUnitOfWork UnitOfWork { get; }
}

public interface ICalendarSyncScopeFactory
{
    ICalendarSyncScope Create();
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
    private IReadOnlyList<string> _affectedPublishedMonths = Array.Empty<string>();
    private CalendarSyncFailureKind? _failureKind;

    public CalendarSyncProgress(bool enabled)
    {
        Enabled = enabled;
        _running = enabled;
    }

    public bool Enabled { get; }

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public (bool Running, DateTimeOffset? FinishedAt, IReadOnlyList<int> UpdatedYears, IReadOnlyList<string> AffectedPublishedMonths, CalendarSyncFailureKind? FailureKind) Snapshot()
    {
        lock (_gate)
        {
            return (_running, _finishedAt, _updatedYears, _affectedPublishedMonths, _failureKind);
        }
    }

    /// <summary>
    /// 重試用：沒開、或已經在跑就回 false（冪等，呼叫端回目前狀態即可）；否則標成 running、清掉上一輪結果並回 true。
    /// 啟動那一輪不走這裡——<see cref="Enabled"/> 時建構子就已經是 running，前端永遠不會在空檔看到「閒置」。
    /// </summary>
    public bool TryBegin()
    {
        lock (_gate)
        {
            if (!Enabled || _running)
            {
                return false;
            }

            _running = true;
            _finishedAt = null;
            _updatedYears = Array.Empty<int>();
            _affectedPublishedMonths = Array.Empty<string>();
            _failureKind = null;
            return true;
        }
    }

    public void Finish(
        DateTimeOffset at,
        IReadOnlyList<int> updatedYears,
        IReadOnlyList<string> affectedPublishedMonths,
        CalendarSyncFailureKind? failureKind = null)
    {
        lock (_gate)
        {
            _running = false;
            _finishedAt = at;
            _updatedYears = updatedYears;
            _affectedPublishedMonths = affectedPublishedMonths;
            _failureKind = failureKind;
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

/// <summary>
/// 這一輪同步失敗的性質：<see cref="Unreachable"/> 完全連不上網（主來源與備援都是網路層錯誤），
/// 前端可讓使用者「先用現有資料」；<see cref="Failed"/> 連得上但中途失敗（驗證不過、寫入失敗…），只能重試。
/// </summary>
public enum CalendarSyncFailureKind
{
    Unreachable,
    Failed,
}

public sealed record CalendarSyncRunResult(
    IReadOnlyDictionary<int, YearSyncOutcome> Years,
    IReadOnlyList<string> AffectedPublishedMonths,
    CalendarSyncFailureKind? FailureKind = null)
{
    public IReadOnlyList<int> UpdatedYears => Years.Where(p => p.Value == YearSyncOutcome.Updated).Select(p => p.Key).OrderBy(y => y).ToArray();
}

/// <summary>
/// 自動更新的用例：今年、明年與資料庫裡已有資料的年份（不含更晚的），逐年抓、驗證、和現有資料逐日比對、只寫有差異的。
///
/// 寫入規則：
/// <list type="bullet">
/// <item>使用者覆寫（<c>Overridden</c>）的列一律不動；寫入時 repository 再確認一次，擋掉比對與寫入之間的競態。</item>
/// <item>「差異」只看 isHoliday／isPublicHoliday／isMakeUpWorkday。名稱不同不算：內建資料的名稱
/// （例如「小年夜補假」）比官方檔的備註（「補假」）更具體，名稱互異不值得蓋掉或吵使用者。
/// 結構一致的列保留原樣；新增或結構有變的列才寫官方名稱。</item>
/// <item>官方說該日是純平日／純週末、但資料庫有一筆非覆寫例外：刪列。例外：週末放假而無備註的日子，
/// 既有的國定假日列不刪（來源漏填週末國定假日備註時，不能把它刪掉）。備援來源一律不准刪。</item>
/// <item>整年驗證通過才寫；任何一步失敗那一年整份不動。每年一個 scope、一次 commit，失敗整個丟掉；狀態最後用新的 scope 存。</item>
/// <item>成功同步過的年份記進狀態，種子的逐日補缺因此不再把內建值補回該年（見 DefaultDataSeeder）。</item>
/// </list>
/// 主來源「尚未公告」只對還沒到的年份成立；今年與更早、或曾同步成功過的年份找不到，視為故障（會退備援）。
/// 未來年份的「尚未公告」不用備援：個人維護的鏡像可能先放只有週末的空殼。
/// </summary>
public sealed class CalendarSyncService
{
    private readonly ICalendarSyncScopeFactory _scopes;
    private readonly IReadOnlyList<ICalendarSource> _sources;
    private readonly ICalendarSyncLog _log;
    private readonly TimeProvider _time;

    public CalendarSyncService(
        ICalendarSyncScopeFactory scopes,
        IEnumerable<ICalendarSource> sources,
        ICalendarSyncLog log,
        TimeProvider time)
    {
        _scopes = scopes;
        _sources = sources.ToArray();
        _log = log;
        _time = time;
    }

    public async Task<CalendarSyncRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var thisYear = _time.GetLocalNow().Year;
        int[] years;
        CalendarSyncState previous;
        using (var scope = _scopes.Create())
        {
            var existing = await scope.Calendar.GetExceptionsAsync(new DateOnly(2000, 1, 1), new DateOnly(thisYear + 1, 12, 31), cancellationToken);
            years = existing.Select(e => e.Day.Date.Year).Append(thisYear).Append(thisYear + 1).Distinct().OrderBy(y => y).ToArray();
            previous = await scope.State.GetAsync(cancellationToken);
        }

        var knownYears = previous.Years.Select(y => y.Year).ToHashSet();
        var outcomes = new Dictionary<int, YearSyncOutcome>();
        var synced = new Dictionary<int, SyncedYear>();
        var unreachableYears = new HashSet<int>();
        var affected = new SortedSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var year in years)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var mustExist = year <= thisYear || knownYears.Contains(year);
                var (outcome, source, months) = await SyncYearAsync(year, mustExist, unreachableYears, cancellationToken);
                outcomes[year] = outcome;
                if (source is not null)
                {
                    synced[year] = new SyncedYear(year, source, _time.GetUtcNow());
                }

                foreach (var m in months)
                {
                    affected.Add(m);
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
                // 例如資料庫寫入失敗：那一年的 scope 已整個丟掉（沒有任何殘留），其他年照跑
                outcomes[year] = YearSyncOutcome.Failed;
                errors.Add($"{year} 年更新失敗");
                _log.Write($"{year}: 寫入失敗：{ex.GetType().Name}: {ex.Message}");
            }
        }

        using (var scope = _scopes.Create())
        {
            var latest = await scope.State.GetAsync(cancellationToken);
            var merged = latest.Years.Where(y => !synced.ContainsKey(y.Year)).Concat(synced.Values).OrderBy(y => y.Year).ToArray();
            await scope.State.SaveAsync(
                new CalendarSyncState(
                    synced.Count > 0 ? _time.GetUtcNow() : latest.LastSuccessAt,
                    merged,
                    errors.Count > 0 ? string.Join("；", errors) : null),
                cancellationToken);
            await scope.UnitOfWork.CommitAsync(cancellationToken);
        }

        // 有失敗的年份：全部都是「完全連不上」才算 unreachable；只要有一年是連得上卻失敗，整體就是 failed
        var failedYears = outcomes.Where(p => p.Value == YearSyncOutcome.Failed).Select(p => p.Key).ToArray();
        CalendarSyncFailureKind? failureKind = failedYears.Length == 0
            ? null
            : failedYears.All(unreachableYears.Contains) ? CalendarSyncFailureKind.Unreachable : CalendarSyncFailureKind.Failed;

        return new CalendarSyncRunResult(outcomes, affected.ToArray(), failureKind);
    }

    /// <summary>
    /// 網路層的錯誤（DNS、連線、TLS、proxy、逾時）：連都連不上。伺服器有回應但狀態碼不對、
    /// 下載到了但驗證失敗，都是「連得上、但失敗」，不算。
    /// </summary>
    internal static bool IsNetworkError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        TaskCanceledException or TimeoutException => true,
        System.Security.Authentication.AuthenticationException => true,
        _ => ex.InnerException is not null && IsNetworkError(ex.InnerException),
    };

    private async Task<(YearSyncOutcome Outcome, string? Source, IReadOnlyList<string> AffectedPublishedMonths)> SyncYearAsync(
        int year, bool mustExist, ISet<int> unreachableYears, CancellationToken cancellationToken)
    {
        IReadOnlyList<OfficialDay>? days = null;
        ICalendarSource? chosen = null;
        var primaryFailed = false;
        var attempts = 0;
        var networkFailures = 0;
        foreach (var source in _sources.OrderBy(s => s.IsFallback))
        {
            if (source.IsFallback && !primaryFailed)
            {
                continue;
            }

            try
            {
                days = await source.FetchYearAsync(year, cancellationToken);
                chosen = source;
                break;
            }
            catch (CalendarSyncException ex) when (ex.Kind == CalendarSyncFailure.NotPublished)
            {
                _log.Write($"{year}: {source.Name} 尚未公告：{ex.Message}");
                attempts++; // 連得上的回應，不算網路錯誤
                if (source.IsFallback)
                {
                    return (YearSyncOutcome.Failed, null, Array.Empty<string>());
                }

                if (!mustExist)
                {
                    return (YearSyncOutcome.NotPublished, null, Array.Empty<string>());
                }

                // 今年或更早、或曾同步成功過的年份，來源不可能說「尚未公告」：當故障，退備援
                primaryFailed = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                primaryFailed = true;
                attempts++;
                if (IsNetworkError(ex))
                {
                    networkFailures++;
                }

                _log.Write($"{year}: {source.Name} 取得失敗：{ex.GetType().Name}: {ex.Message}");
            }
        }

        if (days is null || chosen is null)
        {
            if (attempts > 0 && attempts == networkFailures)
            {
                unreachableYears.Add(year); // 主來源與備援都是網路層錯誤：完全連不上
            }

            return (YearSyncOutcome.Failed, null, Array.Empty<string>());
        }

        var target = OfficialCalendar.ToExceptions(days);
        if (target.Count == 0)
        {
            // 台灣每年 1/1 一定放假，真正的年度檔不可能一個例外日都沒有。這是只有週末的空殼（例如備援鏡像在
            // 官方公告前先放的骨架）：照單全收會把該年全部非覆寫的內建假日刪光，所以整份拒絕。
            _log.Write($"{year}: {chosen.Name} 的資料沒有任何國定假日或補班日，視為空殼、整份不寫");
            return (YearSyncOutcome.Failed, null, Array.Empty<string>());
        }

        var offDays = days.Where(d => d.IsOffDay).Select(d => d.Date).ToHashSet();
        var allowRemove = !chosen.IsFallback;

        // 每年獨立 scope：這一年的任何例外（含 commit 失敗）都只會丟掉這個 scope，不留殘渣
        using var scope = _scopes.Create();
        var rows = (await scope.Calendar.GetExceptionsAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken))
            .ToDictionary(e => e.Day.Date);

        var changedDates = new List<DateOnly>();
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
                var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (!allowRemove || (weekend && offDays.Contains(d) && row!.Day.IsPublicHoliday))
                {
                    continue; // 備援不刪；週末放假而來源沒標備註時，不刪既有的週末國定假日
                }

                if (await scope.Calendar.RemoveIfNotOverriddenAsync(d, cancellationToken))
                {
                    changedDates.Add(d);
                }
            }
            else if (row is null || !SameFacts(row.Day, want))
            {
                if (await scope.Calendar.UpsertIfNotOverriddenAsync(new CalendarException(want, Overridden: false), cancellationToken))
                {
                    changedDates.Add(d);
                }
            }
        }

        if (changedDates.Count == 0)
        {
            return (YearSyncOutcome.Unchanged, chosen.Name, Array.Empty<string>());
        }

        var published = (await scope.Schedules.ListAsync(cancellationToken))
            .Where(h => h.Status == ScheduleStatus.Published)
            .Select(h => h.YearMonth)
            .ToHashSet();
        var affected = changedDates
            .Select(d => new YearMonth(d.Year, d.Month))
            .Where(published.Contains)
            .Distinct()
            .OrderBy(m => m)
            .Select(m => m.ToString())
            .ToArray();

        await scope.UnitOfWork.CommitAsync(cancellationToken);
        _log.Write($"{year}: 已更新 {changedDates.Count} 天（{chosen.Name}）");
        if (affected.Length > 0)
        {
            _log.Write($"{year}: 變動落在已發布月份 {string.Join("、", affected)}，額度點數可能改變");
        }

        return (YearSyncOutcome.Updated, chosen.Name, affected);
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
    IReadOnlyList<string> AffectedPublishedMonths,
    CalendarSyncFailureKind? FailureKind,
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
        var (running, finishedAt, updated, affected, failureKind) = _progress.Snapshot();
        return new CalendarSyncStatusView(_progress.Enabled, running, finishedAt, updated, affected, failureKind, state.LastSuccessAt, state.LastError, state.Years);
    }
}
