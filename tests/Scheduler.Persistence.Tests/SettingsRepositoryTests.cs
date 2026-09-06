using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Tests;

public class SettingsRepositoryTests
{
    [Fact]
    public async Task 整份取代區域_少一個區域_多一個區域類型()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var replacement = new AreaSettings(
            DefaultAreas.AreaTypes.Append(new AreaType("ER", "急診")).ToArray(),
            DefaultAreas.Areas.Where(a => a.Id != "area-c").Append(new Area("area-er", "ER", "急診", "ER", 2)).ToArray());

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceAreasAsync(replacement);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var read = await settings.GetAreasAsync();
            Assert.Equal(replacement.AreaTypes, read.AreaTypes);
            Assert.Equal(replacement.Areas, read.Areas);
        }
    }

    [Fact]
    public async Task 整份取代身分_改上限與改組()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var replacement = new RankSettings(
            DefaultRanks.Groups,
            DefaultRanks.Ranks.Select(r => r.Code == DefaultRanks.R6 ? r with { QuotaCap = 4 } : r).ToArray());

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceRanksAsync(replacement);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var read = await settings.GetRanksAsync();
            Assert.Equal(replacement.Ranks, read.Ranks);
            Assert.Equal(4, read.Ranks.Single(r => r.Code == DefaultRanks.R6).QuotaCap);
            Assert.Null(read.Ranks.Single(r => r.Code == DefaultRanks.NP).QuotaCap);
        }
    }

    [Fact]
    public async Task 整份取代約束_改權重_改範圍_停用硬約束()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var replacement = DefaultConstraints.Settings
            .With(DefaultConstraints.S7FairnessPoint, c => c with { Weight = 25, Enabled = true })
            .With(DefaultConstraints.H7NpMaxConsecutive, c => c with { Enabled = false })
            .With(DefaultConstraints.S6NpAvoidHoliday, c => c with
            {
                Scope = ConstraintScope.ForRanks(DefaultRanks.NP, DefaultRanks.PTR).OnDayKinds(DayKind.Holiday, DayKind.PublicHoliday),
            });

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceConstraintsAsync(replacement);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var read = await settings.GetConstraintsAsync();
            DomainEquality.AssertEqual(replacement, read);

            // 範圍值的葉子表沒有殘留舊值
            var context = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
            var s6Scope = await context.ConstraintScopeEntries
                .Where(s => s.ConstraintCode == DefaultConstraints.S6NpAvoidHoliday)
                .CountAsync();
            Assert.Equal(4, s6Scope);
        }
    }

    [Fact]
    public async Task 整份取代資格矩陣與點數規則()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var matrix = new EligibilityMatrix(new Dictionary<string, IReadOnlyDictionary<string, bool>>
        {
            [DefaultRanks.R1] = new Dictionary<string, bool> { [DefaultAreas.Ward] = true, [DefaultAreas.Icu] = true, [DefaultAreas.Chief] = false },
            [DefaultRanks.NP] = new Dictionary<string, bool> { [DefaultAreas.Ward] = true, [DefaultAreas.Icu] = false, [DefaultAreas.Chief] = false },
        });
        var rules = DefaultPointRules.Rules with
        {
            Quota = new QuotaPointRule(Weekday: 1, Holiday: 3),
            Fairness = DefaultPointRules.Rules.Fairness with { ConsecutiveSaturdayBonus = new ConsecutiveSaturdayBonus(2, 14) },
        };

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceEligibilityAsync(matrix);
            await settings.ReplacePointRulesAsync(rules);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            DomainEquality.AssertEqual(matrix, await settings.GetEligibilityAsync());
            DomainEquality.AssertEqual(rules, await settings.GetPointRulesAsync());
        }
    }

    [Fact]
    public async Task 逐月覆寫_沒設過回空_設過整份取代()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var oct = new YearMonth(2026, 10);

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var empty = await settings.GetMonthlyOverrideAsync(oct);
            Assert.Equal(oct, empty.YearMonth);
            Assert.Empty(empty.QuotaCapByRank);

            await settings.ReplaceMonthlyOverrideAsync(new MonthlyOverride(oct, new Dictionary<string, int> { ["R6"] = 4, ["R5"] = 6 }));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceMonthlyOverrideAsync(new MonthlyOverride(oct, new Dictionary<string, int> { ["R6"] = 5 }));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var read = await settings.GetMonthlyOverrideAsync(oct);
            Assert.Equal(new Dictionary<string, int> { ["R6"] = 5 }, read.QuotaCapByRank);
            Assert.Empty((await settings.GetMonthlyOverrideAsync(new YearMonth(2026, 11))).QuotaCapByRank);
        }
    }
}
