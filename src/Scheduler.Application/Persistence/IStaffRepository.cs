using Scheduler.Domain.Model;

namespace Scheduler.Application.Persistence;

/// <summary>人員名冊。<c>Staff.eligibleAreaTypes</c> 由資格矩陣推導，不儲存。</summary>
public interface IStaffRepository
{
    /// <summary>全部人員（含停用），依員編遞增。</summary>
    Task<IReadOnlyList<Staff>> ListAsync(CancellationToken cancellationToken = default);

    Task<Staff?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>員編是否已被別人使用。<paramref name="excludeId"/> 是本人（更新時排除自己）。</summary>
    Task<bool> EmployeeNoTakenAsync(string employeeNo, string? excludeId, CancellationToken cancellationToken = default);

    /// <summary>某身分是否有人使用。<c>RANK_IN_USE</c> 用。</summary>
    Task<bool> AnyWithRankAsync(string rankCode, CancellationToken cancellationToken = default);

    Task AddAsync(Staff staff, CancellationToken cancellationToken = default);

    /// <summary>依 <see cref="Staff.Id"/> 更新全部欄位。不存在時擲出例外。</summary>
    Task UpdateAsync(Staff staff, CancellationToken cancellationToken = default);

    /// <summary>硬刪。不存在時不動作。</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}
