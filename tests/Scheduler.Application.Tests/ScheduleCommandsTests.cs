using Scheduler.Application.Errors;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

public class ScheduleCommandsTests
{
    private static readonly YearMonth Sep = new(2026, 9);
    private static readonly YearMonth Oct = new(2026, 10);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(8));

    private static ScheduleCommands CommandsOf(InMemoryStore store) =>
        new(store, store, store.Loader, store, new FixedClock(Now));

    private static DateOnly D(int day) => new(2026, 10, day);

    [Fact]
    public async Task 該月沒有值班表_setDuty_自動建草稿_revision_從_1_起()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);

        var result = await CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-icu", D(5)), "s1");

        Assert.Equal(1, result.Revision);
        var header = store.Headers[Oct];
        Assert.Equal(ScheduleStatus.Draft, header.Status);
        Assert.Equal(1, header.Revision);
        var cell = Assert.Single(result.Cells);
        Assert.Equal("s1", cell.StaffId);
        Assert.Equal("area:area-icu:2026-10-05", cell.CellKey);
        Assert.Single(store.Duties);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task 清空一格_回應仍帶那一格且_staffId_為_null()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct).WithDuty("area-icu", D(5), "s1");

        var result = await CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-icu", D(5)), null);

        var cell = Assert.Single(result.Cells);
        Assert.Null(cell.StaffId);
        Assert.Empty(store.Duties);
    }

    [Fact]
    public async Task 打破硬約束不拒絕_回全量違規()
    {
        // s1 連值兩天違反 H4 值休休（MinGap），仍寫入
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct).WithDuty("area-icu", D(5), "s1");

        var result = await CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-icu", D(6)), "s1");

        Assert.Equal(2, store.Duties.Count);
        Assert.Contains(result.Violations, v => v.Code == "H4_MIN_GAP");
    }

    [Fact]
    public async Task 同一人同一天已在另一區_STAFF_ALREADY_ON_DUTY_帶_areaId()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct).WithDuty("area-icu", D(5), "s1");

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-a", D(5)), "s1"));

        Assert.Equal(ErrorCode.StaffAlreadyOnDuty, ex.Code);
        Assert.Equal("area-icu", ex.Details!["areaId"]);
        Assert.Single(store.Duties);
    }

    [Fact]
    public async Task 同一人放回他已在的那一格是冪等的()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct).WithDuty("area-icu", D(5), "s1");

        var result = await CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-icu", D(5)), "s1");

        Assert.Equal("s1", Assert.Single(result.Cells).StaffId);
        Assert.Single(store.Duties);
    }

    [Theory]
    [InlineData("area-icu", "2026-11-01", "s1")]
    [InlineData("area-nope", "2026-10-05", "s1")]
    [InlineData("area-icu", "2026-10-05", "s-nope")]
    public async Task 日期不在本月_區域不存在_人員不存在_INVALID_REQUEST(string areaId, string date, string staffId)
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() =>
            CommandsOf(store).SetDutyAsync(Oct, new CellRef(areaId, DateOnly.Parse(date)), staffId));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
        Assert.False(store.Headers.ContainsKey(Oct), "驗證失敗不該留下空草稿");
    }

    [Fact]
    public async Task 已發布的值班表也可以改_revision_遞增_狀態不變()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithPublished(Oct);

        var result = await CommandsOf(store).SetDutyAsync(Oct, new CellRef("area-icu", D(5)), "s1");

        Assert.Equal(2, result.Revision);
        Assert.Equal(ScheduleStatus.Published, store.Headers[Oct].Status);
    }

    [Fact]
    public async Task swap_對調兩格_含一格空的()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithStaff("s2", DefaultRanks.R3)
            .WithDraft(Oct)
            .WithDuty("area-icu", D(5), "s1");

        var result = await CommandsOf(store).SwapAsync(Oct, new CellRef("area-icu", D(5)), new CellRef("area-icu", D(12)));

        Assert.Equal(2, result.Cells.Count);
        Assert.Null(result.Cells[0].StaffId);
        Assert.Equal("s1", result.Cells[1].StaffId);
        var duty = Assert.Single(store.Duties);
        Assert.Equal(D(12), duty.Date);
    }

    [Fact]
    public async Task swap_同日兩區互換不算撞到自己()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithStaff("s2", DefaultRanks.R3)
            .WithDraft(Oct)
            .WithDuty("area-icu", D(5), "s1")
            .WithDuty("area-a", D(5), "s2");

        var result = await CommandsOf(store).SwapAsync(Oct, new CellRef("area-icu", D(5)), new CellRef("area-a", D(5)));

        Assert.Equal("s2", result.Cells[0].StaffId);
        Assert.Equal("s1", result.Cells[1].StaffId);
    }

    [Fact]
    public async Task swap_不同日對調後撞到同人同日另一區_409()
    {
        // s1 在 10/5 ICU 與 10/12 A；把 10/5 ICU 與 10/12 B 對調，s1 會落到 10/12 B，但他 10/12 已在 A
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(Oct)
            .WithDuty("area-icu", D(5), "s1")
            .WithDuty("area-a", D(12), "s1");

        var ex = await Assert.ThrowsAsync<SchedulerException>(() =>
            CommandsOf(store).SwapAsync(Oct, new CellRef("area-icu", D(5)), new CellRef("area-b", D(12))));

        Assert.Equal(ErrorCode.StaffAlreadyOnDuty, ex.Code);
        Assert.Equal("area-a", ex.Details!["areaId"]);
    }

    [Fact]
    public async Task swap_該月沒有值班表_NOT_FOUND_不憑空建表()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() =>
            CommandsOf(store).SwapAsync(Oct, new CellRef("area-icu", D(5)), new CellRef("area-icu", D(12))));

        Assert.Equal(ErrorCode.NotFound, ex.Code);
        Assert.Empty(store.Headers);
    }

    [Fact]
    public async Task publish_有硬違規未確認_409_確認後發布並結算月結轉()
    {
        // s-r4 連值 10/5、10/6 → H4 硬違規；同組 s-r5 沒值 → s-r4 剩 6−2=4、s-r5 剩 5 → s-r4 差 1
        var store = new InMemoryStore()
            .WithStaff("s-r4", DefaultRanks.R4)
            .WithStaff("s-r5", DefaultRanks.R5)
            .WithDraft(Oct)
            .WithDuty("area-chief", D(5), "s-r4")
            .WithDuty("area-chief", D(6), "s-r4");
        var commands = CommandsOf(store);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => commands.PublishAsync(Oct, acknowledgeViolations: false));
        Assert.Equal(ErrorCode.HardViolationsPresent, ex.Code);
        Assert.Equal(ScheduleStatus.Draft, store.Headers[Oct].Status);

        var result = await commands.PublishAsync(Oct, acknowledgeViolations: true);

        Assert.Equal(ScheduleStatus.Published, result.Status);
        Assert.Equal(Now, result.PublishedAt);
        Assert.Equal(1, result.Revision);
        Assert.Equal(1, result.CarryOver.Single(e => e.StaffId == "s-r4").Points);
        Assert.Equal(0, result.CarryOver.Single(e => e.StaffId == "s-r5").Points);
        Assert.Equal(result.CarryOver, store.CarryOver[Oct]);
    }

    [Fact]
    public async Task publish_第一次凍結上月月結轉_重新發布不重拍但重算本月結算()
    {
        var store = new InMemoryStore()
            .WithStaff("s-r4", DefaultRanks.R4)
            .WithStaff("s-r5", DefaultRanks.R5)
            .WithPublished(Sep, new CarryOverEntry("s-r4", 2))
            .WithDraft(Oct);
        var commands = CommandsOf(store);

        // 空表有 H1 覆蓋違規，這裡只看月結轉，一律確認
        await commands.PublishAsync(Oct, acknowledgeViolations: true);
        Assert.Equal(new[] { new CarryOverEntry("s-r4", 2) }, store.CarryOverApplied[Oct]);

        // 上月事後改了月結轉；本月加了一格再發布：凍結的那份不動，本月的結算會變
        store.CarryOver[Sep] = new List<CarryOverEntry> { new("s-r4", 5) };
        await commands.SetDutyAsync(Oct, new CellRef("area-chief", D(5)), "s-r5");
        var again = await commands.PublishAsync(Oct, acknowledgeViolations: true);

        Assert.Equal(new[] { new CarryOverEntry("s-r4", 2) }, store.CarryOverApplied[Oct]);
        Assert.Equal(3, again.Revision);
        // s-r4 剩 6−0−2=4，s-r5 剩 5−1=4 → 平手
        Assert.All(again.CarryOver, e => Assert.Equal(0, e.Points));
    }

    [Fact]
    public async Task publish_該月沒有值班表_NOT_FOUND()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).PublishAsync(Oct, false));

        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    // ---- 套用變體 ----

    private static InMemoryStore WithVariant(InMemoryStore store, YearMonth month, string jobId = "job-1", string variantId = "v-a")
    {
        store.Jobs[jobId] = new Solving.SolverJobRecord(jobId, month, Solving.SolverJobStatus.Succeeded, 1, 15, Now, Now, Now, 1, null, Array.Empty<string>(), null, null);
        store.Variants.Add(new Solving.VariantRecord(jobId, variantId, "重視公平", new Dictionary<string, double>(),
            new Solving.VariantMetrics(0, 0, 0, 0, null), 0, 0,
            new[] { new Duty("area-icu", new DateOnly(month.Year, month.Month, 3), "s1"), new Duty("area-a", new DateOnly(month.Year, month.Month, 7), "s1") }));
        return store;
    }

    [Fact]
    public async Task applyVariant_整月格子全部換成變體的_沒有值班表時建草稿()
    {
        var store = WithVariant(new InMemoryStore().WithStaff("s1", DefaultRanks.R2), Oct);

        var view = await CommandsOf(store).ApplyVariantAsync(Oct, "job-1", "v-a");

        Assert.Equal(ScheduleStatus.Draft, view.Status);
        Assert.Equal(1, view.Revision);
        Assert.Equal(2, view.Duties.Count);
        Assert.Equal(2, store.Duties.Count);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task applyVariant_既有草稿的格子被整份覆蓋_revision_遞增()
    {
        var store = WithVariant(new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct).WithDuty("area-b", D(20), "s1"), Oct);
        store.Headers[Oct] = store.Headers[Oct] with { Revision = 4 };

        var view = await CommandsOf(store).ApplyVariantAsync(Oct, "job-1", "v-a");

        Assert.Equal(5, view.Revision);
        Assert.DoesNotContain(store.Duties, d => d.AreaId == "area-b");
        Assert.Equal(2, store.Duties.Count);
    }

    [Fact]
    public async Task applyVariant_已發布_409()
    {
        var store = WithVariant(new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithPublished(Oct), Oct);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ApplyVariantAsync(Oct, "job-1", "v-a"));

        Assert.Equal(ErrorCode.ScheduleAlreadyPublished, ex.Code);
        Assert.Empty(store.Duties);
    }

    [Fact]
    public async Task applyVariant_job_或變體不存在_404_月份不符_422()
    {
        var store = WithVariant(new InMemoryStore().WithStaff("s1", DefaultRanks.R2), Sep);

        var missingJob = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ApplyVariantAsync(Sep, "job-x", "v-a"));
        Assert.Equal(ErrorCode.NotFound, missingJob.Code);
        var missingVariant = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ApplyVariantAsync(Sep, "job-1", "v-z"));
        Assert.Equal(ErrorCode.NotFound, missingVariant.Code);
        var wrongMonth = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ApplyVariantAsync(Oct, "job-1", "v-a"));
        Assert.Equal(ErrorCode.InvalidRequest, wrongMonth.Code);
        Assert.Equal(0, store.Commits);
    }

    [Fact]
    public async Task applyVariant_變體裡的人員已被刪_422_不落盤()
    {
        // 變體是求解當下的快照；人員之後被刪（變體裡的值班不算「有值班紀錄」），套用時要重新驗，否則該月之後每個讀取都 500
        var store = WithVariant(new InMemoryStore(), Oct);

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => CommandsOf(store).ApplyVariantAsync(Oct, "job-1", "v-a"));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
        Assert.Contains("s1", ex.Message);
        Assert.Empty(store.Duties);
        Assert.Equal(0, store.Commits);
    }
}

/// <summary>發布時間戳要能斷言，所以時鐘固定。</summary>
internal sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
