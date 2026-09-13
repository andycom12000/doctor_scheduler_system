using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Defaults;
using Scheduler.Persistence.Seed;

namespace Scheduler.Persistence.Tests;

/// <summary>首次啟動的 seed 必須與 <c>Scheduler.Domain.Defaults</c>（即 docs/constraint-defaults.md）逐字相同。</summary>
public class SeedTests
{
    [Fact]
    public async Task 區域與身分從出廠值_seed_且順序不變()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var areas = await settings.GetAreasAsync();
        Assert.Equal(DefaultAreas.AreaTypes, areas.AreaTypes);
        Assert.Equal(DefaultAreas.Areas, areas.Areas);

        var ranks = await settings.GetRanksAsync();
        Assert.Equal(DefaultRanks.Groups, ranks.Groups);
        Assert.Equal(DefaultRanks.Ranks, ranks.Ranks);
    }

    [Fact]
    public async Task 資格矩陣_點數規則_約束從出廠值_seed()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        DomainEquality.AssertEqual(DefaultRanks.Eligibility, await settings.GetEligibilityAsync());
        DomainEquality.AssertEqual(DefaultPointRules.Rules, await settings.GetPointRulesAsync());
        DomainEquality.AssertEqual(DefaultConstraints.Settings, await settings.GetConstraintsAsync());
    }

    [Fact]
    public async Task 行事曆只_seed_2026_的例外日_含落在週末的國定假日()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();

        var all = await calendar.GetExceptionsAsync(new DateOnly(2000, 1, 1), new DateOnly(2100, 12, 31));
        Assert.Equal(BuiltInCalendar.Days.Count, all.Count);
        Assert.All(all, e => Assert.False(e.Overridden));
        Assert.All(all, e => Assert.Equal(2026, e.Day.Date.Year));

        // 2026-02-15 是週日也是小年夜：同時是假日與國定假日
        var lunarEve = Assert.Single(all, e => e.Day.Date == new DateOnly(2026, 2, 15));
        Assert.Equal(DayOfWeek.Sunday, lunarEve.Day.Weekday);
        Assert.True(lunarEve.Day.IsHoliday);
        Assert.True(lunarEve.Day.IsPublicHoliday);

        // 9/28 教師節是 2026 起新增的假日，行事曆權威來源已校正
        Assert.Contains(all, e => e.Day.Date == new DateOnly(2026, 9, 28) && e.Day.HolidayName == "教師節");

        // 2026 沒有補班日
        Assert.DoesNotContain(all, e => e.Day.IsMakeUpWorkday);
    }

    [Fact]
    public async Task 清空區域後重啟_不會把其他設定蓋回出廠值()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        var ranks = new Application.Settings.RankSettings(
            DefaultRanks.Groups,
            DefaultRanks.Ranks.Select(r => r.Code == DefaultRanks.R6 ? r with { QuotaCap = 4 } : r).ToArray());

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.ReplaceRanksAsync(ranks);
            await settings.ReplaceAreasAsync(new Application.Settings.AreaSettings(Array.Empty<Domain.Model.AreaType>(), Array.Empty<Domain.Model.Area>()));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await SchedulerDatabase.InitializeAsync(db.Services);

        using (var scope = db.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            Assert.Equal(DefaultAreas.Areas, (await settings.GetAreasAsync()).Areas); // 區域空了才補回
            Assert.Equal(4, (await settings.GetRanksAsync()).Ranks.Single(r => r.Code == DefaultRanks.R6).QuotaCap); // 身分沒被動

            // 人員名冊沒被清空過，判空閘門第二次啟動不會重種。
            var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
            Assert.Equal(34, (await staff.ListAsync()).Count);
        }
    }

    [Fact]
    public async Task 旗標關閉時不種參考名單_但行事曆與設定照常種()
    {
        await using var db = await SqliteDatabase.CreateAsync(seedReferenceRoster: false);
        using var scope = db.Scope();

        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        Assert.Empty(await staff.ListAsync());

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        Assert.Equal(DefaultAreas.Areas, (await settings.GetAreasAsync()).Areas);
        Assert.Equal(DefaultRanks.Ranks, (await settings.GetRanksAsync()).Ranks);

        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
        var all = await calendar.GetExceptionsAsync(new DateOnly(2000, 1, 1), new DateOnly(2100, 12, 31));
        Assert.Equal(BuiltInCalendar.Days.Count, all.Count);
    }

    [Fact]
    public async Task 人員名冊從參考名單_seed_34人_全在職_員編E001到E034()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();

        var roster = await staff.ListAsync();
        Assert.Equal(34, roster.Count);
        Assert.All(roster, s => Assert.Equal(Domain.Model.StaffStatus.Active, s.Status));

        // ListAsync 依員編遞增；出廠參考名單的員編就是 E001..E034。
        Assert.Equal(
            Enumerable.Range(1, 34).Select(n => $"E{n:D3}"),
            roster.Select(s => s.EmployeeNo));
    }

    [Fact]
    public async Task 人員名冊組成照參考人數表_各身分皆有資格()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var roster = await staff.ListAsync();
        var actualHeadcount = roster
            .GroupBy(s => s.RankCode)
            .ToDictionary(g => g.Key, g => g.Count());

        // 人數組成的唯一來源是 DefaultRanks.ReferenceHeadcount，不在這裡另外寫一份數字。
        Assert.Equal(
            DefaultRanks.ReferenceHeadcount.OrderBy(kv => kv.Key),
            actualHeadcount.OrderBy(kv => kv.Key));

        // eligibleAreaTypes 由資格矩陣推導、不儲存：每個出現在名單裡的身分都查得到資格、且至少能值一種區域類型。
        var eligibility = await settings.GetEligibilityAsync();
        foreach (var rankCode in actualHeadcount.Keys)
        {
            Assert.NotEmpty(eligibility.EligibleAreaTypes(rankCode));
        }
    }
}
