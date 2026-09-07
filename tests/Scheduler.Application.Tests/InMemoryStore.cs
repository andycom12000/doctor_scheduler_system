using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
using Scheduler.Application.Scheduling;
using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

/// <summary>
/// 六個 repository 介面的記憶體版：讀取路徑的測試只在乎 loader 的分支與 handler 的算法，
/// 不在乎 SQL。設定全部從出廠值起，測試只改自己要的那一項。
/// 順便記下 <see cref="DutyRangeQueries"/>，好驗證上月尾巴撈了幾天。
/// </summary>
public sealed class InMemoryStore : IScheduleRepository, IBlockedDayRepository, IStaffRepository, ISettingsRepository, ICalendarRepository, IUnitOfWork
{
    /// <summary>寫入測試用：handler 呼叫了幾次 <see cref="CommitAsync"/>。記憶體版沒有交易，寫入即生效。</summary>
    public int Commits { get; private set; }

    public Dictionary<YearMonth, ScheduleHeader> Headers { get; } = new();
    public List<Duty> Duties { get; } = new();
    public Dictionary<YearMonth, List<CarryOverEntry>> CarryOver { get; } = new();
    public Dictionary<YearMonth, List<CarryOverEntry>> CarryOverApplied { get; } = new();
    public List<BlockedDay> BlockedDays { get; } = new();
    public List<Staff> Staff { get; } = new();
    public Dictionary<DateOnly, CalendarException> CalendarExceptions { get; } = new();
    public Dictionary<YearMonth, MonthlyOverride> Overrides { get; } = new();
    public List<(DateOnly From, DateOnly To)> DutyRangeQueries { get; } = new();

    public AreaSettings Areas { get; set; } = new(DefaultAreas.AreaTypes, DefaultAreas.Areas);
    public RankSettings Ranks { get; set; } = new(DefaultRanks.Groups, DefaultRanks.Ranks);
    public EligibilityMatrix Eligibility { get; set; } = DefaultRanks.Eligibility;
    public PointRules PointRules { get; set; } = DefaultPointRules.Rules;
    public ConstraintSettings Constraints { get; set; } = DefaultConstraints.Settings;

    public SchedulingContextLoader Loader => new(this, this, this, this, this);

    // ---- 情境建構 ----

    public InMemoryStore WithStaff(string id, string rankCode, StaffStatus status = StaffStatus.Active)
    {
        Staff.Add(new Staff(id, $"E{Staff.Count + 1:000}", $"人員 {id}", rankCode, status));
        return this;
    }

    public InMemoryStore WithDraft(YearMonth month)
    {
        Headers[month] = ScheduleHeader.NewDraft(month);
        return this;
    }

    public InMemoryStore WithPublished(YearMonth month, params CarryOverEntry[] carryOver)
    {
        Headers[month] = new ScheduleHeader(month, ScheduleStatus.Published, 1, new DateTimeOffset(month.LastDay.AddDays(-3), TimeOnly.MinValue, TimeSpan.FromHours(8)));
        CarryOver[month] = carryOver.ToList();
        return this;
    }

    public InMemoryStore WithDuty(string areaId, DateOnly date, string staffId)
    {
        Duties.Add(new Duty(areaId, date, staffId));
        return this;
    }

    public InMemoryStore WithBlockedDay(string staffId, DateOnly date)
    {
        BlockedDays.Add(new BlockedDay(staffId, date));
        return this;
    }

    public InMemoryStore WithHoliday(DateOnly date, string name)
    {
        CalendarExceptions[date] = new CalendarException(new CalendarDay(date, IsHoliday: true, IsPublicHoliday: true, HolidayName: name), Overridden: false);
        return this;
    }

    // ---- IScheduleRepository ----

    public Task<ScheduleHeader?> FindAsync(YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult(Headers.GetValueOrDefault(yearMonth));

    public Task<IReadOnlyList<ScheduleHeader>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScheduleHeader>>(Headers.Values.ToArray());

    public Task UpsertAsync(ScheduleHeader header, CancellationToken ct = default)
    {
        Headers[header.YearMonth] = header;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Duty>> GetDutiesAsync(YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Duty>>(Duties.Where(d => yearMonth.Contains(d.Date)).ToArray());

    public Task<IReadOnlyList<Duty>> GetDutiesInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        DutyRangeQueries.Add((from, to));
        return Task.FromResult<IReadOnlyList<Duty>>(Duties.Where(d => d.Date >= from && d.Date <= to).ToArray());
    }

    public Task<bool> AnyDutyForStaffAsync(string staffId, CancellationToken ct = default) => Task.FromResult(Duties.Any(d => d.StaffId == staffId));

    public Task<bool> AnyDutyForAreaAsync(string areaId, CancellationToken ct = default) => Task.FromResult(Duties.Any(d => d.AreaId == areaId));

    public Task SetDutyAsync(YearMonth yearMonth, string areaId, DateOnly date, string? staffId, CancellationToken ct = default)
    {
        Duties.RemoveAll(d => d.AreaId == areaId && d.Date == date);
        if (staffId is not null)
        {
            Duties.Add(new Duty(areaId, date, staffId));
        }

        return Task.CompletedTask;
    }

    public Task ReplaceDutiesAsync(YearMonth yearMonth, IReadOnlyList<Duty> duties, CancellationToken ct = default)
    {
        Duties.RemoveAll(d => yearMonth.Contains(d.Date));
        Duties.AddRange(duties);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAsync(YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CarryOverEntry>>(CarryOver.GetValueOrDefault(yearMonth) ?? new List<CarryOverEntry>());

    public Task ReplaceCarryOverAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken ct = default)
    {
        CarryOver[yearMonth] = entries.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAppliedAsync(YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CarryOverEntry>>(CarryOverApplied.GetValueOrDefault(yearMonth) ?? new List<CarryOverEntry>());

    public Task ReplaceCarryOverAppliedAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken ct = default)
    {
        CarryOverApplied[yearMonth] = entries.ToList();
        return Task.CompletedTask;
    }

    // ---- IBlockedDayRepository ----

    Task<IReadOnlyList<BlockedDay>> IBlockedDayRepository.ListAsync(YearMonth yearMonth, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BlockedDay>>(BlockedDays.Where(b => yearMonth.Contains(b.Date)).ToArray());

    public Task<bool> ExistsAsync(BlockedDay blockedDay, CancellationToken ct = default) => Task.FromResult(BlockedDays.Contains(blockedDay));

    public Task<int> CountAsync(string staffId, YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult(BlockedDays.Count(b => b.StaffId == staffId && yearMonth.Contains(b.Date)));

    public Task AddAsync(BlockedDay blockedDay, CancellationToken ct = default)
    {
        if (!BlockedDays.Contains(blockedDay))
        {
            BlockedDays.Add(blockedDay);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(BlockedDay blockedDay, CancellationToken ct = default)
    {
        BlockedDays.Remove(blockedDay);
        return Task.CompletedTask;
    }

    public Task<int> RemoveAllForStaffAsync(string staffId, CancellationToken ct = default) =>
        Task.FromResult(BlockedDays.RemoveAll(b => b.StaffId == staffId));

    // ---- IStaffRepository ----

    Task<IReadOnlyList<Staff>> IStaffRepository.ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Staff>>(Staff.OrderBy(s => s.EmployeeNo, StringComparer.Ordinal).ToArray());

    Task<Staff?> IStaffRepository.FindAsync(string id, CancellationToken ct) => Task.FromResult(Staff.FirstOrDefault(s => s.Id == id));

    public Task<bool> EmployeeNoTakenAsync(string employeeNo, string? excludeId, CancellationToken ct = default) =>
        Task.FromResult(Staff.Any(s => s.EmployeeNo == employeeNo && s.Id != excludeId));

    public Task<bool> AnyWithRankAsync(string rankCode, CancellationToken ct = default) => Task.FromResult(Staff.Any(s => s.RankCode == rankCode));

    public Task AddAsync(Staff staff, CancellationToken ct = default)
    {
        Staff.Add(staff);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Staff staff, CancellationToken ct = default)
    {
        var index = Staff.FindIndex(s => s.Id == staff.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException(staff.Id);
        }

        Staff[index] = staff;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, CancellationToken ct = default)
    {
        Staff.RemoveAll(s => s.Id == id);
        return Task.CompletedTask;
    }

    // ---- ISettingsRepository ----

    public Task<AreaSettings> GetAreasAsync(CancellationToken ct = default) => Task.FromResult(Areas);

    public Task ReplaceAreasAsync(AreaSettings settings, CancellationToken ct = default)
    {
        Areas = settings;
        return Task.CompletedTask;
    }

    public Task<RankSettings> GetRanksAsync(CancellationToken ct = default) => Task.FromResult(Ranks);

    public Task ReplaceRanksAsync(RankSettings settings, CancellationToken ct = default)
    {
        Ranks = settings;
        return Task.CompletedTask;
    }

    public Task<EligibilityMatrix> GetEligibilityAsync(CancellationToken ct = default) => Task.FromResult(Eligibility);

    public Task ReplaceEligibilityAsync(EligibilityMatrix matrix, CancellationToken ct = default)
    {
        Eligibility = matrix;
        return Task.CompletedTask;
    }

    public Task<PointRules> GetPointRulesAsync(CancellationToken ct = default) => Task.FromResult(PointRules);

    public Task ReplacePointRulesAsync(PointRules rules, CancellationToken ct = default)
    {
        PointRules = rules;
        return Task.CompletedTask;
    }

    public Task<ConstraintSettings> GetConstraintsAsync(CancellationToken ct = default) => Task.FromResult(Constraints);

    public Task ReplaceConstraintsAsync(ConstraintSettings settings, CancellationToken ct = default)
    {
        Constraints = settings;
        return Task.CompletedTask;
    }

    public Task<MonthlyOverride> GetMonthlyOverrideAsync(YearMonth yearMonth, CancellationToken ct = default) =>
        Task.FromResult(Overrides.GetValueOrDefault(yearMonth) ?? MonthlyOverride.Empty(yearMonth));

    public Task ReplaceMonthlyOverrideAsync(MonthlyOverride monthlyOverride, CancellationToken ct = default)
    {
        Overrides[monthlyOverride.YearMonth] = monthlyOverride;
        return Task.CompletedTask;
    }

    // ---- ICalendarRepository ----

    public Task<IReadOnlyList<CalendarException>> GetExceptionsAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CalendarException>>(
            CalendarExceptions.Values.Where(e => e.Day.Date >= from && e.Day.Date <= to).OrderBy(e => e.Day.Date).ToArray());

    Task<CalendarException?> ICalendarRepository.FindAsync(DateOnly date, CancellationToken ct) =>
        Task.FromResult(CalendarExceptions.GetValueOrDefault(date));

    public Task UpsertAsync(CalendarException exception, CancellationToken ct = default)
    {
        CalendarExceptions[exception.Day.Date] = exception;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(DateOnly date, CancellationToken ct = default)
    {
        CalendarExceptions.Remove(date);
        return Task.CompletedTask;
    }

    // ---- IUnitOfWork ----

    public Task CommitAsync(CancellationToken ct = default)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
