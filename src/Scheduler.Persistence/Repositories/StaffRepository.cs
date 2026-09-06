using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Mapping;

namespace Scheduler.Persistence.Repositories;

internal sealed class StaffRepository : IStaffRepository
{
    private readonly SchedulerDbContext _db;

    public StaffRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Staff>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Staff.AsNoTracking()
            .OrderBy(s => s.EmployeeNo)
            .Select(s => ToDomain(s))
            .ToListAsync(cancellationToken);

    public async Task<Staff?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public Task<bool> EmployeeNoTakenAsync(string employeeNo, string? excludeId, CancellationToken cancellationToken = default) =>
        _db.Staff.AnyAsync(s => s.EmployeeNo == employeeNo && (excludeId == null || s.Id != excludeId), cancellationToken);

    public Task<bool> AnyWithRankAsync(string rankCode, CancellationToken cancellationToken = default) =>
        _db.Staff.AnyAsync(s => s.RankCode == rankCode, cancellationToken);

    public Task AddAsync(Staff staff, CancellationToken cancellationToken = default)
    {
        _db.Staff.Add(new StaffEntity
        {
            Id = staff.Id,
            EmployeeNo = staff.EmployeeNo,
            Name = staff.Name,
            RankCode = staff.RankCode,
            Status = EnumNames.Of(staff.Status),
        });
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(Staff staff, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Staff.FindAsync(new object[] { staff.Id }, cancellationToken)
            ?? throw new KeyNotFoundException($"沒有 id 為 {staff.Id} 的人員");
        entity.EmployeeNo = staff.EmployeeNo;
        entity.Name = staff.Name;
        entity.RankCode = staff.RankCode;
        entity.Status = EnumNames.Of(staff.Status);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Staff.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            _db.Staff.Remove(entity);
        }
    }

    private static Staff ToDomain(StaffEntity s) =>
        new(s.Id, s.EmployeeNo, s.Name, s.RankCode, EnumNames.ToStaffStatus(s.Status));
}
