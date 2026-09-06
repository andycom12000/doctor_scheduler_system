using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Tests;

/// <summary>
/// Domain record 裡有集合與字典（<see cref="ConstraintScope"/>、<see cref="EligibilityMatrix"/>、
/// <see cref="FairnessPointRule.Tables"/>），record 的相等是參考相等，所以要逐欄比。
/// </summary>
internal static class DomainEquality
{
    public static void AssertEqual(ConstraintSettings expected, ConstraintSettings actual)
    {
        Assert.Equal(expected.Hard.Select(c => c.Code), actual.Hard.Select(c => c.Code));
        Assert.Equal(expected.Soft.Select(c => c.Code), actual.Soft.Select(c => c.Code));
        foreach (var (e, a) in expected.All.Zip(actual.All))
        {
            AssertEqual(e, a);
        }
    }

    public static void AssertEqual(ConstraintDefinition expected, ConstraintDefinition actual)
    {
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Primitive, actual.Primitive);
        Assert.Equal(expected.Severity, actual.Severity);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Weight, actual.Weight);
        Assert.Equal(expected.Metric, actual.Metric);
        Assert.Equal(expected.Params, actual.Params);
        AssertEqual(expected.Scope, actual.Scope);
    }

    public static void AssertEqual(ConstraintScope expected, ConstraintScope actual)
    {
        AssertSetEqual(expected.RankCodes, actual.RankCodes);
        AssertSetEqual(expected.ExemptRankCodes, actual.ExemptRankCodes);
        AssertSetEqual(expected.AreaTypeCodes, actual.AreaTypeCodes);
        AssertSetEqual(expected.DayKinds, actual.DayKinds);
    }

    public static void AssertEqual(EligibilityMatrix expected, EligibilityMatrix actual)
    {
        Assert.Equal(expected.Matrix.Keys.OrderBy(k => k), actual.Matrix.Keys.OrderBy(k => k));
        foreach (var rank in expected.Matrix.Keys)
        {
            Assert.Equal(
                expected.Matrix[rank].OrderBy(kv => kv.Key),
                actual.Matrix[rank].OrderBy(kv => kv.Key));
        }
    }

    public static void AssertEqual(PointRules expected, PointRules actual)
    {
        Assert.Equal(expected.Quota, actual.Quota);
        Assert.Equal(expected.Fairness.ConsecutiveSaturdayBonus, actual.Fairness.ConsecutiveSaturdayBonus);
        Assert.Equal(expected.Fairness.Tables.Keys.OrderBy(k => k), actual.Fairness.Tables.Keys.OrderBy(k => k));
        foreach (var type in expected.Fairness.Tables.Keys)
        {
            Assert.Equal(
                expected.Fairness.Tables[type].OrderBy(e => e.Today).ThenBy(e => e.Tomorrow),
                actual.Fairness.Tables[type].OrderBy(e => e.Today).ThenBy(e => e.Tomorrow));
        }
    }

    private static void AssertSetEqual<T>(IReadOnlySet<T>? expected, IReadOnlySet<T>? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.True(expected.SetEquals(actual!), $"集合不同：[{string.Join(",", expected)}] vs [{string.Join(",", actual!)}]");
    }
}
