using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Api.CalendarSync;
using Scheduler.Application.Calendars.Sync;
using Scheduler.Persistence;

namespace Scheduler.Api.Tests;

/// <summary>
/// 行事曆自動更新（#112）的 HTTP 與背景工作層。**不連外網**：網路一律是假的 <see cref="HttpMessageHandler"/>，
/// 背景工作的來源在 <c>ConfigureServices</c> 換成假的 <see cref="ICalendarSource"/>。
/// </summary>
public sealed class CalendarSyncTests
{
    // ---- 假網路 ----

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Bytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Bytes(Encoding.UTF8.GetBytes(body), status);

    private static string YearCsv(int year, params (DateOnly Date, int Flag, string Remark)[] special)
    {
        string[] w = { "日", "一", "二", "三", "四", "五", "六" };
        var map = special.ToDictionary(s => s.Date);
        var sb = new StringBuilder("西元日期,星期,是否放假,備註\r\n");
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var (flag, remark) = map.TryGetValue(d, out var s) ? (s.Flag, s.Remark) : (weekend ? 2 : 0, "");
            sb.Append($"{d:yyyyMMdd},{w[(int)d.DayOfWeek]},{flag},{remark}\r\n");
        }

        return sb.ToString();
    }

    private static string Metadata(string description, string url) =>
        $"{{\"result\":{{\"distribution\":[{{\"resourceDescription\":\"{description}\",\"resourceDownloadUrl\":\"{url}\"}}]}}}}";

    // ---- 來源 ----

    [Fact]
    public async Task 主來源_先查metadata再下載_UTF8含BOM()
    {
        var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(YearCsv(2028, (new DateOnly(2028, 1, 1), 2, "開國紀念日")))).ToArray();
        var handler = new FakeHandler(r => r.RequestUri!.Host switch
        {
            "data.gov.tw" => Text(Metadata("117年中華民國政府行政機關辦公日曆表", "https://files.test/uuid-117.csv")),
            "files.test" => Bytes(csv),
            _ => Text("", HttpStatusCode.NotFound),
        });
        var source = new OfficialCalendarSource(CalendarHttp.CreateClient(handler));

        var days = await source.FetchYearAsync(2028, CancellationToken.None);

        Assert.Equal(366, days.Count);
        Assert.Equal(new[] { OfficialCalendarSource.MetadataUrl, "https://files.test/uuid-117.csv" }, handler.Requests);
    }

    [Fact]
    public async Task 主來源_Big5也能解()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = Encoding.GetEncoding("big5").GetBytes(YearCsv(2028, (new DateOnly(2028, 1, 1), 2, "開國紀念日")));
        var handler = new FakeHandler(r => r.RequestUri!.Host == "data.gov.tw"
            ? Text(Metadata("117年中華民國政府行政機關辦公日曆表", "https://files.test/b5.csv"))
            : Bytes(csv));

        var days = await new OfficialCalendarSource(CalendarHttp.CreateClient(handler)).FetchYearAsync(2028, CancellationToken.None);

        Assert.Equal("開國紀念日", days[0].Remark);
    }

    [Fact]
    public async Task 主來源_metadata沒有該年_是尚未公告_不是故障()
    {
        var handler = new FakeHandler(_ => Text(Metadata("116年中華民國政府行政機關辦公日曆表", "https://files.test/116.csv")));

        var ex = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new OfficialCalendarSource(CalendarHttp.CreateClient(handler)).FetchYearAsync(2028, CancellationToken.None));

        Assert.Equal(CalendarSyncFailure.NotPublished, ex.Kind);
    }

    [Fact]
    public async Task 主來源_HTTP錯誤與壞檔_是故障()
    {
        var down = new FakeHandler(_ => Text("", HttpStatusCode.ServiceUnavailable));
        var ex = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new OfficialCalendarSource(CalendarHttp.CreateClient(down)).FetchYearAsync(2028, CancellationToken.None));
        Assert.Equal(CalendarSyncFailure.Failed, ex.Kind);

        var broken = new FakeHandler(r => r.RequestUri!.Host == "data.gov.tw"
            ? Text(Metadata("117年中華民國政府行政機關辦公日曆表", "https://files.test/bad.csv"))
            : Text("西元日期,星期,是否放假,備註\r\n20280101,六,2,開國紀念日\r\n"));
        await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new OfficialCalendarSource(CalendarHttp.CreateClient(broken)).FetchYearAsync(2028, CancellationToken.None));
    }

    [Fact]
    public async Task 備援來源_固定網址_404是尚未公告()
    {
        var ok = new FakeHandler(_ => Text(MirrorFixtures.Json(2027)));
        var days = await new MirrorCalendarSource(CalendarHttp.CreateClient(ok)).FetchYearAsync(2027, CancellationToken.None);
        Assert.Equal(365, days.Count);
        Assert.Equal(new[] { MirrorCalendarSource.UrlFor(2027) }, ok.Requests);

        var missing = new FakeHandler(_ => Text("", HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new MirrorCalendarSource(CalendarHttp.CreateClient(missing)).FetchYearAsync(2028, CancellationToken.None));
        Assert.Equal(CalendarSyncFailure.NotPublished, ex.Kind);
    }

    // ---- 紀錄檔 ----

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 10, 6, 1, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void 紀錄檔_一行一筆_超過1MB輪替()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sched-sync-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "data", "calendar-sync.log");
            var log = new FileCalendarSyncLog(path, new FixedTime());

            log.Write("第一行\n含換行");
            Assert.Equal(new[] { "2027-10-06T01:00:00Z 第一行 含換行" }, File.ReadAllLines(path));

            File.WriteAllText(path, new string('x', (int)FileCalendarSyncLog.MaxBytes + 1));
            log.Write("輪替後");

            Assert.True(File.Exists(path + ".1"));
            Assert.Single(File.ReadAllLines(path));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void 紀錄檔_寫不進去也不丟例外()
    {
        var file = Path.GetTempFileName();
        try
        {
            // 把「目錄」指到一個檔案底下：CreateDirectory 必失敗
            new FileCalendarSyncLog(Path.Combine(file, "sub", "x.log"), new FixedTime()).Write("沒關係");
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- 預設值與端點 ----

    [Fact]
    public void 自動更新預設關閉_fail_safe()
    {
        Assert.False(new ApiHostOptions().CalendarAutoSync);
    }

    [Fact]
    public async Task 沒開自動更新_sync_status回enabled_false且不在跑_也不連網()
    {
        var fakeSources = new List<ICalendarSource>();
        await using var host = await HostAsync(autoSync: false, fakeSources);

        var json = await host.GetStatusAsync();

        Assert.False(json["enabled"]!.GetValue<bool>());
        Assert.False(json["running"]!.GetValue<bool>());
        Assert.Null(json["finishedAt"]);
        Assert.Empty(json["updatedYears"]!.AsArray());
        // 沒開時連來源都沒註冊：不可能有任何網路呼叫
        Assert.Empty(host.App.Services.GetServices<ICalendarSource>());
    }

    // ---- 背景工作（整條路，來源是假的）----

    private sealed class DelegateSource : ICalendarSource
    {
        private readonly Func<int, IReadOnlyList<OfficialDay>> _fetch;

        public DelegateSource(string name, bool fallback, Func<int, IReadOnlyList<OfficialDay>> fetch)
        {
            Name = name;
            IsFallback = fallback;
            _fetch = fetch;
        }

        public string Name { get; }
        public bool IsFallback { get; }

        public Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken) =>
            Task.FromResult(_fetch(year));
    }

    private sealed class ListLog : ICalendarSyncLog
    {
        public List<string> Lines { get; } = new();
        public void Write(string message)
        {
            lock (Lines)
            {
                Lines.Add(message);
            }
        }
    }

    private sealed class TestHostHandle : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public TestHostHandle(WebApplication app, SqliteConnection connection, ListLog log)
        {
            App = app;
            _connection = connection;
            Log = log;
        }

        public WebApplication App { get; }
        public ListLog Log { get; }

        public async Task<JsonNode> GetAsync(string path, string? operationId = null)
        {
            using var client = App.GetTestClient();
            var text = await client.GetStringAsync(path);
            var json = JsonNode.Parse(text)!;
            if (operationId is not null)
            {
                var errors = ContractSchema.Current.Validate(operationId, 200, json);
                Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + text);
            }

            return json;
        }

        public Task<JsonNode> GetStatusAsync() => GetAsync("/api/calendars/sync-status", "getCalendarSyncStatus");

        /// <summary>等背景工作結束（上限 10 秒，逾時視為測試失敗而不是卡死）。</summary>
        public async Task<JsonNode> WaitUntilFinishedAsync()
        {
            for (var i = 0; i < 200; i++)
            {
                var json = await GetStatusAsync();
                if (!json["running"]!.GetValue<bool>())
                {
                    return json;
                }

                await Task.Delay(50);
            }

            throw new TimeoutException("背景工作 10 秒內沒有結束");
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static async Task<TestHostHandle> HostAsync(bool autoSync, IReadOnlyList<ICalendarSource> sources)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var log = new ListLog();
        var app = await ApiHost.BuildAsync(new ApiHostOptions(
            UseTestServer: true,
            SeedReferenceRoster: false,
            CalendarAutoSync: autoSync,
            ConfigurePersistence: services => services.AddSchedulerPersistence(connection),
            ConfigureServices: services =>
            {
                // 固定「今天」是 2027-10-06：同步今年 2027、明年 2028
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTime()));
                services.Replace(ServiceDescriptor.Singleton<ICalendarSyncLog>(log));
                if (sources.Count > 0)
                {
                    services.RemoveAll<ICalendarSource>();
                    foreach (var s in sources)
                    {
                        services.AddSingleton(s);
                    }
                }
            }));
        await app.StartAsync();
        return new TestHostHandle(app, connection, log);
    }

    /// <summary>2028 有一個補假與一個補班日；其他年份與內建資料一致（所以只有 2028 算「有更新」）。</summary>
    private static IReadOnlyList<OfficialDay> Official2028(int year) =>
        OfficialCalendar.ParseCsv(
            year == 2028
                ? YearCsv(2028, (new DateOnly(2028, 1, 3), 2, "補假"), (new DateOnly(2028, 2, 5), 0, "補行上班"))
                : YearCsv(year, BuiltIn(year)),
            year);

    private static (DateOnly, int, string)[] BuiltIn(int year) =>
        Scheduler.Persistence.Seed.BuiltInCalendar.Days
            .Where(d => d.Date.Year == year)
            .Select(d => (d.Date, 2, d.HolidayName ?? "放假"))
            .ToArray();

    [Fact]
    public async Task 開自動更新_背景寫入_sync_status回報更新的年份_且行事曆讀得到新值()
    {
        await using var host = await HostAsync(true, new ICalendarSource[] { new DelegateSource("official", false, Official2028) });

        var status = await host.WaitUntilFinishedAsync();

        Assert.True(status["enabled"]!.GetValue<bool>());
        Assert.Equal(new[] { 2028 }, status["updatedYears"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.NotNull(status["finishedAt"]);
        Assert.NotNull(status["lastSuccessAt"]);
        Assert.Null(status["lastError"]);
        Assert.Equal(new[] { 2026, 2027, 2028 }, status["years"]!.AsArray().Select(y => y!["year"]!.GetValue<int>()));
        Assert.All(status["years"]!.AsArray(), y => Assert.Equal("official", y!["source"]!.GetValue<string>()));

        var calendar = await host.GetAsync("/api/calendars/2028", "getCalendar");
        var days = calendar["days"]!.AsArray();
        Assert.True(days.Single(d => d!["date"]!.GetValue<string>() == "2028-01-03")!["isPublicHoliday"]!.GetValue<bool>());
        Assert.True(days.Single(d => d!["date"]!.GetValue<string>() == "2028-02-05")!["isMakeUpWorkday"]!.GetValue<bool>());
        Assert.False(days.Single(d => d!["date"]!.GetValue<string>() == "2028-02-05")!["overridden"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 所有來源都失敗_不丟例外_不寫資料_紀錄一行_仍可正常使用()
    {
        var sources = new ICalendarSource[]
        {
            new DelegateSource("official", false, _ => throw new HttpRequestException("name resolution failed")),
            new DelegateSource("mirror", true, _ => throw new InvalidOperationException("tls")),
        };
        await using var host = await HostAsync(true, sources);

        var status = await host.WaitUntilFinishedAsync();

        Assert.Empty(status["updatedYears"]!.AsArray());
        Assert.NotNull(status["lastError"]);
        Assert.Null(status["lastSuccessAt"]);
        Assert.Contains(host.Log.Lines, l => l.Contains("name resolution failed"));
        // 內建 2027 年資料原封不動
        var calendar = await host.GetAsync("/api/calendars/2027", "getCalendar");
        Assert.Equal(24, calendar["days"]!.AsArray().Count(d => d!["isPublicHoliday"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task 壞檔不寫入任何資料()
    {
        // 2028 的來源丟出驗證失敗（缺列）；2027 沒變
        var sources = new ICalendarSource[]
        {
            new DelegateSource("official", false, y => y == 2028
                ? OfficialCalendar.ParseCsv(YearCsv(2028).Replace("20280104,二,0,\r\n", ""), 2028)
                : Official2028(y)),
        };
        await using var host = await HostAsync(true, sources);

        var status = await host.WaitUntilFinishedAsync();

        Assert.Empty(status["updatedYears"]!.AsArray());
        var calendar = await host.GetAsync("/api/calendars/2028", "getCalendar");
        Assert.DoesNotContain(calendar["days"]!.AsArray(), d => d!["isPublicHoliday"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 使用者覆寫的日子不被自動更新蓋掉()
    {
        var sources = new ICalendarSource[] { new DelegateSource("official", false, Official2028) };
        await using var host = await HostAsync(autoSync: true, sources);
        await host.WaitUntilFinishedAsync();

        // 使用者把官方補班日 2028-02-05 改成放假，下一輪同步後仍然是使用者的值
        using (var client = host.App.GetTestClient())
        {
            var patch = await client.PatchAsync(
                "/api/calendars/2028/2028-02-05",
                new StringContent("""{"isHoliday":true,"isMakeUpWorkday":false}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        }

        using var scope = host.App.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<CalendarSyncService>().RunAsync();

        Assert.Empty(result.UpdatedYears);
        var calendar = await host.GetAsync("/api/calendars/2028", "getCalendar");
        var day = calendar["days"]!.AsArray().Single(d => d!["date"]!.GetValue<string>() == "2028-02-05")!;
        Assert.True(day["isHoliday"]!.GetValue<bool>());
        Assert.False(day["isMakeUpWorkday"]!.GetValue<bool>());
        Assert.True(day["overridden"]!.GetValue<bool>());
    }
}

internal static class MirrorFixtures
{
    /// <summary>備援來源的假 JSON：1/1 國定假日，其餘週末放假。</summary>
    public static string Json(int year)
    {
        string[] w = { "日", "一", "二", "三", "四", "五", "六" };
        var items = new List<string>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var first = d.Month == 1 && d.Day == 1;
            items.Add($"{{\"date\":\"{d:yyyyMMdd}\",\"week\":\"{w[(int)d.DayOfWeek]}\",\"isHoliday\":{(weekend || first ? "true" : "false")},\"description\":\"{(first ? "開國紀念日" : "")}\"}}");
        }

        return "[" + string.Join(",", items) + "]";
    }
}
