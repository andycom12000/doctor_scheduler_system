using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Validation;

/// <summary>
/// 指不到格子的兩個原語——Fairness 與 Consistency——的分數。
/// 變體的 <c>metrics.quotaFairness / areaConsistency / fairnessPoint</c> 與點數看板由此計算；
/// Solver 建目標函數時用同一個定義。分數越小越好。
/// </summary>
public sealed class ScheduleScores
{
    private readonly SchedulingContext _ctx;
    private readonly MetricEvaluator _metrics;

    public ScheduleScores(SchedulingContext ctx)
    {
        _ctx = ctx;
        _metrics = new MetricEvaluator(ctx);
    }

    /// <summary>
    /// Fairness：各身分組內某度量的 <c>max − min</c>，加總。
    /// quota_point 比的是剩餘額度（上限 − 已排 − 月結轉偏移），fairness_point 比的是累計值。
    /// 度量算不出來的人（NP 的上限與點數類型皆為 null）不進比較。
    /// </summary>
    public int Fairness(ConstraintDefinition c)
    {
        var metric = c.Metric ?? throw new InvalidOperationException($"{c.Code}：Fairness 原語必須指定 metric");
        return FairnessByGroup(c.Scope, metric).Values.Sum();
    }

    public IReadOnlyDictionary<string, int> FairnessByGroup(ConstraintScope scope, Metric metric)
    {
        var result = new Dictionary<string, int>();
        var groups = _ctx.Staff
            .Where(s => scope.AppliesToRank(s.RankCode))
            .GroupBy(s => _ctx.RankOf(s.RankCode).GroupCode);

        foreach (var group in groups)
        {
            var values = group.Select(s => ValueFor(metric, s.Id)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            result[group.Key] = values.Length == 0 ? 0 : values.Max() - values.Min();
        }

        return result;
    }

    /// <summary>Consistency：每人「值班數 − 最常值的那一區的值班數」，加總。即離開主區的次數。</summary>
    public int Consistency(ConstraintDefinition c) =>
        _ctx.Staff
            .Where(s => c.Scope.AppliesToRank(s.RankCode))
            .Sum(s => ConsistencyOf(s.Id));

    public int ConsistencyOf(string staffId)
    {
        var duties = _ctx.DutiesOf(staffId).ToArray();
        if (duties.Length == 0)
        {
            return 0;
        }

        var mostFrequent = duties.GroupBy(d => d.AreaId).Max(g => g.Count());
        return duties.Length - mostFrequent;
    }

    /// <summary>
    /// 月結轉：<c>組內最大剩餘額度 − 本人剩餘額度</c>，剩餘最多的人為 0。
    /// 發布時結算，供下個月當起始偏移；不累積跨越兩個月。
    /// </summary>
    public IReadOnlyList<CarryOverEntry> SettleCarryOver(ConstraintScope scope)
    {
        var entries = new List<CarryOverEntry>();
        var groups = _ctx.Staff
            .Where(s => scope.AppliesToRank(s.RankCode))
            .GroupBy(s => _ctx.RankOf(s.RankCode).GroupCode);

        foreach (var group in groups)
        {
            var remaining = group
                .Select(s => (s.Id, Remaining: _metrics.QuotaRemaining(s.Id)))
                .Where(x => x.Remaining.HasValue)
                .ToArray();
            if (remaining.Length == 0)
            {
                continue;
            }

            var max = remaining.Max(x => x.Remaining!.Value);
            entries.AddRange(remaining.Select(x => new CarryOverEntry(x.Id, max - x.Remaining!.Value)));
        }

        return entries;
    }

    private int? ValueFor(Metric metric, string staffId) => metric switch
    {
        Metric.QuotaPoint => _metrics.QuotaRemaining(staffId),
        Metric.FairnessPoint => _metrics.FairnessPoints(staffId),
        Metric.DutyDay => _ctx.DutiesOf(staffId).Count(),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };
}
