using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Scheduler.Api;
using Scheduler.Api.Solving;

namespace Scheduler.Shell;

/// <summary>
/// WebView2 宿主（ARCHITECTURE §6）。Scheduler.Api 的 <see cref="WebApplication"/> 以 TestServer 在 process 內跑，
/// 不開 socket；這裡只拿到一個 <see cref="HttpClient"/> 與一個進度事件來源，路由、binding、錯誤碼都在 Api（硬性規則 2）。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string DataDirectory = Path.Combine(BaseDirectory, "data");
    private static readonly string WwwRoot = Path.Combine(BaseDirectory, "wwwroot");
    private static readonly string BundledRuntime = Path.Combine(BaseDirectory, "webview2");

    /// <summary>
    /// 參考名單 34 人只在開發期種（前端與手動測試要有資料可看）。發佈包一律是 Release
    /// build（<c>build/publish.ps1</c> 固定 <c>--configuration Release</c>），這裡用編譯期常數
    /// 而不是設定檔：不必碰 data/ 以外的任何檔案，也不會被使用者的環境變數意外打開（#37）。
    /// 動到這個常數要同時看 publish.ps1 是否還是 Release-only。
    /// </summary>
#if DEBUG
    private const bool SeedReferenceRoster = true;
#else
    private const bool SeedReferenceRoster = false;
#endif

    private readonly CancellationTokenSource _shutdown = new();
    private WebApplication? _app;
    private HttpClient? _api;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 啟動順序：data/ 可寫檢查 → Api（資料庫啟動流程在裡面）→ WebView2 環境 → 攔截 → 導向 index.html。
    /// 任何一步失敗都以對話框說明後關閉，不讓 async void 的例外把 process 炸掉（驗收清單第 4 項）。
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureDataDirectoryWritable();

            _app = await ApiHost.BuildAsync(
                new ApiHostOptions(UseTestServer: true, SeedReferenceRoster: SeedReferenceRoster), _shutdown.Token);
            await _app.StartAsync(_shutdown.Token);
            _api = _app.GetTestClient();

            var environment = await CoreWebView2Environment.CreateAsync(
                // 發佈包隨附 Fixed Version runtime；開發機沒有這個資料夾時退回機器上的 Evergreen runtime
                browserExecutableFolder: Directory.Exists(BundledRuntime) ? BundledRuntime : null,
                // 不可省略：預設寫進 %LOCALAPPDATA%，違反 portable 前提（§6.1）
                userDataFolder: Path.Combine(DataDirectory, "wv2data"));
            await WebView.EnsureCoreWebView2Async(environment);

            var core = WebView.CoreWebView2;
            // 整個 https://app.local/* 都由 Shell 回應：/api/ 轉 Api、其餘讀 wwwroot。
            // 不用 SetVirtualHostNameToFolderMapping：實測（runtime 152.0.4191）對應會搶在 WebResourceRequested
            // 之前吃掉同一主機的所有請求，事件連 /api/ 都收不到，見 ARCHITECTURE §6.2。
            core.AddWebResourceRequestedFilter($"{WebViewBridge.Origin}*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += OnWebResourceRequested;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif
            WebView.Source = WebViewBridge.IndexUri;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"程式無法啟動：{ex.Message}\n\n程式目錄：{BaseDirectory}\n請確認 data/ 可寫入，或把整個資料夾搬到可寫入的位置後再試。",
                "醫院排班系統", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown(1);
            return;
        }

        // 啟動完成後才開始推進度。這個迴圈活到關閉為止，刻意不放在上面的 try 裡：
        // 推送途中出錯（例如 browser process 崩潰）不該被當成「程式無法啟動」而關掉整個程式。
        await PumpProgressAsync(WebView.CoreWebView2, _app.Services.GetRequiredService<ISolverProgressFeed>());
    }

    /// <summary>所有執行期狀態都在程式旁的 data/；放在唯讀位置（光碟、Program Files）要在這裡就講清楚。</summary>
    private static void EnsureDataDirectoryWritable()
    {
        Directory.CreateDirectory(DataDirectory);
        var probe = Path.Combine(DataDirectory, $".write-probe-{Environment.ProcessId}");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }

    /// <summary>
    /// 攔截 https://app.local/* ：/api/ 轉給 process 內的 Api；無副檔名的頁面請求回 index.html（deep link）；
    /// 其餘讀 wwwroot 的檔案。非同步所以要 deferral；任何例外都變成 500 回應而不是往上拋。
    /// </summary>
    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = (CoreWebView2)sender!;
        var deferral = e.GetDeferral();
        try
        {
            var uri = new Uri(e.Request.Uri);
            switch (WebViewBridge.Classify(uri, e.ResourceContext == CoreWebView2WebResourceContext.Document))
            {
                case RouteKind.Api:
                    e.Response = await ForwardToApiAsync(core, uri, e.Request);
                    break;
                case RouteKind.IndexFallback:
                    e.Response = StaticFileOr404(core, Path.Combine(WwwRoot, "index.html"), "index.html（前端尚未 build？）");
                    break;
                default:
                    e.Response = StaticFileOr404(core, WebViewBridge.ResolveStaticFile(WwwRoot, uri), uri.AbsolutePath);
                    break;
            }
        }
        catch (Exception ex)
        {
            e.Response = Text(core, HttpStatusCode.InternalServerError, "Shell Error", ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<CoreWebView2WebResourceResponse> ForwardToApiAsync(CoreWebView2 core, Uri uri, CoreWebView2WebResourceRequest request)
    {
        using var message = await WebViewBridge.ToRequestAsync(request.Method, uri, request.Headers, request.Content, _shutdown.Token);
        using var response = await _api!.SendAsync(message, HttpCompletionOption.ResponseContentRead, _shutdown.Token);
        var body = new MemoryStream(await response.Content.ReadAsByteArrayAsync(_shutdown.Token));
        return core.Environment.CreateWebResourceResponse(
            body, (int)response.StatusCode, response.ReasonPhrase ?? string.Empty, WebViewBridge.FlattenHeaders(response));
    }

    /// <summary>整份讀進記憶體再交給 WebView2：它不保證釋放交出去的串流，檔案又都只有幾十 KB。</summary>
    private static CoreWebView2WebResourceResponse StaticFileOr404(CoreWebView2 core, string? path, string label) =>
        path is not null && File.Exists(path)
            ? core.Environment.CreateWebResourceResponse(
                new MemoryStream(File.ReadAllBytes(path)), 200, "OK", $"Content-Type: {WebViewBridge.ContentTypeOf(path)}")
            : Text(core, HttpStatusCode.NotFound, "Not Found", $"找不到 {label}");

    private static CoreWebView2WebResourceResponse Text(CoreWebView2 core, HttpStatusCode status, string reason, string text) =>
        core.Environment.CreateWebResourceResponse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), (int)status, reason, "Content-Type: text/plain; charset=utf-8");

    /// <summary>
    /// 求解進度不走 WebResourceRequested（回應不會漸進送出），改由 Api 的進度事件來源推 PostWebMessageAsJson；
    /// JSON 與 SSE 端點寫的完全相同，前端 realtime.ts 靠 jobId 過濾。await 回到 UI 執行緒，CoreWebView2 才能碰。
    /// 單筆推送失敗只略過那一筆，訂閱要活到關閉：一旦離開迴圈就沒有重訂閱的路。
    /// </summary>
    private async Task PumpProgressAsync(CoreWebView2 core, ISolverProgressFeed feed)
    {
        try
        {
            await foreach (var json in feed.AllAsync(_shutdown.Token))
            {
                try
                {
                    core.PostWebMessageAsJson(json);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Trace.TraceWarning($"進度推送失敗：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // 視窗關閉中
        }
    }

    /// <summary>
    /// WPF 不會等 async void 的 Closed 跑完，Api 的 DisposeAsync 常常來不及；這沒有實害——
    /// 卡在 running 的求解工作由下次啟動的資料庫啟動流程標成失敗（ADR／§4.8），資料庫本身每個用例一次 commit。
    /// </summary>
    private async void OnClosed(object? sender, EventArgs e)
    {
        _shutdown.Cancel();
        _api?.Dispose();
        if (_app is not null)
        {
            try
            {
                await _app.DisposeAsync();
            }
            catch
            {
                // 關閉中，沒人在看
            }
        }

        _shutdown.Dispose();
        WebView.Dispose();
    }
}
