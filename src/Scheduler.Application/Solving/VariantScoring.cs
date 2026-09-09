using Scheduler.Domain.Constraints;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Validation;

namespace Scheduler.Application.Solving;

/// <summary>
/// 變體的軟分數：Σ（使用者權重 × Domain 算出的分數），只算啟用中的軟約束，越小越好。
/// Fairness 與 Consistency 指不到格子，用 <see cref="ScheduleScores"/>；其餘原語是那條約束的違規數。
/// 與 Solver 目標函數的對應：<c>目標值 = 1e9 × 空缺數 + 100 × 本分數</c>（權重不乘立場乘數時），
/// <c>tests/Scheduler.Solver.Tests</c> 守這個等式，建模與 Domain 定義漂移時會直接看出來（§4.8）。
/// </summary>
public static class VariantScoring
{
    public static double SoftScore(SchedulingContext ctx, ConstraintSettings constraints) =>
        SoftScore(ctx, constraints, new ViolationChecker(ctx).Check(constraints));

    public static double SoftScore(SchedulingContext ctx, ConstraintSettings constraints, ValidationResult validation)
    {
        var scores = new ScheduleScores(ctx);
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

        return softScore;
    }
}
