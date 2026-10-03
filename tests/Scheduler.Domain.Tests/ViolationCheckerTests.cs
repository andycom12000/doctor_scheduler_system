using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Validation;

namespace Scheduler.Domain.Tests;

public class ViolationCheckerTests
{
    private static readonly ConstraintSettings Defaults = DefaultConstraints.Settings;

    // ---- H1 覆蓋 ----

    [Fact]
    public void H1_空白值班表_每日每區各一筆硬違規()
    {
        var ctx = new ContextBuilder().Build();

        var result = new ViolationChecker(ctx).Check(Defaults);

        Assert.False(result.Ok);
        Assert.Equal(5 * 30, result.HardCount);
        Assert.All(result.Violations, v =>
        {
            Assert.Equal(DefaultConstraints.H1AreaCoverage, v.Code);
            Assert.StartsWith("area:", v.CellKeys.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void H1_停用後空白值班表沒有違規()
    {
        var settings = Defaults.With(DefaultConstraints.H1AreaCoverage, c => c with { Enabled = false });

        var result = new ViolationChecker(new ContextBuilder().Build()).Check(settings);

        Assert.Empty(result.Violations);
        Assert.True(result.Ok);
    }

    [Fact]
    public void H1_排了人的那格不再是違規()
    {
        var ctx = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2).WithDuty("r2-1", 1, "area-icu").Build();

        var result = new ViolationChecker(ctx).Check(Defaults);

        Assert.DoesNotContain(result.Violations, v => v.CellKeys.Contains("area:area-icu:2026-09-01") && v.Code == DefaultConstraints.H1AreaCoverage);
        Assert.Equal(5 * 30 - 1, result.Violations.Count(v => v.Code == DefaultConstraints.H1AreaCoverage));
    }

    // ---- H2 資格 ----

    [Fact]
    public void H2_PGY1值ICU是硬違規_R2值ICU不是()
    {
        var ctx = new ContextBuilder()
            .WithStaff("pgy1-1", DefaultRanks.PGY1).WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("pgy1-1", 1, "area-icu").WithDuty("r2-1", 5, "area-icu")
            .Build();

        var h2 = new ViolationChecker(ctx).Check(Defaults).Violations.Where(v => v.Code == DefaultConstraints.H2Eligibility).ToArray();

        var only = Assert.Single(h2);
        Assert.Equal(new[] { "area:area-icu:2026-09-01" }, only.CellKeys);
        Assert.Equal(Severity.Hard, only.Severity);
    }

    // ---- H5 不可排班日 ----

    [Fact]
    public void H5_排在登記的不可排班日上是硬違規()
    {
        var ctx = new ContextBuilder()
            .WithStaff("r1-1", DefaultRanks.R1)
            .WithBlockedDay("r1-1", 10)
            .WithDuty("r1-1", 10, "area-b")
            .Build();

        var v = Assert.Single(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H5BlockedDay);
        Assert.Equal(new[] { "area:area-b:2026-09-10" }, v.CellKeys);
    }

    // ---- H3 額度上限（含 R6 逐月覆寫） ----

    [Fact]
    public void H3_R3上限7_累計10點_標超出上限的兩格()
    {
        // 1（二）4（五）7（一）10（四）各 1 點、13（日）2 點 → 累計 6；19（六）2 點 → 8 超過 7；26（六）→ 10。
        // 依日期累計，超過 7 的是 19 與 26 兩格。間隔都 ≥ 3 天避免混入 H4。
        var ctx = new ContextBuilder()
            .WithStaff("r3-1", DefaultRanks.R3)
            .WithDuties("r3-1", "area-icu", 1, 4, 7, 10, 13, 19, 26)
            .Build();

        var v = Assert.Single(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H3QuotaCap);
        Assert.Equal(new[] { "staff:r3-1:2026-09-19", "staff:r3-1:2026-09-26" }, v.CellKeys);
        Assert.Contains("10", v.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void H3_R6預設上限5_當月覆寫成7後7點不違規()
    {
        var withoutOverride = new ContextBuilder().WithStaff("r6-1", DefaultRanks.R6).WithDuties("r6-1", "area-chief", 1, 4, 7, 10, 13, 16).Build();
        var withOverride = new ContextBuilder().WithStaff("r6-1", DefaultRanks.R6).WithDuties("r6-1", "area-chief", 1, 4, 7, 10, 13, 16)
            .WithQuotaCapOverride(DefaultRanks.R6, 7).Build();

        Assert.Contains(new ViolationChecker(withoutOverride).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H3QuotaCap);
        Assert.DoesNotContain(new ViolationChecker(withOverride).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H3QuotaCap);
    }

    // ---- S3 / S4 偏好 ----

    [Fact]
    public void S3_R2值一般病房是軟違規_值ICU不是()
    {
        var ctx = new ContextBuilder()
            .WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-a").WithDuty("r2-1", 5, "area-icu")
            .Build();

        var s3 = new ViolationChecker(ctx).Check(Defaults).Violations.Where(v => v.Code == DefaultConstraints.S3R2R3PreferIcu).ToArray();

        var only = Assert.Single(s3);
        Assert.Equal(Severity.Soft, only.Severity);
        Assert.Equal(new[] { "area:area-a:2026-09-01" }, only.CellKeys);
    }

    [Fact]
    public void S4_R5值ICU是軟違規_PGY1值一般病房與S3S4無關()
    {
        var ctx = new ContextBuilder()
            .WithStaff("r5-1", DefaultRanks.R5).WithStaff("pgy1-1", DefaultRanks.PGY1)
            .WithDuty("r5-1", 1, "area-icu").WithDuty("pgy1-1", 1, "area-a")
            .Build();

        var result = new ViolationChecker(ctx).Check(Defaults);

        Assert.Single(result.Violations, v => v.Code == DefaultConstraints.S4R4R6PreferChief);
        Assert.DoesNotContain(result.Violations, v => v.Code is DefaultConstraints.S3R2R3PreferIcu && v.CellKeys.Contains("area:area-a:2026-09-01"));
    }

    // ---- Fairness / Consistency 不產生違規 ----

    [Fact]
    public void 公平性與延續性是分數_不會出現在違規清單()
    {
        var ctx = new ContextBuilder()
            .WithStaff("r2-1", DefaultRanks.R2).WithStaff("r2-2", DefaultRanks.R2)
            .WithDuties("r2-1", "area-icu", 1, 4, 7, 10, 13, 16, 19)
            .WithDuty("r2-2", 22, "area-a")
            .Build();

        var result = new ViolationChecker(ctx).Check(
            Defaults.With(DefaultConstraints.S7FairnessPoint, c => c with { Weight = 50 }));

        Assert.DoesNotContain(result.Violations, v => v.Code is DefaultConstraints.S1QuotaFairness or DefaultConstraints.S2AreaConsistency or DefaultConstraints.S7FairnessPoint);
    }

    // ---- 結構規則 X1：同人同日兩區（不是約束、不能停用；寫入不擋、發布與匯出才擋，#68） ----

    [Fact]
    public void 同一人同一天排在兩個區域_是硬違規X1_兩格都標_NP也一樣()
    {
        var np = new ContextBuilder().WithStaff("np-1", DefaultRanks.NP)
            .WithDuty("np-1", 1, "area-a").WithDuty("np-1", 1, "area-b").Build();
        var r2 = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-a").WithDuty("r2-1", 1, "area-b").Build();

        foreach (var ctx in new[] { np, r2 })
        {
            var x1 = Assert.Single(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == StructuralRules.StaffDoubleBooked);
            Assert.Equal("X1_STAFF_DOUBLE_BOOKED", x1.Code);
            Assert.Equal(Severity.Hard, x1.Severity);
            Assert.Equal(new[] { "area:area-a:2026-09-01", "area:area-b:2026-09-01" }, x1.CellKeys);
        }

        Assert.Contains("np-1", new ViolationChecker(np).Check(Defaults).Violations.Single(v => v.Code == StructuralRules.StaffDoubleBooked).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void X1_不受約束設定影響_全部約束停用仍會產生_並算進硬違規數()
    {
        var ctx = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-a").WithDuty("r2-1", 1, "area-b").Build();
        var none = new ConstraintSettings(
            Defaults.Hard.Select(c => c with { Enabled = false }).ToArray(),
            Defaults.Soft.Select(c => c with { Weight = 0 }).ToArray());

        var result = new ViolationChecker(ctx).Check(none);

        Assert.Equal(StructuralRules.StaffDoubleBooked, Assert.Single(result.Violations).Code);
        Assert.Equal(1, result.HardCount);
    }

    [Fact]
    public void 同一人同一天沒有重複_不產生X1()
    {
        var ctx = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2).WithDuty("r2-1", 1, "area-a").Build();

        Assert.DoesNotContain(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == StructuralRules.StaffDoubleBooked);
    }

    [Fact]
    public void EnsureConsistent_同人同日兩區不再擲出_同一格兩筆仍擲出()
    {
        var doubleBooked = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-a").WithDuty("r2-1", 1, "area-b").Build();
        var sameCell = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2).WithStaff("r2-2", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-a").WithDuty("r2-2", 1, "area-a").Build();

        doubleBooked.EnsureConsistent();
        Assert.Throws<InvalidOperationException>(sameCell.EnsureConsistent);
    }

    [Fact]
    public void 同一人不同天在不同區域_是合法狀態()
    {
        var ctx = new ContextBuilder().WithStaff("np-1", DefaultRanks.NP)
            .WithDuty("np-1", 1, "area-a").WithDuty("np-1", 2, "area-b").Build();

        _ = new ViolationChecker(ctx).Check(Defaults);
    }

    // ---- Violation.id ----

    [Fact]
    public void 違規id是確定性的_同一違規每次讀都同id_且與cellKeys順序無關()
    {
        var a = Violation.ComputeId("H4_MIN_GAP", new[] { "staff:x:2026-09-01", "staff:x:2026-09-02" });
        var b = Violation.ComputeId("H4_MIN_GAP", new[] { "staff:x:2026-09-01", "staff:x:2026-09-02" });
        var differentCode = Violation.ComputeId("H7_NP_MAX_CONSECUTIVE", new[] { "staff:x:2026-09-01", "staff:x:2026-09-02" });

        Assert.Equal(a, b);
        Assert.NotEqual(a, differentCode);
        Assert.Equal(16, a.Length);

        var constraint = Defaults[DefaultConstraints.H4MinGap];
        var v1 = Violation.Create(constraint, new[] { "staff:x:2026-09-02", "staff:x:2026-09-01" }, "m");
        var v2 = Violation.Create(constraint, new[] { "staff:x:2026-09-01", "staff:x:2026-09-02" }, "m");
        Assert.Equal(v1.Id, v2.Id);
        Assert.Equal(new[] { "staff:x:2026-09-01", "staff:x:2026-09-02" }, v1.CellKeys);
    }

    [Fact]
    public void 同一份值班表檢查兩次_結果逐筆相同()
    {
        var ctx = new ContextBuilder()
            .WithStaff("np-1", DefaultRanks.NP).WithStaff("r2-1", DefaultRanks.R2)
            .WithDuties("np-1", "area-a", 1, 2, 3, 4, 5, 6, 7)
            .WithDuty("r2-1", 1, "area-b")
            .Build();

        var first = new ViolationChecker(ctx).Check(Defaults).Violations.Select(v => v.Id).ToArray();
        var second = new ViolationChecker(ctx).Check(Defaults).Violations.Select(v => v.Id).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(first.Length, first.Distinct().Count());
    }
}
