using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Domain.Scheduling;

/// <summary>
/// 度量的唯一實作：每一格值多少、某人本月累計多少。
/// Budget 與 Fairness 兩個原語、點數看板、變體指標都經過這裡，
/// 兩套點數才不會各算各的。
/// </summary>
public sealed class MetricEvaluator
{
    private readonly SchedulingContext _ctx;

    public MetricEvaluator(SchedulingContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>某一格在某度量下的值。公平性點數對 NP（無點數類型）為 null。</summary>
    public int? ValueOf(Metric metric, Duty duty) => metric switch
    {
        Metric.QuotaPoint => _ctx.PointRules.Quota.ValueOf(_ctx.DayOf(duty)),
        Metric.DutyDay => 1,
        Metric.FairnessPoint => FairnessPointOf(duty),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    /// <summary>某人本月在某度量下的累計。只計入 <paramref name="duties"/>（呼叫端已依範圍篩過）。</summary>
    public int Total(Metric metric, IEnumerable<Duty> duties) =>
        duties.Sum(d => ValueOf(metric, d) ?? 0);

    public int QuotaPoints(string staffId) => Total(Metric.QuotaPoint, _ctx.DutiesOf(staffId));

    /// <summary>剩餘額度 <c>cap − 已排 − 月結轉偏移</c>。NP 為 null。</summary>
    public int? QuotaRemaining(string staffId)
    {
        var cap = _ctx.QuotaCapOf(_ctx.RankOfStaff(staffId));
        return cap is null ? null : cap - QuotaPoints(staffId) - _ctx.CarryOverOf(staffId);
    }

    public int? FairnessPoints(string staffId) =>
        _ctx.RankOfStaff(staffId).PointType is null ? null : Total(Metric.FairnessPoint, _ctx.DutiesOf(staffId));

    /// <summary>
    /// 公平性點數：查「當日 / 隔日」表，若值班當日是週六、往後 windowDays 天（含當日）內沒有國定假日、
    /// 且下週六也值班，再加連值週六 bonus。這是唯一決策相依的點數項。
    /// </summary>
    private int? FairnessPointOf(Duty duty)
    {
        var pointType = _ctx.RankOfStaff(duty.StaffId).PointType;
        if (pointType is null)
        {
            return null;
        }

        var today = _ctx.Calendar[duty.Date];
        var tomorrow = _ctx.Calendar[duty.Date.AddDays(1)];
        var points = _ctx.PointRules.Fairness.Lookup(pointType.Value, today, tomorrow);

        if (today.Weekday == DayOfWeek.Saturday && HasDutyOn(duty.StaffId, duty.Date.AddDays(7)))
        {
            var bonus = _ctx.PointRules.Fairness.ConsecutiveSaturdayBonus;
            var window = Enumerable.Range(0, bonus.WindowDays).Select(i => duty.Date.AddDays(i));
            if (!window.Any(d => _ctx.Calendar.Covers(d) && _ctx.Calendar[d].IsPublicHoliday))
            {
                points += bonus.Points;
            }
        }

        return points;
    }

    private bool HasDutyOn(string staffId, DateOnly date) =>
        _ctx.DutiesOf(staffId).Any(d => d.Date == date);
}
