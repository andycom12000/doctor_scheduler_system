using Scheduler.Domain.Model;

namespace Scheduler.Domain.Scheduling;

/// <summary>
/// 月結轉的結算：發布時，同一身分組內「剩餘額度最多的人」為 0，其他人是與他的差額。
/// 用的尺是 <see cref="MetricEvaluator.QuotaRemaining"/>（上限 − 已排 − 本月的起始偏移），
/// 與 Fairness(quota_point) 同一把（契約 <c>CarryOverEntry.points</c> 的說明）。
/// 因為本月的起始偏移已經扣在剩餘額度裡，上月多值的人只要本月少值回來，差額就歸零——不會累積跨越兩個月。
/// 算不出剩餘額度的人（NP，上限為 null）與停用者不進結算。
/// </summary>
public static class CarryOverSettlement
{
    public static IReadOnlyList<CarryOverEntry> Settle(SchedulingContext ctx)
    {
        var metrics = new MetricEvaluator(ctx);
        var remaining = ctx.Staff
            .Where(s => s.Status == StaffStatus.Active)
            .Select(s => (Staff: s, Group: ctx.RankOf(s.RankCode).GroupCode, Remaining: metrics.QuotaRemaining(s.Id)))
            .Where(x => x.Remaining is not null)
            .ToArray();

        var entries = new List<CarryOverEntry>();
        foreach (var group in remaining.GroupBy(x => x.Group))
        {
            var max = group.Max(x => x.Remaining!.Value);
            foreach (var x in group.OrderBy(x => x.Staff.EmployeeNo, StringComparer.Ordinal))
            {
                entries.Add(new CarryOverEntry(x.Staff.Id, max - x.Remaining!.Value));
            }
        }

        return entries;
    }
}
