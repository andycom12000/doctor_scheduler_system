using System.IO;
using System.Net.Http;

namespace Scheduler.Shell;

/// <summary>WebView2 攔到的請求該怎麼處理（ARCHITECTURE §6.2、§6.3）。</summary>
public enum RouteKind
{
    /// <summary><c>/api/</c> 底下：轉給 process 內的 Scheduler.Api。</summary>
    Api,

    /// <summary>SPA deep link：非 <c>/api/</c>、無副檔名的頁面請求，回 <c>index.html</c>。</summary>
    IndexFallback,

    /// <summary>其餘：wwwroot 裡的靜態資產，由 Shell 自己讀檔回應。</summary>
    Static,
}

/// <summary>
/// WebView2 請求 ↔ <see cref="HttpClient"/> 訊息的純轉換，沒有 WebView2 型別，好測。
/// 路由、狀態碼、錯誤格式都不在這裡決定——那些在 Scheduler.Api（硬性規則 2）；這裡只搬位元組與標頭。
/// </summary>
public static class WebViewBridge
{
    public const string HostName = "app.local";
    public static readonly Uri Origin = new($"https://{HostName}/");
    public static readonly Uri IndexUri = new(Origin, "index.html");

    /// <summary>
    /// 只有頁面（Document）請求才做 deep link fallback：前端的資料請求只打 <c>/api/</c>，
    /// 而 <c>&lt;script&gt;</c>、<c>&lt;img&gt;</c> 等資產一律有副檔名，走靜態檔。
    /// </summary>
    public static RouteKind Classify(Uri uri, bool isDocument)
    {
        var path = uri.AbsolutePath;
        if (path.StartsWith("/api/", StringComparison.Ordinal) || path == "/api")
        {
            return RouteKind.Api;
        }

        if (isDocument && !Path.HasExtension(path))
        {
            return RouteKind.IndexFallback;
        }

        return RouteKind.Static;
    }

    /// <summary>
    /// 把 URL 路徑對到 wwwroot 底下的檔案；跳出 wwwroot 的路徑（<c>..</c>）回 null。
    /// 路徑已由瀏覽器正規化，這裡再用 <see cref="Path.GetFullPath(string)"/> 守一次。
    /// </summary>
    public static string? ResolveStaticFile(string wwwRoot, Uri uri)
    {
        var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(wwwRoot);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Vite 產物會出現的幾種副檔名；其餘一律 octet-stream，瀏覽器照樣能下載。</summary>
    public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// 下載要跳的「另存新檔」對話框內容：WebView2 預設會不問就存進「下載」資料夾（#33 實測），
    /// 所以 Shell 接手 <c>DownloadStarting</c>，以它建議的路徑當預設檔名與資料夾，讓使用者自己選位置。
    /// </summary>
    public static SaveDialogSpec SaveDialogFor(string suggestedPath)
    {
        var extension = Path.GetExtension(suggestedPath).ToLowerInvariant();
        const string all = "所有檔案 (*.*)|*.*";
        var filter = extension switch
        {
            "" => all,
            ".xlsx" => $"Excel 活頁簿 (*.xlsx)|*.xlsx|{all}",
            _ => $"{extension.TrimStart('.').ToUpperInvariant()} 檔案 (*{extension})|*{extension}|{all}",
        };
        return new SaveDialogSpec(Path.GetFileName(suggestedPath), Path.GetDirectoryName(suggestedPath) ?? string.Empty, extension, filter);
    }

    /// <summary>
    /// 組出送進 TestServer 的請求：相對路徑（TestServer 的 BaseAddress 是 <c>http://localhost/</c>）、
    /// 本體先整份讀進記憶體（WebView2 的 COM 串流不可重讀）、內容類標頭放到 <see cref="HttpContent.Headers"/>，
    /// 其餘標頭原樣搬；<c>Host</c> 丟掉，TestServer 自己會填。
    /// </summary>
    public static async Task<HttpRequestMessage> ToRequestAsync(
        string method,
        Uri uri,
        IEnumerable<KeyValuePair<string, string>> headers,
        Stream? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri.PathAndQuery);

        if (body is not null)
        {
            var buffer = new MemoryStream();
            await body.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            request.Content = new StreamContent(buffer);
        }

        foreach (var (name, value) in headers)
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (request.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            // 內容類標頭（Content-Type、Content-Length…）只能掛在 Content 上；沒本體就沒地方放，略過
            request.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>
    /// 把回應標頭攤成 WebView2 要的 <c>Name: Value\r\n</c> 列表。本體已整份緩衝，
    /// 所以 <c>Transfer-Encoding: chunked</c>（TestServer 常這樣回）要丟掉，長度由串流決定。
    /// </summary>
    public static string FlattenHeaders(HttpResponseMessage response)
    {
        var lines = new List<string>();
        Append(response.Headers);
        Append(response.Content.Headers);
        return string.Join("\r\n", lines);

        void Append(System.Net.Http.Headers.HttpHeaders source)
        {
            foreach (var (name, values) in source)
            {
                if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    lines.Add($"{name}: {value}");
                }
            }
        }
    }
}

/// <summary>「另存新檔」對話框的預設值；欄位名稱對應 <c>Microsoft.Win32.SaveFileDialog</c> 的同名屬性。</summary>
public sealed record SaveDialogSpec(string FileName, string InitialDirectory, string DefaultExt, string Filter);
