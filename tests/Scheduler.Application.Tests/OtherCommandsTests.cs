using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.Errors;
using Scheduler.Application.People;
using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

public class BlockedDayCommandsTests
{
    private static readonly YearMonth Oct = new(2026, 10);

    private static BlockedDayCommands CommandsOf(InMemoryStore store) => new(store, store, store);

    [Fact]
    public async Task 登記_冪等_統計正確()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithStaff("s2", DefaultRanks.R3)
            .WithBlockedDay("s2", new DateOnly(2026, 10, 5));
        var commands = CommandsOf(store);

        var first = await commands.SetAsync(Oct, "s1", new DateOnly(2026, 10, 5));
        var second = await commands.SetAsync(Oct, "s1", new DateOnly(2026, 10, 5));

        Assert.Equal(new BlockedDayMutation(1, 15, 2), first);
        Assert.Equal(first, second);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task 超過每人每月上限_BLOCKED_DAY_CAP_EXCEEDED()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        for (var day = 1; day <= DefaultPointRules.BlockedDayMonthlyCap; day++)
        {
            store.WithBlockedDay("s1", new DateOnly(2026, 10, day));
        }

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).SetAsync(Oct, "s1", new DateOnly(2026, 10, 20)));

        Assert.Equal(ErrorCode.BlockedDayCapExceeded, ex.Code);
    }

    [Fact]
    public async Task 清除_未登記也冪等()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithBlockedDay("s1", new DateOnly(2026, 10, 5));
        var commands = CommandsOf(store);

        var first = await commands.ClearAsync(Oct, "s1", new DateOnly(2026, 10, 5));
        var second = await commands.ClearAsync(Oct, "s1", new DateOnly(2026, 10, 5));

        Assert.Equal(new BlockedDayMutation(0, 16, 0), first);
        Assert.Equal(first, second);
        Assert.Empty(store.BlockedDays);
    }

    [Fact]
    public async Task 人員不存在_404_日期不在該月_422()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        var commands = CommandsOf(store);

        var notFound = await Assert.ThrowsAsync<SchedulerException>(() => commands.SetAsync(Oct, "s-nope", new DateOnly(2026, 10, 5)));
        var invalid = await Assert.ThrowsAsync<SchedulerException>(() => commands.SetAsync(Oct, "s1", new DateOnly(2026, 11, 5)));

        Assert.Equal(ErrorCode.NotFound, notFound.Code);
        Assert.Equal(ErrorCode.InvalidRequest, invalid.Code);
    }
}

public class StaffCommandsTests
{
    private static StaffCommands CommandsOf(InMemoryStore store) => new(store, store, store, store, store);

    [Fact]
    public async Task 新增_可值類型由資格矩陣推導_員編重複_409()
    {
        var store = new InMemoryStore();
        var commands = CommandsOf(store);

        var created = await commands.CreateAsync(new StaffWrite("E100", "王小明", DefaultRanks.R2));

        Assert.StartsWith("s-", created.Id);
        Assert.Equal(StaffStatus.Active, created.Status);
        Assert.Equal(new[] { DefaultAreas.Ward, DefaultAreas.Icu }, created.EligibleAreaTypes);
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.CreateAsync(new StaffWrite("E100", "另一人", DefaultRanks.R3)));
        Assert.Equal(ErrorCode.EmployeeNoTaken, ex.Code);
    }

    [Fact]
    public async Task 身分不存在_422()
    {
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(new InMemoryStore()).CreateAsync(new StaffWrite("E1", "x", "R9")));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task 編輯_保留自己的員編不算重複_換身分重算可值類型()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        var employeeNo = store.Staff[0].EmployeeNo;

        var updated = await CommandsOf(store).UpdateAsync("s1", new StaffWrite(employeeNo, "改名", DefaultRanks.R4));

        Assert.Equal("改名", updated.Name);
        Assert.Contains(DefaultAreas.Chief, updated.EligibleAreaTypes);
        Assert.Equal(DefaultRanks.R4, store.Staff[0].RankCode);
    }

    [Fact]
    public async Task 刪除_有值班紀錄_409_否則級聯清掉不可排班日()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithStaff("s2", DefaultRanks.R2)
            .WithDraft(new YearMonth(2026, 10))
            .WithDuty("area-icu", new DateOnly(2026, 10, 5), "s1")
            .WithBlockedDay("s2", new DateOnly(2026, 10, 5))
            .WithBlockedDay("s2", new DateOnly(2026, 11, 5))
            .WithBlockedDay("s1", new DateOnly(2026, 11, 6));
        var commands = CommandsOf(store);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.DeleteAsync("s1"));
        Assert.Equal(ErrorCode.StaffHasDuties, ex.Code);

        await commands.DeleteAsync("s2");

        Assert.Single(store.Staff);
        var remaining = Assert.Single(store.BlockedDays);
        Assert.Equal("s1", remaining.StaffId);
    }

    [Fact]
    public async Task 狀態變更_不存在_404()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        var commands = CommandsOf(store);

        var view = await commands.SetStatusAsync("s1", StaffStatus.Inactive);
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.SetStatusAsync("s-nope", StaffStatus.Active));

        Assert.Equal(StaffStatus.Inactive, view.Status);
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }
}

public class CalendarCommandsTests
{
    private static CalendarCommands CommandsOf(InMemoryStore store) => new(store, store, store);

    [Fact]
    public async Task 純週末的一天_設成補班日_視為平日_點數_1()
    {
        var store = new InMemoryStore();
        var saturday = new DateOnly(2026, 10, 10);

        var view = await CommandsOf(store).OverrideDayAsync(saturday, new CalendarDayPatch(IsMakeUpWorkday: true));

        Assert.False(view.Day.IsHoliday);
        Assert.True(view.Day.IsMakeUpWorkday);
        Assert.True(view.Overridden);
        Assert.Equal(1, view.QuotaPointValue);
        Assert.True(store.CalendarExceptions[saturday].Overridden);
    }

    [Fact]
    public async Task 沒送的欄位維持原值_holidayName_送_null_才清掉()
    {
        var day = new DateOnly(2026, 9, 28);
        var store = new InMemoryStore().WithHoliday(day, "教師節");
        var commands = CommandsOf(store);

        var kept = await commands.OverrideDayAsync(day, new CalendarDayPatch(IsPublicHoliday: false));
        Assert.Equal("教師節", kept.Day.HolidayName);
        Assert.True(kept.Day.IsHoliday, "沒動 isHoliday 就維持");

        var cleared = await commands.OverrideDayAsync(day, new CalendarDayPatch(HolidayNameProvided: true, HolidayName: null));
        Assert.Null(cleared.Day.HolidayName);
    }

    [Fact]
    public async Task 補班日又是假日_或國定假日卻不是假日_422()
    {
        var commands = CommandsOf(new InMemoryStore());
        var day = new DateOnly(2026, 10, 6);

        var a = await Assert.ThrowsAsync<SchedulerException>(() => commands.OverrideDayAsync(day, new CalendarDayPatch(IsHoliday: true, IsMakeUpWorkday: true)));
        var b = await Assert.ThrowsAsync<SchedulerException>(() => commands.OverrideDayAsync(day, new CalendarDayPatch(IsPublicHoliday: true)));

        Assert.Equal(ErrorCode.InvalidRequest, a.Code);
        Assert.Equal(ErrorCode.InvalidRequest, b.Code);
    }
}

public class SettingsCommandsTests
{
    private static SettingsCommands CommandsOf(InMemoryStore store) => new(store, store, store, store);

    [Fact]
    public async Task putAreas_每日需求人數目前只支援_1_給_2_是_422()
    {
        // duty 表主鍵一格一人、EnsureConsistent 對同格多筆擲出；放行 2 會讓求解器排兩人然後在重算指標時炸掉
        var store = new InMemoryStore();
        var settings = new AreaSettings(store.Areas.AreaTypes, store.Areas.Areas.Select(a => a.Id == "area-a" ? a with { RequiredPerDay = 2 } : a).ToArray());

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ReplaceAreasAsync(settings));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
        Assert.Equal(1, store.Areas.Areas.Single(a => a.Id == "area-a").RequiredPerDay);
    }

    [Fact]
    public async Task 刪掉仍被值班表引用的區域_AREA_IN_USE()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(new YearMonth(2026, 10))
            .WithDuty("area-icu", new DateOnly(2026, 10, 5), "s1");
        var without = new AreaSettings(DefaultAreas.AreaTypes, DefaultAreas.Areas.Where(a => a.Id != "area-icu").ToArray());

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ReplaceAreasAsync(without));

        Assert.Equal(ErrorCode.AreaInUse, ex.Code);
    }

    [Fact]
    public async Task 刪掉仍被區域引用的區域類型_AREA_TYPE_IN_USE_未知類型_422()
    {
        var store = new InMemoryStore();
        var commands = CommandsOf(store);
        var typeRemoved = new AreaSettings(DefaultAreas.AreaTypes.Where(t => t.Code != DefaultAreas.Icu).ToArray(), DefaultAreas.Areas);
        var unknownType = new AreaSettings(DefaultAreas.AreaTypes, new[] { new Area("area-x", "X", "X", "NOPE") });

        var inUse = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceAreasAsync(typeRemoved));
        var invalid = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceAreasAsync(unknownType));

        Assert.Equal(ErrorCode.AreaTypeInUse, inUse.Code);
        Assert.Equal(ErrorCode.InvalidRequest, invalid.Code);
    }

    [Fact]
    public async Task 刪掉仍被人員_資格矩陣或約束範圍引用的身分_RANK_IN_USE()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        var commands = CommandsOf(store);
        RankSettings Without(string code) => new(DefaultRanks.Groups, DefaultRanks.Ranks.Where(r => r.Code != code).ToArray());

        var byStaff = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceRanksAsync(Without(DefaultRanks.R2)));
        // NP 沒有人員，但資格矩陣有他那一列、S1 的 exemptRankCodes 也點名他
        var byMatrix = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceRanksAsync(Without(DefaultRanks.NP)));

        Assert.Equal(ErrorCode.RankInUse, byStaff.Code);
        Assert.Equal(ErrorCode.RankInUse, byMatrix.Code);
    }

    [Fact]
    public async Task 整份取代身分_新增一個身分成功()
    {
        var store = new InMemoryStore();
        var ranks = DefaultRanks.Ranks.Append(new Rank("R7", "R7", DefaultRanks.Senior, 4, PointType.B)).ToArray();

        var result = await CommandsOf(store).ReplaceRanksAsync(new RankSettings(DefaultRanks.Groups, ranks));

        Assert.Contains(result.Ranks, r => r.Code == "R7");
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task 約束_範圍空陣列正規化成不限_缺必要參數_422()
    {
        var store = new InMemoryStore();
        var commands = CommandsOf(store);
        var emptyScope = DefaultConstraints.Settings.With("H4_MIN_GAP", c => c with { Scope = new ConstraintScope(ExemptRankCodes: new HashSet<string>()) });

        var saved = await commands.ReplaceConstraintsAsync(emptyScope);
        Assert.Null(saved["H4_MIN_GAP"].Scope.ExemptRankCodes);

        var missingDays = DefaultConstraints.Settings.With("H4_MIN_GAP", c => c with { Params = ConstraintParams.None });
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceConstraintsAsync(missingDays));
        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task 點數規則_查表缺列_422()
    {
        var rules = DefaultPointRules.Rules;
        var broken = rules with
        {
            Fairness = rules.Fairness with
            {
                Tables = new Dictionary<PointType, IReadOnlyList<FairnessTableEntry>>
                {
                    [PointType.A] = rules.Fairness.Tables[PointType.A].Skip(1).ToArray(),
                    [PointType.B] = rules.Fairness.Tables[PointType.B],
                },
            },
        };

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(new InMemoryStore()).ReplacePointRulesAsync(broken));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task 逐月覆寫_路徑月份為準_未知身分_422()
    {
        var store = new InMemoryStore();
        var commands = CommandsOf(store);
        var oct = new YearMonth(2026, 10);

        var saved = await commands.ReplaceMonthlyOverrideAsync(oct, new Dictionary<string, int> { [DefaultRanks.R6] = 3 });
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.ReplaceMonthlyOverrideAsync(oct, new Dictionary<string, int> { ["R9"] = 3 }));

        Assert.Equal(oct, saved.YearMonth);
        Assert.Equal(3, saved.QuotaCapByRank[DefaultRanks.R6]);
        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }
}
