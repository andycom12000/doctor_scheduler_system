using System.Threading.Channels;
using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Validation;

namespace Scheduler.Application.Solving;

/// <summary>契約 <c>SolverProgress</c>：推送的 payload。多帶 <see cref="JobId"/>，兩個 transport 共用一條通道時靠它過濾。</summary>
public sealed record SolverProgressSnapshot(
    string JobId,
    SolverJobStatus Status,
    int VariantIndex,
    int VariantCount,
    double ElapsedSec,
    double TimeLimitSec,
    int SolutionCount,
    double? BestObjective,
    double? BestBound,
    double? Gap);

/// <summary>契約 <c>SolverJob</c>：紀錄加上目前的進度快照。</summary>
public sealed record SolverJobView(SolverJobRecord Record, SolverProgressSnapshot Progress);

/// <summary>
/// 求解工作的迴圈與狀態機（ARCHITECTURE §4.8）。單一 slot：同時只有一個工作在跑，第二個回 <c>SOLVER_BUSY</c>。
/// 三份變體序列求解，每份用一個具名立場的乘數算實際權重，把前幾份的解丟給 Solver 當多樣性約束；
/// 每完成一份就落盤一份，中止時已完成的變體留著。狀態轉換才寫資料庫，逐秒進度只留記憶體；
/// 活著的工作從記憶體讀，結束的從資料庫讀。
///
/// 這是 singleton，而 repository 是 scoped：每次落盤都自己開一個 scope、一次 commit，不把 request 的 scope 帶進背景工作。
/// 變體的指標全部由 Domain 重算（<see cref="ViolationChecker"/>、<see cref="ScheduleScores"/>），Solver 的目標值只供搜尋與進度顯示。
/// </summary>
public sealed class SolverJobService
{
    public const int MaxTimeLimitSec = 600;
    public const int DefaultTimeLimitSec = 15;
    public const string DiversityDroppedWarningFormat = "第 {0} 份變體加上「與前幾份至少差 {1} 格」後無解，這一份改為不要求與前幾份不同";

    private readonly ISolverScopeFactory _scopes;
    private readonly ISolver _solver;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, LiveJob> _live = new(StringComparer.Ordinal);

    /// <summary>不分工作的訂閱者（Shell 開機訂一次，前端靠 jobId 過濾）。同樣在 <see cref="_gate"/> 下增減。</summary>
    private readonly List<Channel<SolverProgressSnapshot>> _broadcast = new();
    private bool _slotTaken;

    /// <summary>
    /// 佔著 slot 的工作編號，佔 slot 的當下就決定、與 <see cref="_slotTaken"/> 在同一個鎖裡設定。
    /// 工作登記進 <see cref="_live"/> 之前（撈資料、寫第一筆紀錄的那段）撞到 slot 的人也拿得到 <c>details.jobId</c>。
    /// </summary>
    private string? _slotJobId;

    public SolverJobService(ISolverScopeFactory scopes, ISolver solver, TimeProvider clock)
    {
        _scopes = scopes;
        _solver = solver;
        _clock = clock;
    }

    /// <summary>排入一個工作並立刻開始跑。回傳時狀態是 queued 或 running，看背景工作搶不搶得到先。</summary>
    public async Task<SolverJobView> CreateAsync(YearMonth month, int? variantCount, int? timeLimitSecPerVariant, CancellationToken cancellationToken = default)
    {
        var count = variantCount ?? VariantProfiles.MaxVariantCount;
        if (count is < 1 or > VariantProfiles.MaxVariantCount)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"variantCount 必須在 1–{VariantProfiles.MaxVariantCount}");
        }

        var timeLimit = timeLimitSecPerVariant ?? DefaultTimeLimitSec;
        if (timeLimit is < 1 or > MaxTimeLimitSec)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"timeLimitSecPerVariant 必須在 1–{MaxTimeLimitSec}");
        }

        var jobId = NewJobId();

        // 先佔 slot 再撈資料：撈資料是 async，不能抱著鎖等；佔到之後出任何錯都要放掉
        lock (_gate)
        {
            if (_slotTaken)
            {
                throw Busy(_slotJobId);
            }

            _slotTaken = true;
            _slotJobId = jobId;
        }

        LiveJob live;
        try
        {
            LoadedContext loaded;
            using (var scope = _scopes.Create())
            {
                loaded = await scope.Loader.LoadAsync(month, cancellationToken);
            }

            var ctx = loaded.Context;
            var activeStaff = ctx.Staff.Where(s => s.Status == StaffStatus.Active).ToArray();
            var scale = new SolverScale(
                activeStaff.Length,
                ctx.Areas.Count,
                month.DayCount,
                activeStaff.Sum(s => ctx.Areas.Count(a => ctx.Eligibility.IsEligible(s.RankCode, a.AreaTypeCode))) * month.DayCount);
            var constraintCount = new ConstraintCount(
                loaded.Constraints.Hard.Count(c => c.IsActive),
                loaded.Constraints.Soft.Count(c => c.IsActive));

            var record = new SolverJobRecord(
                jobId, month, SolverJobStatus.Queued, count, timeLimit, _clock.GetUtcNow(),
                StartedAt: null, FinishedAt: null, ElapsedSec: null, FailureReason: null,
                loaded.Warnings, scale, constraintCount);

            // 撈完 context 之後就不再看 request 的取消權杖：commit 到一半被取消會留下永遠 queued 的殭屍紀錄
            await WithRepositoryAsync(repo => repo.AddAsync(record, CancellationToken.None), CancellationToken.None);

            live = new LiveJob(record, loaded, _clock.GetUtcNow());
            // 登記進 _live 與指派 RunTask 在同一個鎖裡：CancelAsync 才不會撞到「已登記但 RunTask 還是 null」的空窗；
            // RunAsync 本身要等 Started 放行，finally 的 _live.Remove 才不可能跑在登記之前
            lock (_gate)
            {
                _live[record.JobId] = live;
                live.RunTask = Task.Run(() => RunAsync(live), CancellationToken.None);
            }
        }
        catch
        {
            lock (_gate)
            {
                _slotTaken = false;
                _slotJobId = null;
            }

            throw;
        }

        live.Started.SetResult();
        return live.View(_clock.GetUtcNow());
    }

    public async Task<SolverJobView> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(jobId, out var live))
            {
                return live.View(_clock.GetUtcNow());
            }
        }

        var record = await FindRecordAsync(jobId, cancellationToken) ?? throw NotFound(jobId);
        return new SolverJobView(record, TerminalSnapshot(record));
    }

    /// <summary>中止。已結束的工作是冪等的，回目前狀態。回傳時狀態已經是 cancelled（等背景工作收尾，最多等幾秒）。</summary>
    public async Task<SolverJobView> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        LiveJob? live;
        lock (_gate)
        {
            _live.TryGetValue(jobId, out live);
        }

        if (live is not null)
        {
            live.Cancellation.Cancel();
            if (live.RunTask is not null)
            {
                await Task.WhenAny(live.RunTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            }
        }

        return await GetAsync(jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<VariantRecord>> ListVariantsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        bool isLive;
        lock (_gate)
        {
            isLive = _live.ContainsKey(jobId);
        }

        if (!isLive && await FindRecordAsync(jobId, cancellationToken) is null)
        {
            throw NotFound(jobId);
        }

        using var scope = _scopes.Create();
        return await scope.Jobs.GetVariantsAsync(jobId, cancellationToken);
    }

    /// <summary>
    /// 訂閱進度。活著的工作先重播目前快照再逐筆推送，結束時序列結束；已結束的工作只回一筆終態快照。
    /// Api 的 SSE 端點與 Shell 的 PostWebMessageAsJson 都從這裡拿，收到的內容一模一樣。
    /// </summary>
    public async IAsyncEnumerable<SolverProgressSnapshot> SubscribeAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<SolverProgressSnapshot>? channel = null;
        LiveJob? live;
        lock (_gate)
        {
            if (_live.TryGetValue(jobId, out live))
            {
                channel = Channel.CreateUnbounded<SolverProgressSnapshot>(new UnboundedChannelOptions { SingleReader = true });
                live.Subscribers.Add(channel);
                channel.Writer.TryWrite(live.Snapshot);
            }
        }

        if (channel is null)
        {
            var record = await FindRecordAsync(jobId, cancellationToken) ?? throw NotFound(jobId);
            yield return TerminalSnapshot(record);
            yield break;
        }

        try
        {
            await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }
        finally
        {
            lock (_gate)
            {
                live!.Subscribers.Remove(channel);
            }
        }
    }

    /// <summary>
    /// 訂閱所有工作的進度，不指定 jobId、不會自己結束（只由 <paramref name="cancellationToken"/> 收掉）。
    /// 訂閱當下已在跑的工作各重播一筆目前快照，之後逐筆推送。
    /// 給只有一條通道的 Shell 用：開機訂一次，每筆事件都帶 jobId，前端自己過濾（§4.7）。
    /// 每個活著的工作發出的每一筆快照（含終態）都會經過這裡，與 <see cref="SubscribeAsync"/> 收到的內容一致。
    /// </summary>
    public async IAsyncEnumerable<SolverProgressSnapshot> SubscribeAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<SolverProgressSnapshot>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            _broadcast.Add(channel);
            // 訂閱時已在跑的工作先給一筆目前快照，訂閱者不必等下一秒的 tick
            foreach (var live in _live.Values)
            {
                channel.Writer.TryWrite(live.Snapshot);
            }
        }

        try
        {
            await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return snapshot;
            }
        }
        finally
        {
            lock (_gate)
            {
                _broadcast.Remove(channel);
            }
        }
    }

    // ---- 背景迴圈 ----

    private async Task RunAsync(LiveJob live)
    {
        await live.Started.Task;
        SolverJobRecord terminal;
        var token = live.Cancellation.Token;
        var ticker = TickAsync(live);
        try
        {
            live.Update(r => r with { Status = SolverJobStatus.Running, StartedAt = _clock.GetUtcNow() });
            await PersistAsync(live);
            live.Publish(this);

            var avoid = new List<IReadOnlyList<Duty>>();
            for (var i = 0; i < live.Record.VariantCount; i++)
            {
                token.ThrowIfCancellationRequested();
                var profile = VariantProfiles.All[i];
                live.BeginVariant(i + 1, _clock.GetUtcNow());
                live.Publish(this);

                var result = await SolveOneAsync(live, profile, avoid, token);
                token.ThrowIfCancellationRequested();
                if (result.Status is SolveStatus.Cancelled)
                {
                    throw new OperationCanceledException(token);
                }

                if (result.Status is SolveStatus.Infeasible or SolveStatus.Unknown)
                {
                    throw new SchedulerException(ErrorCode.SolverFailed, result.Status == SolveStatus.Infeasible
                        ? $"第 {i + 1} 份變體無解"
                        : $"第 {i + 1} 份變體在 {live.Record.TimeLimitSecPerVariant} 秒內找不到任何可行解");
                }

                var variant = BuildVariant(live, profile, result.Duties);
                await WithRepositoryAsync(repo => repo.AddVariantAsync(variant, CancellationToken.None), CancellationToken.None);
                // 變體存進去了才算「完成」，Last* 才不會指到一份沒落盤的變體
                live.CompleteVariant(i + 1, result, _clock.GetUtcNow());
                live.Publish(this);
                avoid.Add(result.Duties);
            }

            terminal = Finish(live, SolverJobStatus.Succeeded, null);
        }
        catch (OperationCanceledException)
        {
            terminal = Finish(live, SolverJobStatus.Cancelled, null);
        }
        catch (Exception ex)
        {
            terminal = Finish(live, SolverJobStatus.Failed, ex.Message);
        }

        // 順序是不變式：終態先寫進資料庫，之後「記憶體裡的終態、移出 _live、放 slot」在同一個鎖裡一次完成。
        // 任何人看得到終態（GetAsync 讀記憶體或資料庫、訂閱）時 slot 都已經空出來，不會「看到結束卻 SOLVER_BUSY」；
        // 反過來，_live 移除之後讀資料庫一定已是終態。PersistTerminalAsync 吞掉所有例外，所以放 slot 不會被跳過
        await PersistTerminalAsync(terminal);
        lock (_gate)
        {
            live.Update(_ => terminal);
            _live.Remove(live.Record.JobId);
            _slotTaken = false;
            _slotJobId = null;
        }

        try
        {
            live.Cancellation.Cancel();
            await ticker;
            live.Publish(this);
        }
        finally
        {
            lock (_gate)
            {
                foreach (var subscriber in live.Subscribers)
                {
                    subscriber.Writer.TryComplete();
                }
            }

            live.Cancellation.Dispose();
        }
    }

    /// <summary>終態一定要落盤，否則 _live 移除後資料庫裡停在 running，前端會無限輪詢到程式重啟。失敗就再試兩次。</summary>
    private async Task PersistTerminalAsync(SolverJobRecord terminal)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await PersistAsync(terminal);
                return;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), _clock);
            }
            catch
            {
                // 三次都寫不進去：沒有更好的地方報告，記憶體裡的終態仍會推給訂閱者
                return;
            }
        }
    }

    private async Task<SolveResult> SolveOneAsync(LiveJob live, VariantProfile profile, List<IReadOnlyList<Duty>> avoid, CancellationToken token)
    {
        var weights = VariantProfiles.EffectiveWeights(live.Loaded.Constraints, profile);
        // 快照一份：request 是不可變的紀錄，不能讓之後 avoid.Add 改到已送出的請求
        var request = new SolveRequest(
            live.Loaded.Context, live.Loaded.Constraints, weights, avoid.ToArray(),
            VariantProfiles.MinDifferentCells, TimeSpan.FromSeconds(live.Record.TimeLimitSecPerVariant));

        var result = await _solver.SolveAsync(request, p => live.OnProgress(this, p, _clock.GetUtcNow()), token);
        if (result.Status == SolveStatus.Infeasible && avoid.Count > 0)
        {
            // 多樣性是「為了看得出差異」加的，不值得讓整個工作失敗：拿掉再解一次，提醒排班者
            var index = live.Snapshot.VariantIndex;
            live.Update(r => r with
            {
                Warnings = r.Warnings.Append(string.Format(DiversityDroppedWarningFormat, index, VariantProfiles.MinDifferentCells)).ToArray(),
            });
            live.BeginVariant(index, _clock.GetUtcNow());
            result = await _solver.SolveAsync(request with { AvoidSolutions = Array.Empty<IReadOnlyList<Duty>>() }, p => live.OnProgress(this, p, _clock.GetUtcNow()), token);
        }

        return result;
    }

    /// <summary>逐秒推一次快照，只為了 elapsedSec 會動；Solver 找到新解時另外會推。</summary>
    private async Task TickAsync(LiveJob live)
    {
        try
        {
            while (!live.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _clock, live.Cancellation.Token);
                live.Touch(_clock.GetUtcNow());
                live.Publish(this);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>算出終態紀錄但不套用到活著的工作：套用要等落盤之後、和放 slot 同一刻（見 <see cref="RunAsync"/>）。</summary>
    private SolverJobRecord Finish(LiveJob live, SolverJobStatus status, string? failureReason)
    {
        var now = _clock.GetUtcNow();
        var r = live.Record;
        return r with
        {
            Status = status,
            FinishedAt = now,
            ElapsedSec = Math.Round((now - (r.StartedAt ?? r.CreatedAt)).TotalSeconds, 1),
            FailureReason = failureReason,
        };
    }

    /// <summary>變體的指標全部由 Domain 重算，與驗證頁面用同一份定義，建模漂移時會直接看出來（§4.8）。</summary>
    private static VariantRecord BuildVariant(LiveJob live, VariantProfile profile, IReadOnlyList<Duty> duties)
    {
        var c = live.Loaded.Context;
        var ctx = new SchedulingContext(
            c.Month, c.Calendar, c.Areas, c.Staff, c.Ranks, c.Eligibility, c.PointRules, c.Override,
            duties, c.PreviousMonthDuties, c.BlockedDays, c.CarryOver);
        var constraints = live.Loaded.Constraints;
        var validation = new ViolationChecker(ctx).Check(constraints);
        var scores = new ScheduleScores(ctx);

        var byCell = duties.ToLookup(d => (d.AreaId, d.Date));
        var vacancies = ctx.Areas.Sum(a => ctx.Month.Days().Sum(d => Math.Max(0, a.RequiredPerDay - byCell[(a.Id, d)].Count())));

        var preferCodes = constraints.Soft
            .Where(x => x.Primitive == Primitive.Preference && x.Params.Direction == PreferenceDirection.Prefer)
            .Select(x => x.Code)
            .ToHashSet(StringComparer.Ordinal);
        var rankPreference = validation.Violations.Count(v => preferCodes.Contains(v.Code));

        // 指標用「那一條約束自己的範圍」算，與目標函數看同一群人（S2 豁免 NP，指標就不該把 NP 算進去）；
        // 設定裡沒有那一條時退回不限範圍
        var quotaFairnessScope = ScopeOf(constraints, Primitive.Fairness, Metric.QuotaPoint);
        var consistencyScope = ScopeOf(constraints, Primitive.Consistency, null);
        var fairnessPoint = constraints.Soft.FirstOrDefault(x => x.IsActive && x.Primitive == Primitive.Fairness && x.Metric == Metric.FairnessPoint);
        var metrics = new VariantMetrics(
            vacancies,
            scores.FairnessByGroup(quotaFairnessScope, Metric.QuotaPoint).Values.Sum(),
            ctx.Staff.Where(s => s.Status == StaffStatus.Active && consistencyScope.AppliesToRank(s.RankCode)).Sum(s => scores.ConsistencyOf(s.Id)),
            rankPreference,
            fairnessPoint is null ? null : scores.FairnessByGroup(fairnessPoint.Scope, Metric.FairnessPoint).Values.Sum());

        // 軟分數用使用者的原始權重（不乘立場乘數），三份變體才能放在同一把尺上比
        var softScore = VariantScoring.SoftScore(ctx, constraints, validation);

        return new VariantRecord(
            live.Record.JobId, profile.Id, profile.Label, profile.Multipliers,
            metrics, validation.HardCount, softScore, duties);
    }

    private static ConstraintScope ScopeOf(ConstraintSettings constraints, Primitive primitive, Metric? metric) =>
        constraints.Soft.FirstOrDefault(x => x.Primitive == primitive && (metric is null || x.Metric == metric))?.Scope ?? ConstraintScope.All;

    // ---- 落盤 ----

    private Task PersistAsync(LiveJob live) => PersistAsync(live.Record);

    private Task PersistAsync(SolverJobRecord record) =>
        WithRepositoryAsync(repo => repo.UpdateAsync(record, CancellationToken.None), CancellationToken.None);

    private async Task WithRepositoryAsync(Func<ISolverJobRepository, Task> action, CancellationToken cancellationToken)
    {
        using var scope = _scopes.Create();
        await action(scope.Jobs);
        await scope.UnitOfWork.CommitAsync(cancellationToken);
    }

    private async Task<SolverJobRecord?> FindRecordAsync(string jobId, CancellationToken cancellationToken)
    {
        using var scope = _scopes.Create();
        return await scope.Jobs.FindAsync(jobId, cancellationToken);
    }

    // ---- helpers ----

    /// <summary>
    /// 已結束、只剩資料庫紀錄的工作。variantIndex 與搜尋統計都是「最後一份完成的變體」的：成功時就是最後一份；
    /// 中止或失敗時是之前完成的那份（一份都沒完成則是 0／null），與工作還活著時最後推出去的那筆（未完成那份的即時值）不同。
    /// elapsedSec 是整個工作的耗時（紀錄裡只有這個），不是活著時的本份變體耗時。
    /// </summary>
    private static SolverProgressSnapshot TerminalSnapshot(SolverJobRecord r) =>
        new(
            r.JobId, r.Status, r.LastVariantIndex ?? 0, r.VariantCount, r.ElapsedSec ?? 0, r.TimeLimitSecPerVariant,
            r.LastSolutionCount ?? 0, r.LastBestObjective, r.LastBestBound, GapOf(r.LastBestObjective, r.LastBestBound));

    /// <summary>收斂間隙 |obj − bound| / |obj|；還沒有可行解（任一為 null）時是 null，目標為 0 就是已證明最佳。</summary>
    private static double? GapOf(double? objective, double? bound) =>
        objective is double obj && bound is double bnd
            ? (obj == 0 ? 0 : Math.Round(Math.Abs(obj - bnd) / Math.Abs(obj), 4))
            : null;

    private static string NewJobId() => "job-" + Guid.NewGuid().ToString("N")[..12];

    private static SchedulerException NotFound(string jobId) => SchedulerException.NotFound($"找不到求解工作 {jobId}");

    private static SchedulerException Busy(string? jobId) =>
        new(ErrorCode.SolverBusy, "已有求解工作在執行中", jobId is null ? null : new Dictionary<string, object?> { ["jobId"] = jobId });

    /// <summary>
    /// 活著的工作：紀錄、輸入、進度快照、訂閱者。紀錄與快照的變更在自己的 <c>_sync</c> 下；
    /// <see cref="SolverJobService._gate"/> 只保護 _live、_broadcast 與 Subscribers。
    /// </summary>
    private sealed class LiveJob
    {
        private readonly object _sync = new();
        private DateTimeOffset _variantStartedAt;

        public LiveJob(SolverJobRecord record, LoadedContext loaded, DateTimeOffset now)
        {
            Record = record;
            Loaded = loaded;
            _variantStartedAt = now;
            Snapshot = new SolverProgressSnapshot(record.JobId, record.Status, 0, record.VariantCount, 0, record.TimeLimitSecPerVariant, 0, null, null, null);
        }

        public SolverJobRecord Record { get; private set; }

        public LoadedContext Loaded { get; }

        public SolverProgressSnapshot Snapshot { get; private set; }

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>RunAsync 等這個放行，登記進 _live 之前背景工作不會動。</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task? RunTask { get; set; }

        public List<Channel<SolverProgressSnapshot>> Subscribers { get; } = new();

        /// <summary>執行中的總耗時現算，結束後用結算時寫進紀錄的那個數。</summary>
        public SolverJobView View(DateTimeOffset now)
        {
            lock (_sync)
            {
                var record = Record.ElapsedSec is null && Record.Status == SolverJobStatus.Running
                    ? Record with { ElapsedSec = Math.Round((now - (Record.StartedAt ?? Record.CreatedAt)).TotalSeconds, 1) }
                    : Record;
                return new SolverJobView(record, Snapshot);
            }
        }

        public void Update(Func<SolverJobRecord, SolverJobRecord> change)
        {
            lock (_sync)
            {
                Record = change(Record);
                Snapshot = Snapshot with { Status = Record.Status };
            }
        }

        public void BeginVariant(int index, DateTimeOffset now)
        {
            lock (_sync)
            {
                _variantStartedAt = now;
                Snapshot = Snapshot with { VariantIndex = index, ElapsedSec = 0, SolutionCount = 0, BestObjective = null, BestBound = null, Gap = null };
            }
        }

        /// <summary>
        /// 一份變體解完：用 <see cref="SolveResult"/> 的最終統計蓋掉快照，並記進紀錄。
        /// 進度回呼只在找到新解時觸發，最後的 bound 收緊（含證明最佳）不會再推一次；多樣性退讓重解時
        /// <see cref="BeginVariant"/> 也已把快照歸零，所以以求解結果為準。
        /// </summary>
        public void CompleteVariant(int index, SolveResult result, DateTimeOffset now)
        {
            lock (_sync)
            {
                Record = Record with
                {
                    LastVariantIndex = index,
                    LastSolutionCount = result.SolutionCount,
                    LastBestObjective = result.Objective,
                    LastBestBound = result.Bound,
                };
                Snapshot = Snapshot with
                {
                    ElapsedSec = Math.Round((now - _variantStartedAt).TotalSeconds, 1),
                    SolutionCount = result.SolutionCount,
                    BestObjective = result.Objective,
                    BestBound = result.Bound,
                    Gap = GapOf(result.Objective, result.Bound),
                };
            }
        }

        public void Touch(DateTimeOffset now)
        {
            lock (_sync)
            {
                if (Record.Status == SolverJobStatus.Running)
                {
                    Snapshot = Snapshot with { ElapsedSec = Math.Round((now - _variantStartedAt).TotalSeconds, 1) };
                }
            }
        }

        /// <summary>耗時用服務自己的時鐘，不用求解器回報的：模型建置與 native 載入也算在這一份的時間裡，數字才不會倒退。</summary>
        public void OnProgress(SolverJobService owner, SolveProgress p, DateTimeOffset now)
        {
            lock (_sync)
            {
                Snapshot = Snapshot with
                {
                    ElapsedSec = Math.Round((now - _variantStartedAt).TotalSeconds, 1),
                    SolutionCount = p.SolutionCount,
                    BestObjective = p.BestObjective,
                    BestBound = p.BestBound,
                    Gap = GapOf(p.BestObjective, p.BestBound),
                };
            }

            Publish(owner);
        }

        public void Publish(SolverJobService owner)
        {
            lock (owner._gate)
            {
                SolverProgressSnapshot snapshot;
                lock (_sync)
                {
                    snapshot = Snapshot;
                }

                foreach (var subscriber in Subscribers)
                {
                    subscriber.Writer.TryWrite(snapshot);
                }

                foreach (var subscriber in owner._broadcast)
                {
                    subscriber.Writer.TryWrite(snapshot);
                }
            }
        }
    }
}
