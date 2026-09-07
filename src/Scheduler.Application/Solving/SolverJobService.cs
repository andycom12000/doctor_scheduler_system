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
    private bool _slotTaken;

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

        // 先佔 slot 再撈資料：撈資料是 async，不能抱著鎖等；佔到之後出任何錯都要放掉
        lock (_gate)
        {
            if (_slotTaken)
            {
                throw Busy(_live.Values.FirstOrDefault()?.Record.JobId);
            }

            _slotTaken = true;
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
                NewJobId(), month, SolverJobStatus.Queued, count, timeLimit, _clock.GetUtcNow(),
                StartedAt: null, FinishedAt: null, ElapsedSec: null, FailureReason: null,
                loaded.Warnings, scale, constraintCount);

            await WithRepositoryAsync(repo => repo.AddAsync(record, cancellationToken), cancellationToken);

            live = new LiveJob(record, loaded, _clock.GetUtcNow());
            lock (_gate)
            {
                _live[record.JobId] = live;
            }
        }
        catch
        {
            lock (_gate)
            {
                _slotTaken = false;
            }

            throw;
        }

        live.RunTask = Task.Run(() => RunAsync(live), CancellationToken.None);
        return live.View();
    }

    public async Task<SolverJobView> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(jobId, out var live))
            {
                return live.View();
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

    // ---- 背景迴圈 ----

    private async Task RunAsync(LiveJob live)
    {
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
                avoid.Add(result.Duties);
            }

            Finish(live, SolverJobStatus.Succeeded, null);
        }
        catch (OperationCanceledException)
        {
            Finish(live, SolverJobStatus.Cancelled, null);
        }
        catch (Exception ex)
        {
            Finish(live, SolverJobStatus.Failed, ex.Message);
        }

        try
        {
            await PersistAsync(live);
        }
        catch
        {
            // 落盤失敗沒有更好的地方報告；記憶體裡的終態仍會推給訂閱者
        }
        finally
        {
            live.Cancellation.Cancel();
            await ticker;
            live.Publish(this);
            lock (_gate)
            {
                foreach (var subscriber in live.Subscribers)
                {
                    subscriber.Writer.TryComplete();
                }

                _live.Remove(live.Record.JobId);
                _slotTaken = false;
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
            live.Update(r => r with
            {
                Warnings = r.Warnings.Append(string.Format(DiversityDroppedWarningFormat, live.Snapshot.VariantIndex, VariantProfiles.MinDifferentCells)).ToArray(),
            });
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

    private void Finish(LiveJob live, SolverJobStatus status, string? failureReason)
    {
        var now = _clock.GetUtcNow();
        live.Update(r => r with
        {
            Status = status,
            FinishedAt = now,
            ElapsedSec = Math.Round((now - (r.StartedAt ?? r.CreatedAt)).TotalSeconds, 1),
            FailureReason = failureReason,
        });
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

        var fairnessPointActive = constraints.Soft.Any(x => x.IsActive && x.Primitive == Primitive.Fairness && x.Metric == Metric.FairnessPoint);
        var metrics = new VariantMetrics(
            vacancies,
            scores.FairnessByGroup(ConstraintScope.All, Metric.QuotaPoint).Values.Sum(),
            ctx.Staff.Where(s => s.Status == StaffStatus.Active).Sum(s => scores.ConsistencyOf(s.Id)),
            rankPreference,
            fairnessPointActive ? scores.FairnessByGroup(ConstraintScope.All, Metric.FairnessPoint).Values.Sum() : null);

        // 軟分數用使用者的原始權重（不乘立場乘數），三份變體才能放在同一把尺上比
        var softScore = 0.0;
        foreach (var x in constraints.Soft.Where(x => x.IsActive))
        {
            var score = x.Primitive switch
            {
                Primitive.Fairness => scores.Fairness(x),
                Primitive.Consistency => scores.Consistency(x),
                _ => validation.Violations.Count(v => v.Code == x.Code),
            };
            softScore += x.Weight * score;
        }

        return new VariantRecord(
            live.Record.JobId, profile.Id, profile.Label, profile.Multipliers,
            metrics, validation.HardCount, softScore, duties);
    }

    // ---- 落盤 ----

    private Task PersistAsync(LiveJob live) =>
        WithRepositoryAsync(repo => repo.UpdateAsync(live.Record, CancellationToken.None), CancellationToken.None);

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

    private static SolverProgressSnapshot TerminalSnapshot(SolverJobRecord r) =>
        new(r.JobId, r.Status, r.VariantCount, r.VariantCount, r.ElapsedSec ?? 0, r.TimeLimitSecPerVariant, 0, null, null, null);

    private static string NewJobId() => "job-" + Guid.NewGuid().ToString("N")[..12];

    private static SchedulerException NotFound(string jobId) => SchedulerException.NotFound($"找不到求解工作 {jobId}");

    private static SchedulerException Busy(string? jobId) =>
        new(ErrorCode.SolverBusy, "已有求解工作在執行中", jobId is null ? null : new Dictionary<string, object?> { ["jobId"] = jobId });

    /// <summary>活著的工作：紀錄、輸入、進度快照、訂閱者。所有變更都在 <see cref="SolverJobService._gate"/> 下。</summary>
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

        public Task? RunTask { get; set; }

        public List<Channel<SolverProgressSnapshot>> Subscribers { get; } = new();

        public SolverJobView View()
        {
            lock (_sync)
            {
                return new SolverJobView(Record, Snapshot);
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
                var gap = p.BestObjective is double obj && p.BestBound is double bound
                    ? (obj == 0 ? 0 : Math.Round(Math.Abs(obj - bound) / Math.Abs(obj), 4))
                    : (double?)null;
                Snapshot = Snapshot with
                {
                    ElapsedSec = Math.Round((now - _variantStartedAt).TotalSeconds, 1),
                    SolutionCount = p.SolutionCount,
                    BestObjective = p.BestObjective,
                    BestBound = p.BestBound,
                    Gap = gap,
                };
            }

            Publish(owner);
        }

        public void Publish(SolverJobService owner)
        {
            lock (owner._gate)
            {
                var snapshot = View().Progress;
                foreach (var subscriber in Subscribers)
                {
                    subscriber.Writer.TryWrite(snapshot);
                }
            }
        }
    }
}
