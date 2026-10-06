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

    /// <summary>每一輪同步開始前呼叫一次，讓來源丟掉上一輪的快取（例如 metadata 同一輪只抓一次）。</summary>
    void BeginRun()
    {
    }
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

public sealed record CalendarSyncProgressSnapshot(
    bool Running,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<int> UpdatedYears,
    IReadOnlyList<string> AffectedPublishedMonths,
    CalendarSyncFailureKind? FailureKind);

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

    public CalendarSyncProgressSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new CalendarSyncProgressSnapshot(_running, _finishedAt, _updatedYears, _affectedPublishedMonths, _failureKind);
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
/// 這一輪同步失敗的性質（案主決定）：只有「已拿到通過驗證的資料、但寫入失敗」是 <see cref="WriteFailed"/>（只能重試）；
/// 其他取不到資料的情況一律 <see cref="Unavailable"/>，前端可讓使用者「先用現有資料」。
/// </summary>
public enum CalendarSyncFailureKind
{
    /// <summary>取不到資料（網路層錯誤、HTTP 錯誤含 403／407、封鎖頁或格式不符、空殼、下載中斷…）：行事曆沒有被改動，可略過。</summary>
    Unavailable,

    /// <summary>已拿到通過驗證的官方資料、但寫入資料庫失敗：只能重試。</summary>
    WriteFailed,
}

public sealed record CalendarSyncRunResult(
    IReadOnlyDictionary<int, YearSyncOutcome> Years,
    IReadOnlyList<string> AffectedPublishedMonths,
    CalendarSyncFailureKind? FailureKind = null)
{
    public IReadOnlyList<int> UpdatedYears => Years.Where(p => p.Value == YearSyncOutcome.Updated).Select(p => p.Key).OrderBy(y => y).ToArray();
}

/// <summary>
/// 自動更新的用例：只同步今年與明年（過去的年份不再重抓），逐年抓、驗證、和現有資料逐日比對、只寫有差異的。
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
        foreach (var source in _sources)
        {
            source.BeginRun(); // 例如主來源的 metadata 同一輪只抓一次
        }

        // 只同步今年與明年：過去的年份不會再變，每次啟動都重抓只會拉長（離線時的）等待
        var thisYear = _time.GetLocalNow().Year;
        var years = new[] { thisYear, thisYear + 1 };
        CalendarSyncState previous;
        using (var scope = _scopes.Create())
        {
            previous = await scope.State.GetAsync(cancellationToken);
        }

        var knownYears = previous.Years.Select(y => y.Year).ToHashSet();
        var outcomes = new Dictionary<int, YearSyncOutcome>();
        var synced = new Dictionary<int, SyncedYear>();
        var unreachableYears = new HashSet<int>();
        var writeFailedYears = new HashSet<int>();
        var affected = new SortedSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        var networkDown = false;
        foreach (var year in years)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (networkDown)
            {
                // 前一年兩個來源都是網路層錯誤：網路是通的才值得再試，不要再讓使用者等一輪逾時
                outcomes[year] = YearSyncOutcome.Failed;
                errors.Add($"{year} 年未能取得資料");
                _log.Write($"{year}: 網路不通，略過");
                continue;
            }

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
                    errors.Add($"{year} 年未能取得資料");
                    networkDown = unreachableYears.Contains(year);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 走到這裡的一定是「已拿到通過驗證的資料、但寫入資料庫失敗」（取不到資料的情況都在 SyncYearAsync 裡處理掉了）。
                // 那一年的 scope 已整個丟掉（沒有任何殘留），其他年照跑
                outcomes[year] = YearSyncOutcome.Failed;
                writeFailedYears.Add(year);
                errors.Add($"{year} 年寫入失敗");
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

        // 只有「拿到通過驗證的資料、但寫入失敗」是 WriteFailed（只能重試）；其餘取不到資料的情況一律 Unavailable（可略過）
        CalendarSyncFailureKind? failureKind = writeFailedYears.Count > 0
            ? CalendarSyncFailureKind.WriteFailed
            : outcomes.Values.Any(o => o == YearSyncOutcome.Failed) ? CalendarSyncFailureKind.Unavailable : null;

        return new CalendarSyncRunResult(outcomes, affected.ToArray(), failureKind);
    }

    /// <summary>
    /// 網路層的錯誤（DNS、連線、TLS、proxy、逾時）：連都連不上。兩個來源在同一年都是這種錯誤時，
    /// 剩下的年份直接略過。伺服器有回應但狀態碼不對（含 403、407）、下載到了但驗證失敗，不算。
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
        // 變動日所在月份，以及前一個月：公平性點數看隔日、連值週六有視窗，月底最後一天會看到下個月的第一天
        var affected = changedDates
            .SelectMany(d => new[] { new YearMonth(d.Year, d.Month), new YearMonth(d.AddMonths(-1).Year, d.AddMonths(-1).Month) })
            .Where(published.Contains)
            .Distinct()
            .OrderBy(m => m)
            .Select(m => m.ToString())
            .ToArray();

        await scope.UnitOfWork.CommitAsync(cancellationToken);
        _log.Write($"{year}: 已更新 {changedDates.Count} 天（{chosen.Name}）");
        if (affected.Length > 0)
        {
            _log.Write($"{year}: 變動影響已發布月份 {string.Join("、", affected)}，額度點數或公平性點數可能改變");
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

    /// <param name="snapshot">
    /// 呼叫端已經取好的進度快照（重試端點：緊接在開始同步之後取，回應才保證是 running，
    /// 不會因為背景跑得太快、或讀 app_meta 的空檔被追過去而回「已結束」）。沒給就現在取，且先於讀資料庫。
    /// </param>
    public async Task<CalendarSyncStatusView> GetStatusAsync(CalendarSyncProgressSnapshot? snapshot = null, CancellationToken cancellationToken = default)
    {
        var (running, finishedAt, updated, affected, failureKind) = snapshot ?? _progress.Snapshot();
        var state = await _state.GetAsync(cancellationToken);
        return new CalendarSyncStatusView(_progress.Enabled, running, finishedAt, updated, affected, failureKind, state.LastSuccessAt, state.LastError, state.Years);
    }
}
