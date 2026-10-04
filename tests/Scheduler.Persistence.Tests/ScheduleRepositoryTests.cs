using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Tests;

public class ScheduleRepositoryTests
{
    private static readonly YearMonth Sep = new(2026, 9);
    private static readonly YearMonth Oct = new(2026, 10);

    [Fact]
    public async Task 標頭_upsert_後在另一個_scope_讀得到()
    {
        await using var db = await SqliteDatabase.CreateAsync();

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            var header = await repo.FindAsync(Sep);
            Assert.Equal(ScheduleHeader.NewDraft(Sep), header);
            Assert.Null(await repo.FindAsync(Oct));
        }
    }

    [Fact]
    public async Task 發布會更新狀態_修改次數與發布時間()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var publishedAt = new DateTimeOffset(2026, 8, 28, 10, 30, 0, TimeSpan.FromHours(8));

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(new ScheduleHeader(Sep, ScheduleStatus.Published, Revision: 3, publishedAt, PublishedVersion: 2, PublishedRevision: 3));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            var header = await repo.FindAsync(Sep);
            Assert.Equal(new ScheduleHeader(Sep, ScheduleStatus.Published, 3, publishedAt, 2, 3), header);
            Assert.Single(await repo.ListAsync());
        }
    }

    [Fact]
    public async Task 設定_覆寫_清空一格()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var d5 = new DateOnly(2026, 9, 5);

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await repo.SetDutyAsync(Sep, "area-a", d5, "s1");
            await repo.SetDutyAsync(Sep, "area-b", d5, "s2");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.SetDutyAsync(Sep, "area-a", d5, "s3");
            await repo.SetDutyAsync(Sep, "area-b", d5, null);
            await repo.SetDutyAsync(Sep, "area-c", d5, null); // 本來就是空的，不動作
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            var duties = await repo.GetDutiesAsync(Sep);
            Assert.Equal(new[] { new Duty("area-a", d5, "s3") }, duties);
            Assert.True(await repo.AnyDutyForStaffAsync("s3"));
            Assert.False(await repo.AnyDutyForStaffAsync("s2"));
            Assert.True(await repo.AnyDutyForAreaAsync("area-a"));
            Assert.False(await repo.AnyDutyForAreaAsync("area-b"));
        }
    }

    [Fact]
    public async Task 整月取代格子_保留仍在的_刪掉不在的_加上新的()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var d1 = new DateOnly(2026, 9, 1);
        var d2 = new DateOnly(2026, 9, 2);
        var d3 = new DateOnly(2026, 9, 3);

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await repo.SetDutyAsync(Sep, "area-a", d1, "s1");
            await repo.SetDutyAsync(Sep, "area-a", d2, "s2");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        var replacement = new[]
        {
            new Duty("area-a", d1, "s9"),
            new Duty("area-a", d3, "s3"),
        };

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.ReplaceDutiesAsync(Sep, replacement);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            Assert.Equal(replacement, await repo.GetDutiesAsync(Sep));
        }
    }

    [Fact]
    public async Task 日期不在該月就拒收()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
        var oct1 = new DateOnly(2026, 10, 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.SetDutyAsync(Sep, "area-a", oct1, "s1"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.ReplaceDutiesAsync(Sep, new[] { new Duty("area-a", oct1, "s1") }));
    }

    [Fact]
    public async Task 日期區間查詢跨月份()
    {
        await using var db = await SqliteDatabase.CreateAsync();

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Oct));
            await repo.SetDutyAsync(Sep, "area-a", new DateOnly(2026, 9, 28), "s1");
            await repo.SetDutyAsync(Sep, "area-a", new DateOnly(2026, 9, 30), "s2");
            await repo.SetDutyAsync(Oct, "area-a", new DateOnly(2026, 10, 1), "s3");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            var tail = await repo.GetDutiesInRangeAsync(new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 1));
            Assert.Equal(new[] { "s2", "s3" }, tail.Select(d => d.StaffId));
        }
    }

    [Fact]
    public async Task 月結轉與凍結的上月月結轉是兩份獨立資料()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var settled = new[] { new CarryOverEntry("s1", 2), new CarryOverEntry("s2", 0) };
        var applied = new[] { new CarryOverEntry("s1", 1) };

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
            await repo.ReplaceCarryOverAsync(Sep, settled);
            await repo.ReplaceCarryOverAppliedAsync(Sep, applied);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            Assert.Equal(settled, await repo.GetCarryOverAsync(Sep));
            Assert.Equal(applied, await repo.GetCarryOverAppliedAsync(Sep));
            Assert.Empty(await repo.GetCarryOverAsync(Oct));
        }

        // 重新發布：整份覆寫月結轉，凍結的那份不動
        var resettled = new[] { new CarryOverEntry("s2", 3) };
        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.ReplaceCarryOverAsync(Sep, resettled);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            Assert.Equal(resettled, await repo.GetCarryOverAsync(Sep));
            Assert.Equal(applied, await repo.GetCarryOverAppliedAsync(Sep));
        }
    }

    [Fact]
    public async Task 沒有_commit_的變更不會落盤()
    {
        await using var db = await SqliteDatabase.CreateAsync();

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            await repo.UpsertAsync(ScheduleHeader.NewDraft(Sep));
        }

        using (var scope = db.Scope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
            Assert.Null(await repo.FindAsync(Sep));
        }
    }
}
