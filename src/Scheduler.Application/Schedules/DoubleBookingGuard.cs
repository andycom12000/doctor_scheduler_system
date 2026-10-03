using Scheduler.Application.Errors;
using Scheduler.Domain.Validation;

namespace Scheduler.Application.Schedules;

/// <summary>
/// 發布、匯出共用的把關：值班表上有同人同日兩區（X1，<see cref="StructuralRules.StaffDoubleBooked"/>）就
/// 409 <see cref="ErrorCode.DoubleBookingPresent"/>。理由是一個人物理上不可能同時在兩區，這份班表必然有錯，
/// 不該發出去也不該流出去（#68）。判斷直接讀檢查器的結果，不另寫一份。
/// </summary>
internal static class DoubleBookingGuard
{
    /// <param name="action">動詞，用在訊息裡：「發布」「匯出」。</param>
    public static void EnsureNone(ValidationResult validation, string action)
    {
        var count = validation.Violations.Count(v => v.Code == StructuralRules.StaffDoubleBooked);
        if (count > 0)
        {
            throw new SchedulerException(
                ErrorCode.DoubleBookingPresent,
                $"仍有 {count} 項同一人同一天排在兩區，排除後才能{action}",
                new Dictionary<string, object?> { ["doubleBookingCount"] = count, ["hardViolationCount"] = validation.HardCount });
        }
    }
}
