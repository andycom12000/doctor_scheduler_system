using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Tests;

public class CarryOverSettlementTests
{
    [Fact]
    public void 組內剩餘額度最多者為零_其他人是差額_跨組不比()
    {
        // R2 上限 8、R3 上限 7：s-r2 值 2 個平日剩 6，s-r3 值 1 個平日剩 6 → 同組平手都是 0
        // R4 上限 6 值 3 個平日剩 3、R5 上限 5 沒值剩 5 → s-r4 差 2
        var ctx = new ContextBuilder()
            .WithStaff("s-r2", DefaultRanks.R2)
            .WithStaff("s-r3", DefaultRanks.R3)
            .WithStaff("s-r4", DefaultRanks.R4)
            .WithStaff("s-r5", DefaultRanks.R5)
            .WithStaff("s-np", DefaultRanks.NP)
            .WithDuties("s-r2", "area-icu", 1, 2)
            .WithDuty("s-r3", 3, "area-icu")
            .WithDuties("s-r4", "area-chief", 1, 2, 3)
            .WithDuties("s-np", "area-a", 7, 8, 9, 10)
            .Build();

        var entries = CarryOverSettlement.Settle(ctx).ToDictionary(e => e.StaffId, e => e.Points);

        Assert.Equal(0, entries["s-r2"]);
        Assert.Equal(0, entries["s-r3"]);
        Assert.Equal(2, entries["s-r4"]);
        Assert.Equal(0, entries["s-r5"]);
        Assert.False(entries.ContainsKey("s-np"), "NP 沒有額度上限，不進結算");
    }

    [Fact]
    public void 上月帶入的偏移已扣在剩餘額度裡_本月少值回來就歸零_不累積()
    {
        // 上月 s-r4 多值 2 點帶入 2。本月 s-r4 值 1 個平日（剩 6−1−2=3）、s-r5 值 2 個平日（剩 5−2=3）→ 平手
        var ctx = new ContextBuilder()
            .WithStaff("s-r4", DefaultRanks.R4)
            .WithStaff("s-r5", DefaultRanks.R5)
            .WithCarryOver("s-r4", 2)
            .WithDuty("s-r4", 1, "area-chief")
            .WithDuties("s-r5", "area-chief", 2, 3)
            .Build();

        var entries = CarryOverSettlement.Settle(ctx);

        Assert.All(entries, e => Assert.Equal(0, e.Points));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void 停用者不進結算()
    {
        var ctx = new ContextBuilder()
            .WithStaff("s-r4", DefaultRanks.R4)
            .WithStaff("s-r5", DefaultRanks.R5)
            .WithInactiveStaff("s-gone", DefaultRanks.R6)
            .Build();

        var ids = CarryOverSettlement.Settle(ctx).Select(e => e.StaffId).ToArray();

        Assert.Equal(new[] { "s-r4", "s-r5" }, ids.OrderBy(x => x, StringComparer.Ordinal));
    }
}
