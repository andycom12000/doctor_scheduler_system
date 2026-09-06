using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduler.Persistence.Tests;

/// <summary>Migration 與模型的一致性。這些測試擋的是「改了 DbContext 忘了加 migration」。</summary>
public class SchemaTests
{
    [Fact]
    public async Task Migration_可以套用在空資料庫上並建出全部的表()
    {
        await using var db = await SqliteDatabase.CreateAsync(initialize: false);
        using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();

        await context.Database.MigrateAsync();

        var tables = await context.Database
            .SqlQueryRaw<string>("select name as Value from sqlite_master where type = 'table' and name not like 'sqlite_%' and name <> '__EFMigrationsHistory'")
            .ToListAsync();

        var expected = new[]
        {
            "schedule", "duty", "blocked_day", "carry_over", "carry_over_applied",
            "staff", "area_type", "area", "rank_group", "rank", "eligibility",
            "point_rule", "fairness_point_table", "constraint_definition", "constraint_scope_entry",
            "monthly_override", "calendar_day",
            "solver_job", "solver_job_warning", "variant", "variant_weight", "variant_duty",
        };
        Assert.Equal(expected.OrderBy(t => t), tables.OrderBy(t => t));
    }

    [Fact]
    public async Task 模型與最後一個_migration_的_snapshot_沒有落差()
    {
        await using var db = await SqliteDatabase.CreateAsync(initialize: false);
        using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var modelRuntimeInitializer = context.GetService<IModelRuntimeInitializer>();

        var snapshotModel = migrationsAssembly.ModelSnapshot?.Model;
        Assert.NotNull(snapshotModel);
        if (snapshotModel is IMutableModel mutable)
        {
            snapshotModel = mutable.FinalizeModel();
        }

        var snapshot = modelRuntimeInitializer.Initialize(snapshotModel!, designTime: true, validationLogger: null).GetRelationalModel();
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = differ.GetDifferences(snapshot, current);
        Assert.True(
            operations.Count == 0,
            "DbContext 的模型與 Migrations/ 的 snapshot 不一致，請跑 dotnet ef migrations add。差異：" +
            string.Join(", ", operations.Select(o => o.GetType().Name)));
    }

    [Fact]
    public async Task 啟動流程可以重複執行()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var before = (await context.AreaTypes.CountAsync(), await context.ConstraintDefinitions.CountAsync(), await context.CalendarDays.CountAsync());

        await SchedulerDatabase.InitializeAsync(db.Services);

        var after = (await context.AreaTypes.CountAsync(), await context.ConstraintDefinitions.CountAsync(), await context.CalendarDays.CountAsync());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task 載入自檢回傳_SQLite_版本()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var version = await SchedulerDatabase.ProbeAsync(db.Services);
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }
}
