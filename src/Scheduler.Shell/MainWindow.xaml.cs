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

            _app = await ApiHost.BuildAsync(new ApiHostOptions(UseTestServer: true), _shutdown.Token);
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

            await PumpProgressAsync(core, _app.Services.GetRequiredService<ISolverProgressFeed>());
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // 視窗關閉中
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"程式無法啟動：{ex.Message}\n\n程式目錄：{BaseDirectory}\n請確認 data/ 可寫入，或把整個資料夾搬到可寫入的位置後再試。",
                "醫院排班系統", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown(1);
        }
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
                    e.Response = StaticFile(core, Path.Combine(WwwRoot, "index.html"));
                    break;
                default:
                    var file = WebViewBridge.ResolveStaticFile(WwwRoot, uri);
                    e.Response = file is not null && File.Exists(file)
                        ? StaticFile(core, file)
                        : Text(core, HttpStatusCode.NotFound, "Not Found", $"找不到 {uri.AbsolutePath}");
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

    private static CoreWebView2WebResourceResponse StaticFile(CoreWebView2 core, string path) =>
        core.Environment.CreateWebResourceResponse(
            File.OpenRead(path), 200, "OK", $"Content-Type: {WebViewBridge.ContentTypeOf(path)}");

    private static CoreWebView2WebResourceResponse Text(CoreWebView2 core, HttpStatusCode status, string reason, string text) =>
        core.Environment.CreateWebResourceResponse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), (int)status, reason, "Content-Type: text/plain; charset=utf-8");

    /// <summary>
    /// 求解進度不走 WebResourceRequested（回應不會漸進送出），改由 Api 的進度事件來源推 PostWebMessageAsJson；
    /// JSON 與 SSE 端點寫的完全相同，前端 realtime.ts 靠 jobId 過濾。await 回到 UI 執行緒，CoreWebView2 才能碰。
    /// </summary>
    private async Task PumpProgressAsync(CoreWebView2 core, ISolverProgressFeed feed)
    {
        await foreach (var json in feed.AllAsync(_shutdown.Token))
        {
            core.PostWebMessageAsJson(json);
        }
    }

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
    }
}
