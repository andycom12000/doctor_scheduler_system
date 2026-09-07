using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Tests;
using Scheduler.Domain.Validation;

namespace Scheduler.Solver.Tests;

/// <summary>
/// 憑空造情境、真的跑 CP-SAT、把輸出丟回 Domain 檢查器。fixture 借 Domain.Tests 的 <see cref="ContextBuilder"/>，
/// 出廠值只有一份抄寫來源。時間上限刻意短：這裡守的是「不漂移」，不是解的品質。
/// </summary>
internal static class SolverFixture
{
    public static readonly TimeSpan ShortLimit = TimeSpan.FromSeconds(3);

    /// <summary>docs/constraint-defaults.md 的參考人數組成：33 位醫師 + 1 位 NP。id 形如 <c>R4-1</c>。</summary>
    public static ContextBuilder WithReferenceRoster(this ContextBuilder b)
    {
        foreach (var (rank, count) in DefaultRanks.ReferenceHeadcount)
        {
            for (var i = 1; i <= count; i++)
            {
                b.WithStaff($"{rank}-{i}", rank);
            }
        }

        return b;
    }

    public static IReadOnlyDictionary<string, double> UserWeights(ConstraintSettings settings) =>
        settings.Soft.ToDictionary(c => c.Code, c => (double)c.Weight, StringComparer.Ordinal);

    public static Task<SolveResult> SolveAsync(
        SchedulingContext ctx,
        ConstraintSettings? constraints = null,
        IReadOnlyList<IReadOnlyList<Duty>>? avoid = null,
        int minDifferent = VariantProfiles.MinDifferentCells,
        TimeSpan? limit = null,
        CancellationToken cancellationToken = default)
    {
        var settings = constraints ?? DefaultConstraints.Settings;
        var request = new SolveRequest(ctx, settings, UserWeights(settings), avoid ?? Array.Empty<IReadOnlyList<Duty>>(), minDifferent, limit ?? ShortLimit);
        return new CpSatSolver().SolveAsync(request, null, cancellationToken);
    }

    public static SchedulingContext WithDuties(this SchedulingContext c, IReadOnlyList<Duty> duties) => new(
        c.Month, c.Calendar, c.Areas, c.Staff, c.Ranks, c.Eligibility, c.PointRules, c.Override,
        duties, c.PreviousMonthDuties, c.BlockedDays, c.CarryOver);

    /// <summary>漂移守門：硬違規只准是覆蓋（H1）。回傳檢查結果讓測試再看細節。</summary>
    public static ValidationResult AssertOnlyCoverageViolations(SchedulingContext ctx, IReadOnlyList<Duty> duties, ConstraintSettings? constraints = null)
    {
        var settings = constraints ?? DefaultConstraints.Settings;
        var coverageCodes = settings.Hard.Where(c => c.Primitive == Primitive.ExactCount).Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var result = new ViolationChecker(ctx.WithDuties(duties)).Check(settings);
        var drift = result.Violations.Where(v => v.Severity == Severity.Hard && !coverageCodes.Contains(v.Code)).ToArray();
        Assert.True(drift.Length == 0,
            "求解器輸出違反了檢查器的硬約束（建模與 Domain 定義漂移）：\n" + string.Join("\n", drift.Select(v => $"{v.Code}: {v.Message}")));
        return result;
    }

    public static int Vacancies(SchedulingContext ctx, IReadOnlyList<Duty> duties)
    {
        var filled = duties.Select(d => (d.AreaId, d.Date)).ToHashSet();
        return ctx.Areas.Sum(a => ctx.Month.Days().Count(d => !filled.Contains((a.Id, d))));
    }
}
