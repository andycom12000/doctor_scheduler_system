using System.Net;
using System.Text;

namespace Scheduler.Shell.Tests;

/// <summary>
/// WebView2 ↔ HttpClient 轉換的純邏輯（ARCHITECTURE §6.2、§6.3）。WebView2 本身只能在 Windows 上手動驗，
/// 這裡守的是最容易錯的三件事：路由分類、標頭該掛在哪、回應標頭怎麼攤。
/// </summary>
public class WebViewBridgeTests
{
    private static Uri At(string path) => new(WebViewBridge.Origin, path);

    [Theory]
    [InlineData("/api/health", true, RouteKind.Api)]
    [InlineData("/api/schedules/2026-09", false, RouteKind.Api)]
    [InlineData("/api", true, RouteKind.Api)]
    [InlineData("/schedules/2026-09", true, RouteKind.IndexFallback)]
    [InlineData("/", true, RouteKind.IndexFallback)]
    [InlineData("/index.html", true, RouteKind.Static)]
    [InlineData("/assets/index-abc.js", false, RouteKind.Static)]
    [InlineData("/schedules/2026-09", false, RouteKind.Static)]
    [InlineData("/apiary", true, RouteKind.IndexFallback)]
    public void 路由分類_api_轉送_無副檔名的頁面回_index_其餘靜態(string path, bool isDocument, RouteKind expected)
        => Assert.Equal(expected, WebViewBridge.Classify(At(path), isDocument));

    [Fact]
    public void 靜態檔解析_留在_wwwroot_裡_跳出去的回_null()
    {
        var root = Path.Combine(Path.GetTempPath(), "wwwroot-test");
        var inside = WebViewBridge.ResolveStaticFile(root, At("/assets/a.js"));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "assets", "a.js")), inside);

        // 明寫的 .. 在 URL 階段就被正規化掉了（瀏覽器也一樣），會鑽進來的是逃逸過的
        Assert.Null(WebViewBridge.ResolveStaticFile(root, new Uri("https://app.local/a/..%2f..%2fsecret.txt")));
        // 同名前綴的兄弟資料夾也不算在裡面
        Assert.Null(WebViewBridge.ResolveStaticFile(root, new Uri("https://app.local/..%2fwwwroot-test2/x.js")));
    }

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("assets/x.js", "text/javascript; charset=utf-8")]
    [InlineData("assets/x.css", "text/css; charset=utf-8")]
    [InlineData("favicon.svg", "image/svg+xml")]
    [InlineData("assets/x.woff2", "font/woff2")]
    [InlineData("x.unknown", "application/octet-stream")]
    public void 內容類型依副檔名(string path, string expected) => Assert.Equal(expected, WebViewBridge.ContentTypeOf(path));

    [Fact]
    public async Task 請求轉換_相對路徑_內容標頭掛在_Content_上_Host_丟掉_本體可重讀()
    {
        var body = new NonSeekableStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        var headers = new[]
        {
            KeyValuePair.Create("Host", "app.local"),
            KeyValuePair.Create("Content-Type", "application/json"),
            KeyValuePair.Create("Content-Length", "7"),
            KeyValuePair.Create("Accept", "application/json"),
            KeyValuePair.Create("X-Custom", "v"),
        };

        using var request = await WebViewBridge.ToRequestAsync("PUT", At("/api/staff/s-1?x=1"), headers, body, CancellationToken.None);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/api/staff/s-1?x=1", request.RequestUri!.OriginalString);
        Assert.False(request.RequestUri.IsAbsoluteUri);
        Assert.Null(request.Headers.Host);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.ToString());
        Assert.Equal(7, request.Content.Headers.ContentLength);
        Assert.Equal("application/json", request.Headers.Accept.ToString());
        Assert.Equal("v", request.Headers.GetValues("X-Custom").Single());
        Assert.Equal("""{"a":1}""", await request.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 請求轉換_沒本體的_GET_內容標頭直接略過()
    {
        var headers = new[] { KeyValuePair.Create("Content-Type", "application/json"), KeyValuePair.Create("Accept", "*/*") };
        using var request = await WebViewBridge.ToRequestAsync("GET", At("/api/health"), headers, null, CancellationToken.None);

        Assert.Null(request.Content);
        Assert.Equal("*/*", request.Headers.Accept.ToString());
    }

    [Fact]
    public void 回應標頭攤平_含內容標頭_丟掉_Transfer_Encoding()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        response.Headers.TransferEncodingChunked = true;
        response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        response.Headers.TryAddWithoutValidation("Location", "/api/solver-jobs/job-1");

        var flat = WebViewBridge.FlattenHeaders(response);
        var lines = flat.Split("\r\n");

        Assert.Contains("Content-Type: application/json; charset=utf-8", lines);
        Assert.Contains("Cache-Control: no-cache", lines);
        Assert.Contains("Location: /api/solver-jobs/job-1", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Transfer-Encoding", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("\n", flat.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void 存檔對話框_以_WebView2_建議的路徑帶出檔名資料夾與_Excel_篩選()
    {
        var suggested = Path.Combine(Path.GetTempPath(), "Downloads", "duty-2026-11.xlsx");

        var dialog = WebViewBridge.SaveDialogFor(suggested);

        Assert.Equal("duty-2026-11.xlsx", dialog.FileName);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Downloads"), dialog.InitialDirectory);
        Assert.Equal(".xlsx", dialog.DefaultExt);
        Assert.Equal("Excel 活頁簿 (*.xlsx)|*.xlsx|所有檔案 (*.*)|*.*", dialog.Filter);
    }

    [Theory]
    [InlineData("report.csv", ".csv", "CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*")]
    [InlineData("REPORT.XLSX", ".xlsx", "Excel 活頁簿 (*.xlsx)|*.xlsx|所有檔案 (*.*)|*.*")]
    [InlineData("data.bin", ".bin", "BIN 檔案 (*.bin)|*.bin|所有檔案 (*.*)|*.*")]
    [InlineData("noext", "", "所有檔案 (*.*)|*.*")]
    public void 存檔對話框_篩選依副檔名_不認得的照副檔名列_沒有副檔名只給所有檔案(string name, string defaultExt, string filter)
    {
        var dialog = WebViewBridge.SaveDialogFor(Path.Combine(Path.GetTempPath(), name));

        Assert.Equal(name, dialog.FileName);
        Assert.Equal(defaultExt, dialog.DefaultExt);
        Assert.Equal(filter, dialog.Filter);
    }

    [Theory]
    [InlineData("print", true)]
    [InlineData("saveAs", false)]
    [InlineData("copy", false)]
    [InlineData("Print", false)]
    public void 右鍵選單_只拿掉列印_列印要走排班主表的按鈕(string name, bool expected)
        => Assert.Equal(expected, WebViewBridge.IsBlockedContextMenuItem(name));

    /// <summary>WebView2 給的 COM 串流不可 seek，轉換要自己緩衝。</summary>
    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}
