using System.Text;
using Scheduler.Application.Calendars.Sync;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

/// <summary>
/// 官方行事曆的解析、metadata 選檔、與同步用例（#112）。全部是假造的小檔與假的來源，不碰網路。
/// 格式照官方：<c>西元日期,星期,是否放假,備註</c>。
/// </summary>
public sealed class CalendarSyncTests
{
    private static readonly string[] WeekdayChars = { "日", "一", "二", "三", "四", "五", "六" };

    /// <summary>一整年的 CSV：週末放假（0 上班／2 放假），再蓋上 <paramref name="special"/>（日期 → 放假旗標與備註）。</summary>
    internal static string YearCsv(int year, Dictionary<DateOnly, (int Flag, string Remark)>? special = null)
    {
        var sb = new StringBuilder("西元日期,星期,是否放假,備註\r\n");
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var (flag, remark) = special is not null && special.TryGetValue(d, out var s) ? s : (weekend ? 2 : 0, "");
            sb.Append($"{d:yyyyMMdd},{WeekdayChars[(int)d.DayOfWeek]},{flag},{remark}\r\n");
        }

        return sb.ToString();
    }

    private static Dictionary<DateOnly, (int, string)> Holidays2028() => new()
    {
        [new DateOnly(2028, 1, 1)] = (2, "開國紀念日"),      // 週六：國定假日落在週末
        [new DateOnly(2028, 1, 3)] = (2, "補假"),            // 週一
        [new DateOnly(2028, 2, 5)] = (0, "補行上班"),        // 週六補班
    };

    // ---- 解碼 ----

    [Fact]
    public void Decode_Utf8WithBom()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(YearCsv(2028, Holidays2028()))).ToArray();

        var days = OfficialCalendar.ParseCsv(OfficialCalendar.Decode(bytes), 2028);

        Assert.Equal(366, days.Count);
        Assert.Equal("開國紀念日", days[0].Remark);
    }

    [Fact]
    public void Decode_Big5FallsBackWhenNotUtf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding("big5").GetBytes(YearCsv(2028, Holidays2028()));

        var days = OfficialCalendar.ParseCsv(OfficialCalendar.Decode(bytes), 2028);

        Assert.Equal("開國紀念日", days[0].Remark);
        Assert.Equal("補行上班", days.Single(d => d.Date == new DateOnly(2028, 2, 5)).Remark);
    }

    // ---- 解析與驗證 ----

    [Fact]
    public void ToExceptions_MapsHolidaysMakeUpDaysAndPlainDays()
    {
        var exceptions = OfficialCalendar.ToExceptions(OfficialCalendar.ParseCsv(YearCsv(2028, Holidays2028()), 2028));

        // 只有三天是例外；一般週末（沒有備註）與平日都不是
        Assert.Equal(3, exceptions.Count);
        var nationalDayOnWeekend = exceptions[new DateOnly(2028, 1, 1)];
        Assert.True(nationalDayOnWeekend.IsHoliday);
        Assert.True(nationalDayOnWeekend.IsPublicHoliday);
        Assert.Equal("開國紀念日", nationalDayOnWeekend.HolidayName);
        Assert.True(exceptions[new DateOnly(2028, 1, 3)].IsPublicHoliday);
        var makeUp = exceptions[new DateOnly(2028, 2, 5)];
        Assert.True(makeUp.IsMakeUpWorkday);
        Assert.False(makeUp.IsHoliday);
        Assert.False(makeUp.IsPublicHoliday);
    }

    [Fact]
    public void ParseCsv_LeapYearNeeds366Rows()
    {
        Assert.Equal(366, OfficialCalendar.ParseCsv(YearCsv(2028), 2028).Count);
        Assert.Equal(365, OfficialCalendar.ParseCsv(YearCsv(2027), 2027).Count);
    }

    [Fact]
    public void ParseCsv_RejectsMissingRow()
    {
        var lines = YearCsv(2027).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        lines.RemoveAt(100);

        var ex = Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseCsv(string.Join("\r\n", lines), 2027));
        Assert.Equal(CalendarSyncFailure.Failed, ex.Kind);
    }

    [Fact]
    public void ParseCsv_RejectsNonConsecutiveDates()
    {
        // 列數對，但把某一天換成隔天（出現重複日期）
        var lines = YearCsv(2027).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        lines[50] = lines[51];

        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseCsv(string.Join("\r\n", lines), 2027));
    }

    [Fact]
    public void ParseCsv_RejectsWrongWeekday()
    {
        var csv = YearCsv(2027).Replace("20270104,一,", "20270104,二,");

        var ex = Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseCsv(csv, 2027));
        Assert.Contains("星期", ex.Message);
    }

    [Fact]
    public void ParseCsv_RejectsWrongYearAndBadFlag()
    {
        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseCsv(YearCsv(2027), 2028));
        Assert.Throws<CalendarSyncException>(() =>
            OfficialCalendar.ParseCsv(YearCsv(2027, new() { [new DateOnly(2027, 3, 3)] = (1, "") }), 2027));
        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseCsv("not a csv", 2027));
    }

    [Fact]
    public void ParseMirrorJson_ValidAndInvalid()
    {
        var json = MirrorJson(2027);
        var days = OfficialCalendar.ParseMirrorJson(json, 2027);

        Assert.Equal(365, days.Count);
        Assert.True(days[0].IsOffDay);
        Assert.Equal("開國紀念日", days[0].Remark);

        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseMirrorJson("[{\"date\":\"20270101\"}]", 2027));
        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseMirrorJson("{}", 2027));
        Assert.Throws<CalendarSyncException>(() => OfficialCalendar.ParseMirrorJson("<html>", 2027));
    }

    internal static string MirrorJson(int year)
    {
        var items = new List<string>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var first = d.Month == 1 && d.Day == 1;
            items.Add($"{{\"date\":\"{d:yyyyMMdd}\",\"week\":\"{WeekdayChars[(int)d.DayOfWeek]}\",\"isHoliday\":{(weekend || first ? "true" : "false")},\"description\":\"{(first ? "開國紀念日" : "")}\"}}");
        }

        return "[" + string.Join(",", items) + "]";
    }

    // ---- metadata 選檔 ----

    private static string Metadata(params (string Description, string Url)[] items) =>
        "{\"result\":{\"distribution\":[" +
        string.Join(",", items.Select(i => $"{{\"resourceDescription\":\"{i.Description}\",\"resourceDownloadUrl\":\"{i.Url}\"}}")) +
        "]}}";

    [Fact]
    public void PickDownloadUrl_ExcludesGoogleVersion()
    {
        var json = Metadata(
            ("115年中華民國政府行政機關辦公日曆表", "https://x/115.csv"),
            ("116年中華民國政府行政機關辦公日曆表_Google行事曆專用", "https://x/116-google.csv"),
            ("116年中華民國政府行政機關辦公日曆表", "https://x/116.csv"));

        Assert.Equal("https://x/116.csv", OfficialDatasetMetadata.PickDownloadUrl(json, 2027));
        Assert.Equal("https://x/115.csv", OfficialDatasetMetadata.PickDownloadUrl(json, 2026));
    }

    [Fact]
    public void PickDownloadUrl_PicksLatestWhenSeveralForTheSameYear()
    {
        var json = Metadata(
            ("114年中華民國政府行政機關辦公日曆表", "https://x/114-old.csv"),
            ("114年中華民國政府行政機關辦公日曆表(1141020更新)", "https://x/114-1020.csv"),
            ("114年中華民國政府行政機關辦公日曆表(1140801更新)", "https://x/114-0801.csv"),
            ("114年中華民國政府行政機關辦公日曆表_Google行事曆專用(修正)", "https://x/114-google.csv"));

        Assert.Equal("https://x/114-1020.csv", OfficialDatasetMetadata.PickDownloadUrl(json, 2025));
    }

    [Fact]
    public void PickDownloadUrl_NullWhenNotPublished_ThrowsWhenMalformed()
    {
        var json = Metadata(("116年中華民國政府行政機關辦公日曆表", "https://x/116.csv"));

        Assert.Null(OfficialDatasetMetadata.PickDownloadUrl(json, 2028));
        Assert.Throws<CalendarSyncException>(() => OfficialDatasetMetadata.PickDownloadUrl("not json", 2028));
        Assert.Throws<CalendarSyncException>(() => OfficialDatasetMetadata.PickDownloadUrl("{}", 2028));
    }

    // ---- 同步用例 ----

    private sealed class FakeSource : ICalendarSource
    {
        private readonly Func<int, IReadOnlyList<OfficialDay>> _fetch;

        public FakeSource(string name, bool fallback, Func<int, IReadOnlyList<OfficialDay>> fetch)
        {
            Name = name;
            IsFallback = fallback;
            _fetch = fetch;
        }

        public string Name { get; }
        public bool IsFallback { get; }
        public List<int> Calls { get; } = new();

        public Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken)
        {
            Calls.Add(year);
            return Task.FromResult(_fetch(year));
        }
    }

    private sealed class ListLog : ICalendarSyncLog
    {
        public List<string> Lines { get; } = new();
        public void Write(string message) => Lines.Add(message);
    }

    private sealed class MemoryState : ICalendarSyncStateRepository
    {
        public CalendarSyncState State { get; private set; } = CalendarSyncState.Empty;
        public Task<CalendarSyncState> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task SaveAsync(CalendarSyncState state, CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 10, 6, 1, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static IReadOnlyList<OfficialDay> Official(int year, Dictionary<DateOnly, (int, string)>? special = null) =>
        OfficialCalendar.ParseCsv(YearCsv(year, special), year);

    private static (CalendarSyncService Service, InMemoryStore Store, MemoryState State, ListLog Log) Build(
        InMemoryStore? store = null, params ICalendarSource[] sources)
    {
        store ??= new InMemoryStore();
        var state = new MemoryState();
        var log = new ListLog();
        return (new CalendarSyncService(store, state, store, sources, log, new FixedTime()), store, state, log);
    }

    [Fact]
    public async Task Run_FetchesThisYearAndNextYear_WritesExceptionsAndRecordsState()
    {
        var source = new FakeSource("official", false, y => Official(y, y == 2028 ? Holidays2028() : null));
        var (service, store, state, _) = Build(sources: source);

        var result = await service.RunAsync();

        Assert.Equal(new[] { 2027, 2028 }, source.Calls);
        Assert.Equal(new[] { 2028 }, result.UpdatedYears);
        Assert.Equal(YearSyncOutcome.Unchanged, result.Years[2027]);
        Assert.Equal(3, store.CalendarExceptions.Count);
        Assert.All(store.CalendarExceptions.Values, e => Assert.False(e.Overridden));
        Assert.Equal(new[] { 2027, 2028 }, state.State.Years.Select(y => y.Year));
        Assert.All(state.State.Years, y => Assert.Equal("official", y.Source));
        Assert.NotNull(state.State.LastSuccessAt);
        Assert.Null(state.State.LastError);
    }

    [Fact]
    public async Task Run_RefetchesYearsThatAlreadyHaveData()
    {
        var store = new InMemoryStore();
        store.CalendarExceptions[new DateOnly(2026, 5, 1)] = Exception(new DateOnly(2026, 5, 1), "勞動節");
        var source = new FakeSource("official", false, y => Official(y));
        var (service, _, _, _) = Build(store, source);

        await service.RunAsync();

        Assert.Equal(new[] { 2026, 2027, 2028 }, source.Calls);
    }

    private static CalendarException Exception(DateOnly date, string name, bool overridden = false) =>
        new(new CalendarDay(date, IsHoliday: true, IsPublicHoliday: true, IsMakeUpWorkday: false, HolidayName: name), overridden);

    [Fact]
    public async Task Run_NeverTouchesOverriddenRows()
    {
        var store = new InMemoryStore();
        // 使用者把官方的補班日改成放假、把官方的國定假日取消：兩者都不能被蓋回去
        var userHoliday = new DateOnly(2028, 2, 5);
        var userCancelled = new DateOnly(2028, 1, 3);
        store.CalendarExceptions[userHoliday] = new CalendarException(
            new CalendarDay(userHoliday, true, false, false, "臨時放假"), Overridden: true);
        store.CalendarExceptions[userCancelled] = new CalendarException(
            new CalendarDay(userCancelled, false, false, false, null), Overridden: true);
        var source = new FakeSource("official", false, y => Official(y, y == 2028 ? Holidays2028() : null));
        var (service, _, _, _) = Build(store, source);

        await service.RunAsync();

        Assert.Equal("臨時放假", store.CalendarExceptions[userHoliday].Day.HolidayName);
        Assert.True(store.CalendarExceptions[userHoliday].Overridden);
        Assert.False(store.CalendarExceptions[userCancelled].Day.IsHoliday);
        Assert.True(store.CalendarExceptions[userCancelled].Overridden);
        Assert.True(store.CalendarExceptions.ContainsKey(new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public async Task Run_NoDifferenceMeansNoWriteAndKeepsBuiltInNames()
    {
        var store = new InMemoryStore();
        // 內建名稱比官方備註具體；結構（假日、國定假日、補班日）一致就不動它
        store.CalendarExceptions[new DateOnly(2028, 1, 3)] = Exception(new DateOnly(2028, 1, 3), "元旦補假");
        store.CalendarExceptions[new DateOnly(2028, 1, 1)] = Exception(new DateOnly(2028, 1, 1), "元旦");
        store.CalendarExceptions[new DateOnly(2028, 2, 5)] = new CalendarException(
            new CalendarDay(new DateOnly(2028, 2, 5), false, false, true, null), Overridden: false);
        var source = new FakeSource("official", false, y => Official(y, y == 2028 ? Holidays2028() : null));
        var (service, _, state, _) = Build(store, source);
        var commitsBefore = store.Commits;

        var result = await service.RunAsync();

        Assert.Empty(result.UpdatedYears);
        Assert.Equal("元旦補假", store.CalendarExceptions[new DateOnly(2028, 1, 3)].Day.HolidayName);
        // 只有最後存狀態的那一次 commit，沒有行事曆寫入
        Assert.Equal(commitsBefore + 1, store.Commits);
        Assert.Contains(state.State.Years, y => y.Year == 2028);
    }

    [Fact]
    public async Task Run_ChangesOnlyTheDaysThatDiffer_AndRemovesStaleBuiltIns()
    {
        var store = new InMemoryStore();
        var kept = new DateOnly(2028, 1, 1);
        var stale = new DateOnly(2028, 6, 6);
        store.CalendarExceptions[kept] = Exception(kept, "元旦");
        store.CalendarExceptions[stale] = Exception(stale, "已被官方取消的假日");
        var source = new FakeSource("official", false, y => Official(y, y == 2028 ? Holidays2028() : null));
        var (service, _, _, _) = Build(store, source);

        var result = await service.RunAsync();

        Assert.Equal(new[] { 2028 }, result.UpdatedYears);
        Assert.Equal("元旦", store.CalendarExceptions[kept].Day.HolidayName);
        Assert.False(store.CalendarExceptions.ContainsKey(stale));
        Assert.True(store.CalendarExceptions.ContainsKey(new DateOnly(2028, 1, 3)));
        Assert.True(store.CalendarExceptions[new DateOnly(2028, 2, 5)].Day.IsMakeUpWorkday);
    }

    [Fact]
    public async Task Run_PrimaryFailureFallsBackToMirror_AndRecordsMirrorAsSource()
    {
        var primary = new FakeSource("official", false, _ => throw new HttpRequestException("連不上"));
        var mirror = new FakeSource("mirror", true, y => Official(y, y == 2028 ? Holidays2028() : null));
        var (service, store, state, log) = Build(sources: new ICalendarSource[] { primary, mirror });

        var result = await service.RunAsync();

        Assert.Equal(new[] { 2028 }, result.UpdatedYears);
        Assert.Equal(3, store.CalendarExceptions.Count);
        Assert.All(state.State.Years, y => Assert.Equal("mirror", y.Source));
        Assert.Contains(log.Lines, l => l.Contains("official") && l.Contains("連不上"));
    }

    [Fact]
    public async Task Run_PrimaryNotPublishedDoesNotUseMirror()
    {
        var primary = new FakeSource("official", false, y => y == 2027
            ? Official(y)
            : throw new CalendarSyncException(CalendarSyncFailure.NotPublished, "尚未公告"));
        var mirror = new FakeSource("mirror", true, y => Official(y, Holidays2028()));
        var (service, store, state, _) = Build(sources: new ICalendarSource[] { primary, mirror });

        var result = await service.RunAsync();

        Assert.Equal(YearSyncOutcome.NotPublished, result.Years[2028]);
        Assert.Empty(mirror.Calls);
        Assert.Empty(store.CalendarExceptions);
        Assert.Equal(new[] { 2027 }, state.State.Years.Select(y => y.Year));
        Assert.Null(state.State.LastError); // 尚未公告不是故障
    }

    [Fact]
    public async Task Run_BothSourcesFail_LogsAndDoesNotThrowOrWrite()
    {
        var primary = new FakeSource("official", false, _ => throw new HttpRequestException("逾時"));
        var mirror = new FakeSource("mirror", true, _ => throw new CalendarSyncException(CalendarSyncFailure.Failed, "格式不符"));
        var (service, store, state, log) = Build(sources: new ICalendarSource[] { primary, mirror });

        var result = await service.RunAsync();

        Assert.All(result.Years.Values, o => Assert.Equal(YearSyncOutcome.Failed, o));
        Assert.Empty(store.CalendarExceptions);
        Assert.Empty(state.State.Years);
        Assert.Null(state.State.LastSuccessAt);
        Assert.NotNull(state.State.LastError);
        Assert.Contains(log.Lines, l => l.Contains("逾時"));
        Assert.Contains(log.Lines, l => l.Contains("格式不符"));
    }

    [Fact]
    public async Task Run_BadYearIsRejectedWholesale_OtherYearStillWritten()
    {
        // 2028 的資料缺一列（來源丟驗證失敗）：2028 整份不寫；2027 照常
        var source = new FakeSource("official", false, y => y == 2028
            ? OfficialCalendar.ParseCsv(YearCsv(2028, Holidays2028()).Replace("20280104,二,0,\r\n", ""), 2028)
            : Official(y, new() { [new DateOnly(2027, 2, 10)] = (2, "春節") }));
        var (service, store, _, log) = Build(sources: source);

        // ParseCsv 在 fake 內丟出，等同真來源的行為
        var result = await service.RunAsync();

        Assert.Equal(YearSyncOutcome.Updated, result.Years[2027]);
        Assert.Equal(YearSyncOutcome.Failed, result.Years[2028]);
        Assert.All(store.CalendarExceptions.Keys, d => Assert.Equal(2027, d.Year));
        Assert.Contains(log.Lines, l => l.Contains("2028"));
    }
}
