using System.Net;
using Scheduler.Application.Calendars.Sync;
using Scheduler.Application.Persistence;

namespace Scheduler.Api.CalendarSync;

/// <summary>
/// 主來源：人事行政總處辦公日曆表。先查 data.gov.tw 的 metadata 找下載網址（UUID 檔名每年、每次修正都會換，
/// 不能寫死），再下載 CSV、解碼（UTF-8 失敗退 Big5）、驗證。解析與驗證在 Application 的 <see cref="OfficialCalendar"/>。
/// </summary>
internal sealed class OfficialCalendarSource : ICalendarSource
{
    public const string MetadataUrl = "https://data.gov.tw/api/v2/rest/dataset/14718";

    private readonly HttpClient _http;

    public OfficialCalendarSource(HttpClient http)
    {
        _http = http;
    }

    public string Name => "official";

    public bool IsFallback => false;

    public async Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken)
    {
        var metadata = await CalendarHttp.GetStringAsync(_http, MetadataUrl, cancellationToken);
        var url = OfficialDatasetMetadata.PickDownloadUrl(metadata, year)
            ?? throw new CalendarSyncException(CalendarSyncFailure.NotPublished, "metadata 裡沒有該年的辦公日曆表");

        var bytes = await CalendarHttp.GetBytesAsync(_http, url, cancellationToken);
        return OfficialCalendar.ParseCsv(OfficialCalendar.Decode(bytes), year);
    }
}

/// <summary>備援：社群鏡像 ruyut/TaiwanCalendar（jsDelivr）。網址固定；404 視為尚未公告。</summary>
internal sealed class MirrorCalendarSource : ICalendarSource
{
    private readonly HttpClient _http;

    public MirrorCalendarSource(HttpClient http)
    {
        _http = http;
    }

    public string Name => "mirror";

    public bool IsFallback => true;

    public static string UrlFor(int year) => $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{year}.json";

    public async Task<IReadOnlyList<OfficialDay>> FetchYearAsync(int year, CancellationToken cancellationToken)
    {
        var json = await CalendarHttp.GetStringAsync(_http, UrlFor(year), cancellationToken, notFoundIsNotPublished: true);
        return OfficialCalendar.ParseMirrorJson(json, year);
    }
}

internal static class CalendarHttp
{
    /// <summary>每個請求的逾時。啟動時在背景跑，但不該讓一個不通的網路拖住整個流程。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>系統 proxy 設定（HttpClient 預設行為），不碰任何設定檔或登錄檔。</summary>
    public static HttpClient CreateClient(HttpMessageHandler? handler = null) =>
        new(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = Timeout };

    public static async Task<string> GetStringAsync(
        HttpClient http, string url, CancellationToken cancellationToken, bool notFoundIsNotPublished = false) =>
        System.Text.Encoding.UTF8.GetString(await GetBytesAsync(http, url, cancellationToken, notFoundIsNotPublished));

    public static async Task<byte[]> GetBytesAsync(
        HttpClient http, string url, CancellationToken cancellationToken, bool notFoundIsNotPublished = false)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNotPublished)
        {
            throw new CalendarSyncException(CalendarSyncFailure.NotPublished, "來源回 404");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CalendarSyncException(CalendarSyncFailure.Failed, $"HTTP {(int)response.StatusCode}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>
/// <c>data/calendar-sync.log</c>：一行一筆、超過 1 MB 輪替成 <c>.1</c>（只留一份）。
/// 寫不進去（唯讀、被占用）就算了——紀錄壞了不能讓同步或程式出事。
/// </summary>
internal sealed class FileCalendarSyncLog : ICalendarSyncLog
{
    public const long MaxBytes = 1024 * 1024;

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public FileCalendarSyncLog(string path, TimeProvider time)
    {
        _path = path;
        _time = time;
    }

    public void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                var line = $"{_time.GetUtcNow():yyyy-MM-ddTHH:mm:ssZ} {message.ReplaceLineEndings(" ")}{Environment.NewLine}";
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
            // 紀錄是盡力而為
        }
    }
}

/// <summary>
/// 啟動後在背景跑一次同步。Api 起來之後才執行、不擋啟動也不擋 UI；任何例外都吞掉並寫紀錄，
/// 背景工作絕不讓 process 崩潰（BackgroundService 的未處理例外預設會停掉整個 host）。
/// </summary>
internal sealed class CalendarSyncWorker : BackgroundService
{
    private readonly CalendarSyncRunner _runner;

    public CalendarSyncWorker(CalendarSyncRunner runner)
    {
        _runner = runner;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 讓出執行緒：host 啟動流程不等網路。啟動那一輪的 running 狀態在 CalendarSyncProgress 建構時就已成立，
        // 前端不會在這裡之前看到「閒置」。
        await Task.Yield();
        await _runner.RunAsync(stoppingToken);
    }
}

/// <summary>
/// 跑一輪同步並把結果寫進 <see cref="CalendarSyncProgress"/>。啟動（<see cref="CalendarSyncWorker"/>）與
/// 使用者按「重試」（<c>POST /api/calendars/sync</c>）共用；任何例外都吞掉並寫紀錄，背景工作絕不讓 process 崩潰。
/// </summary>
internal sealed class CalendarSyncRunner
{
    private readonly CalendarSyncService _service;
    private readonly CalendarSyncProgress _progress;
    private readonly ICalendarSyncLog _log;
    private readonly TimeProvider _time;
    private readonly IHostApplicationLifetime _lifetime;

    public CalendarSyncRunner(
        CalendarSyncService service, CalendarSyncProgress progress, ICalendarSyncLog log, TimeProvider time, IHostApplicationLifetime lifetime)
    {
        _service = service;
        _progress = progress;
        _log = log;
        _time = time;
        _lifetime = lifetime;
    }

    /// <summary>
    /// 重試：沒開、或已經在跑就什麼都不做（冪等，回 false）；否則先把狀態標成 running 再在背景開跑，
    /// 所以呼叫端緊接著讀到的狀態一定是 running。
    /// </summary>
    public bool StartIfIdle()
    {
        if (!_progress.TryBegin())
        {
            return false;
        }

        _ = Task.Run(() => RunAsync(_lifetime.ApplicationStopping));
        return true;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<int> updated = Array.Empty<int>();
        IReadOnlyList<string> affected = Array.Empty<string>();
        CalendarSyncFailureKind? failure = null;
        try
        {
            var result = await _service.RunAsync(stoppingToken);
            updated = result.UpdatedYears;
            affected = result.AffectedPublishedMonths;
            failure = result.FailureKind;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = CalendarSyncFailureKind.Failed;
            _log.Write($"同步中止：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _progress.Finish(_time.GetUtcNow(), updated, affected, failure);
        }
    }
}

/// <summary><see cref="ICalendarSyncScopeFactory"/> 的 DI 版：每次 <c>Create</c> 開一個新的 DI scope（新的 DbContext），用完就丟。</summary>
internal sealed class ServiceProviderCalendarSyncScopeFactory : ICalendarSyncScopeFactory
{
    private readonly IServiceScopeFactory _scopes;

    public ServiceProviderCalendarSyncScopeFactory(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public ICalendarSyncScope Create() => new Scope(_scopes.CreateScope());

    private sealed class Scope : ICalendarSyncScope
    {
        private readonly IServiceScope _scope;

        public Scope(IServiceScope scope)
        {
            _scope = scope;
        }

        public ICalendarRepository Calendar => _scope.ServiceProvider.GetRequiredService<ICalendarRepository>();

        public ICalendarSyncStateRepository State => _scope.ServiceProvider.GetRequiredService<ICalendarSyncStateRepository>();

        public IScheduleRepository Schedules => _scope.ServiceProvider.GetRequiredService<IScheduleRepository>();

        public IUnitOfWork UnitOfWork => _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public void Dispose() => _scope.Dispose();
    }
}
