using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.People;

/// <summary><c>GET /staff</c>。</summary>
public sealed record StaffList(IReadOnlyList<StaffView> Items, StaffCounts Counts);

public sealed record StaffCounts(int Active, int Inactive);

public sealed record StaffView(
    string Id,
    string EmployeeNo,
    string Name,
    string RankCode,
    StaffStatus Status,
    IReadOnlyList<string> EligibleAreaTypes);

public sealed class StaffQueries
{
    private readonly IStaffRepository _staff;
    private readonly ISettingsRepository _settings;

    public StaffQueries(IStaffRepository staff, ISettingsRepository settings)
    {
        _staff = staff;
        _settings = settings;
    }

    /// <summary>不分頁、一次全帶；<c>counts</c> 不受 <paramref name="status"/> 篩選影響。</summary>
    public async Task<StaffList> ListAsync(StaffStatus? status, CancellationToken cancellationToken = default)
    {
        var all = await _staff.ListAsync(cancellationToken);
        var eligibility = await _settings.GetEligibilityAsync(cancellationToken);

        var items = all
            .Where(s => status is null || s.Status == status)
            .Select(s => new StaffView(s.Id, s.EmployeeNo, s.Name, s.RankCode, s.Status, eligibility.EligibleAreaTypes(s.RankCode)))
            .ToArray();
        var counts = new StaffCounts(
            all.Count(s => s.Status == StaffStatus.Active),
            all.Count(s => s.Status == StaffStatus.Inactive));
        return new StaffList(items, counts);
    }
}
