using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.People;

/// <summary>契約 <c>StaffWrite</c>。<c>eligibleAreaTypes</c> 不由 client 送入，由資格矩陣推導。</summary>
public sealed record StaffWrite(string EmployeeNo, string Name, string RankCode);

/// <summary>
/// 人員名冊的寫入路徑。停用／復職不影響歷史值班表；有值班紀錄者不可刪、只能停用；
/// 刪除時級聯清掉他的不可排班日登記（登記是求解輸入，不是歷史事實）。
/// </summary>
public sealed class StaffCommands
{
    private readonly IStaffRepository _staff;
    private readonly IScheduleRepository _schedules;
    private readonly IBlockedDayRepository _blockedDays;
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;

    public StaffCommands(
        IStaffRepository staff,
        IScheduleRepository schedules,
        IBlockedDayRepository blockedDays,
        ISettingsRepository settings,
        IUnitOfWork unitOfWork)
    {
        _staff = staff;
        _schedules = schedules;
        _blockedDays = blockedDays;
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    public async Task<StaffView> CreateAsync(StaffWrite write, CancellationToken cancellationToken = default)
    {
        await EnsureWriteValidAsync(write, excludeId: null, cancellationToken);
        var staff = new Staff(NewId(), write.EmployeeNo.Trim(), write.Name.Trim(), write.RankCode, StaffStatus.Active);
        await _staff.AddAsync(staff, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await ViewOfAsync(staff, cancellationToken);
    }

    public async Task<StaffView> UpdateAsync(string id, StaffWrite write, CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(id, cancellationToken);
        await EnsureWriteValidAsync(write, excludeId: id, cancellationToken);
        var updated = existing with { EmployeeNo = write.EmployeeNo.Trim(), Name = write.Name.Trim(), RankCode = write.RankCode };
        await _staff.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await ViewOfAsync(updated, cancellationToken);
    }

    public async Task<StaffView> SetStatusAsync(string id, StaffStatus status, CancellationToken cancellationToken = default)
    {
        var existing = await RequireAsync(id, cancellationToken);
        var updated = existing with { Status = status };
        await _staff.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await ViewOfAsync(updated, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await RequireAsync(id, cancellationToken);
        if (await _schedules.AnyDutyForStaffAsync(id, cancellationToken))
        {
            throw new SchedulerException(ErrorCode.StaffHasDuties, "已有值班紀錄，不可刪除，請改為停用");
        }

        await _blockedDays.RemoveAllForStaffAsync(id, cancellationToken);
        await _staff.RemoveAsync(id, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
    }

    // ---- helpers ----

    private async Task<Staff> RequireAsync(string id, CancellationToken cancellationToken) =>
        await _staff.FindAsync(id, cancellationToken) ?? throw SchedulerException.NotFound($"找不到人員 {id}");

    /// <summary>員編、姓名不得空白；身分必須存在（422）；員編不得與他人重複（409）。</summary>
    private async Task EnsureWriteValidAsync(StaffWrite write, string? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(write.EmployeeNo))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "員編不得空白");
        }

        if (string.IsNullOrWhiteSpace(write.Name))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "姓名不得空白");
        }

        var ranks = await _settings.GetRanksAsync(cancellationToken);
        if (!ranks.Ranks.Any(r => r.Code == write.RankCode))
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"找不到身分 {write.RankCode}");
        }

        if (await _staff.EmployeeNoTakenAsync(write.EmployeeNo.Trim(), excludeId, cancellationToken))
        {
            throw new SchedulerException(ErrorCode.EmployeeNoTaken, $"員編 {write.EmployeeNo.Trim()} 已被使用");
        }
    }

    private async Task<StaffView> ViewOfAsync(Staff staff, CancellationToken cancellationToken)
    {
        var eligibility = await _settings.GetEligibilityAsync(cancellationToken);
        return new StaffView(staff.Id, staff.EmployeeNo, staff.Name, staff.RankCode, staff.Status, eligibility.EligibleAreaTypes(staff.RankCode));
    }

    /// <summary>識別子與員編脫鉤：員編可以改，id 不能。</summary>
    private static string NewId() => "s-" + Guid.NewGuid().ToString("N")[..12];
}
