namespace Scheduler.Application.Errors;

/// <summary>
/// 契約 <c>ErrorResponse.error.code</c> 的完整清單。HTTP 狀態碼的對應只寫在 <c>Scheduler.Api</c>，
/// 這裡只是語義。
/// </summary>
public enum ErrorCode
{
    NotFound,
    InvalidRequest,
    BlockedDayCapExceeded,
    HardViolationsPresent,
    ScheduleAlreadyPublished,
    DoubleBookingPresent,
    AreaInUse,
    AreaTypeInUse,
    RankInUse,
    EmployeeNoTaken,
    StaffHasDuties,
    SolverBusy,
    SolverFailed,
    CalendarSyncInProgress,
}

/// <summary>
/// Application 對外唯一的「業務錯誤」。訊息是可以直接顯示給使用者的中文，
/// <see cref="Details"/> 對應契約的 <c>error.details</c>。
/// </summary>
public sealed class SchedulerException : Exception
{
    public SchedulerException(ErrorCode code, string message, IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public ErrorCode Code { get; }

    public IReadOnlyDictionary<string, object?>? Details { get; }

    public static SchedulerException NotFound(string message) => new(ErrorCode.NotFound, message);

    public static SchedulerException ScheduleNotFound(Domain.Model.YearMonth yearMonth) =>
        NotFound($"{yearMonth} 尚無值班表");
}
