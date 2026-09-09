using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Application.Solving;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Tests;

public class BlockedDayRepositoryTests
{
    [Fact]
    public async Task 登記_重複登記不重複_取消_計數()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var sep = new YearMonth(2026, 9);
        var a = new BlockedDay("s1", new DateOnly(2026, 9, 3));
        var b = new BlockedDay("s1", new DateOnly(2026, 9, 4));
        var oct = new BlockedDay("s1", new DateOnly(2026, 10, 1));

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IBlockedDayRepository>();
            await repo.AddAsync(a);
            await repo.AddAsync(a);
            await repo.AddAsync(b);
            await repo.AddAsync(oct);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IBlockedDayRepository>();
            Assert.Equal(new[] { a, b }, await repo.ListAsync(sep));
            Assert.Equal(2, await repo.CountAsync("s1", sep));
            Assert.True(await repo.ExistsAsync(a));

            await repo.RemoveAsync(a);
            await repo.RemoveAsync(new BlockedDay("nobody", a.Date));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IBlockedDayRepository>();
            Assert.Equal(new[] { b }, await repo.ListAsync(sep));
            Assert.False(await repo.ExistsAsync(a));
        }
    }
}

public class StaffRepositoryTests
{
    [Fact]
    public async Task 新增_更新_停用_刪除()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var s1 = new Staff("st-1", "E001", "王小明", "R2");
        var s2 = new Staff("st-2", "E002", "李小華", "NP");

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
            await repo.AddAsync(s2);
            await repo.AddAsync(s1);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
            Assert.Equal(new[] { s1, s2 }, await repo.ListAsync()); // 依員編
            Assert.True(await repo.EmployeeNoTakenAsync("E002", excludeId: null));
            Assert.False(await repo.EmployeeNoTakenAsync("E002", excludeId: "st-2"));
            Assert.True(await repo.AnyWithRankAsync("NP"));
            Assert.False(await repo.AnyWithRankAsync("R6"));

            await repo.UpdateAsync(s1 with { RankCode = "R3", Status = StaffStatus.Inactive });
            await repo.RemoveAsync("st-2");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
            Assert.Equal(s1 with { RankCode = "R3", Status = StaffStatus.Inactive }, await repo.FindAsync("st-1"));
            Assert.Null(await repo.FindAsync("st-2"));
        }
    }

    [Fact]
    public async Task 員編唯一由資料庫守住()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var repo = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        await repo.AddAsync(new Staff("st-1", "E001", "王小明", "R2"));
        await repo.AddAsync(new Staff("st-2", "E001", "李小華", "R3"));

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync());
    }
}

public class CalendarRepositoryTests
{
    [Fact]
    public async Task 使用者覆寫_新增_覆蓋內建_移除()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var typhoon = new DateOnly(2026, 8, 12);
        var teachersDay = new DateOnly(2026, 9, 28);

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
            await repo.UpsertAsync(new CalendarException(new CalendarDay(typhoon, IsHoliday: true, IsPublicHoliday: true, HolidayName: "颱風假"), Overridden: true));
            await repo.UpsertAsync(new CalendarException(new CalendarDay(teachersDay, IsHoliday: false, IsPublicHoliday: false, IsMakeUpWorkday: true), Overridden: true));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
            var added = await repo.FindAsync(typhoon);
            Assert.NotNull(added);
            Assert.True(added!.Overridden);
            Assert.Equal("颱風假", added.Day.HolidayName);

            var overwritten = await repo.FindAsync(teachersDay);
            Assert.NotNull(overwritten);
            Assert.True(overwritten!.Overridden);
            Assert.False(overwritten.Day.IsHoliday);
            Assert.True(overwritten.Day.IsMakeUpWorkday);
            Assert.Null(overwritten.Day.HolidayName);

            var range = await repo.GetExceptionsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30));
            Assert.Equal(new[] { typhoon, new DateOnly(2026, 9, 25), teachersDay }, range.Select(e => e.Day.Date));

            await repo.RemoveAsync(typhoon);
            await repo.RemoveAsync(new DateOnly(2030, 1, 1));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
            Assert.Null(await repo.FindAsync(typhoon));
        }
    }
}

public class SolverJobRepositoryTests
{
    private static readonly YearMonth Sep = new(2026, 9);

    private static SolverJobRecord Queued(string id, DateTimeOffset createdAt) =>
        new(id, Sep, SolverJobStatus.Queued, VariantCount: 3, TimeLimitSecPerVariant: 15, createdAt,
            StartedAt: null, FinishedAt: null, ElapsedSec: null, FailureReason: null,
            Warnings: new[] { "上月值班表仍是草稿，跨月間隔以草稿計" }, Scale: null, ConstraintCount: null);

    [Fact]
    public async Task 工作與變體的完整往返()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var t0 = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(8));
        var job = Queued("job-1", t0);
        var variant = new VariantRecord(
            "job-1", "v-a", "重視公平",
            new Dictionary<string, double> { ["S1_QUOTA_FAIRNESS"] = 1.5, ["S2_AREA_CONSISTENCY"] = 0.5 },
            new VariantMetrics(Vacancies: 0, QuotaFairness: 1, AreaConsistency: 4, RankPreference: 2, FairnessPoint: null),
            HardViolationCount: 0, SoftScore: 123.5,
            new[] { new Duty("area-a", new DateOnly(2026, 9, 1), "s1"), new Duty("area-b", new DateOnly(2026, 9, 1), "s2") });

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            await repo.AddAsync(job);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        var succeeded = job with
        {
            Status = SolverJobStatus.Succeeded,
            StartedAt = t0.AddSeconds(1),
            FinishedAt = t0.AddSeconds(40),
            ElapsedSec = 39.2,
            Scale = new SolverScale(33, 5, 30, 4950),
            ConstraintCount = new ConstraintCount(7, 6),
            Warnings = Array.Empty<string>(),
            LastSolutionCount = 4,
            LastBestObjective = 2_000_012_300,
            LastBestBound = 2_000_011_000,
        };

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            await repo.AddVariantAsync(variant);
            await repo.UpdateAsync(succeeded);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            var read = await repo.FindAsync("job-1");
            Assert.NotNull(read);
            Assert.Equal(succeeded with { Warnings = read!.Warnings }, read);
            Assert.Empty(read.Warnings);

            var variants = await repo.GetVariantsAsync("job-1");
            var v = Assert.Single(variants);
            Assert.Equal((variant.JobId, variant.Id, variant.Label, variant.HardViolationCount, variant.SoftScore), (v.JobId, v.Id, v.Label, v.HardViolationCount, v.SoftScore));
            Assert.Equal(variant.Metrics, v.Metrics);
            Assert.Equal(variant.WeightProfile.OrderBy(kv => kv.Key), v.WeightProfile.OrderBy(kv => kv.Key));
            Assert.Equal(variant.Duties, v.Duties);

            var found = await repo.FindVariantAsync("job-1", "v-a");
            Assert.NotNull(found);
            Assert.Equal(v.Duties, found!.Duties);
            Assert.Equal(v.Metrics, found.Metrics);
            Assert.Null(await repo.FindVariantAsync("job-1", "v-z"));
        }
    }

    [Fact]
    public async Task 同一個工作單元加多個變體_讀回順序照加入順序()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var t0 = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(8));
        static VariantRecord V(string id) => new(
            "job-1", id, id, new Dictionary<string, double>(),
            new VariantMetrics(0, 0, 0, 0, null), 0, 0, Array.Empty<Duty>());

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            await repo.AddAsync(Queued("job-1", t0));
            await repo.AddVariantAsync(V("v-c"));
            await repo.AddVariantAsync(V("v-a"));
            await repo.AddVariantAsync(V("v-b"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            Assert.Equal(new[] { "v-c", "v-a", "v-b" }, (await repo.GetVariantsAsync("job-1")).Select(v => v.Id));
        }
    }

    [Fact]
    public async Task 啟動時把沒跑完的工作標成失敗_已結束的不動()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var t0 = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(8));

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            await repo.AddAsync(Queued("queued", t0));
            await repo.AddAsync(Queued("running", t0.AddMinutes(1)) with { Status = SolverJobStatus.Running });
            await repo.AddAsync(Queued("done", t0.AddMinutes(2)) with { Status = SolverJobStatus.Cancelled });
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await SchedulerDatabase.InitializeAsync(db.Services);

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();
            var all = await repo.ListAsync();
            Assert.Equal(new[] { "done", "running", "queued" }, all.Select(j => j.JobId)); // 建立時間遞減
            Assert.Equal(SolverJobStatus.Failed, all.Single(j => j.JobId == "queued").Status);
            Assert.Equal("程式重啟中斷", all.Single(j => j.JobId == "running").FailureReason);
            Assert.Equal(SolverJobStatus.Cancelled, all.Single(j => j.JobId == "done").Status);
        }
    }
}
