using System.IO;
using System.Text.Json;

namespace Scheduler.Shell;

/// <summary>前端交來、要存到使用者所選路徑的檔案（<c>FileName</c> 已清過，<c>Bytes</c> 已解碼）。</summary>
public sealed record SaveFileRequest(string Id, string FileName, byte[] Bytes);

/// <summary>
/// 前端 → Shell 的「存檔」訊息協定（#70）。匯出不再走 Chromium 的下載機制（那條路會在使用者選位置之前
/// 就把內容寫進「下載」資料夾的 <c>GUID.tmp</c>），改成前端用 <c>fetch</c> 拿到位元組後 base64 編碼、
/// <c>chrome.webview.postMessage</c> 交給 Shell；Shell 跳系統存檔對話框，只在使用者按儲存後才寫入選定的路徑。
/// 這支只做純轉換（解析、檔名、回覆 JSON），沒有 WebView2／WPF 型別，好測。
/// 訊息形狀要與 <c>frontend/src/realtime.ts</c> 的 <c>saveFile</c> 一致。
/// </summary>
public static class SaveFileProtocol
{
    public const string RequestType = "save-file";
    public const string ResultType = "save-file-result";

    /// <summary>單檔上限（解碼後位元組）。匯出檔只有幾十 KB，上限是擋異常訊息，不是業務限制。</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    public const string Saved = "saved";
    public const string Cancelled = "cancelled";
    public const string Failed = "error";

    /// <summary>只收來自 app.local 的頁面（含 about:blank 之類一律拒絕）。</summary>
    public static bool IsTrustedSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == WebViewBridge.Origin.Scheme
        && uri.Host.Equals(WebViewBridge.HostName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析一則 WebMessage。不是存檔訊息（例如別種型別、不是物件）回 <see cref="ParseOutcome.NotForUs"/>；
    /// 是存檔訊息但內容壞掉回 <see cref="ParseOutcome.Invalid"/>，並盡量帶回 id 讓前端的等待能收到失敗。
    /// </summary>
    public static ParseOutcome Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ParseOutcome.NotForUs;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || StringOf(root, "type") != RequestType)
            {
                return ParseOutcome.NotForUs;
            }

            var id = StringOf(root, "id");
            var base64 = StringOf(root, "base64");
            if (string.IsNullOrEmpty(id) || base64 is null)
            {
                return ParseOutcome.Invalid(id, "存檔訊息缺少欄位。");
            }

            // Base64 長度 ≈ 4/3 位元組；先擋長度再解碼，免得為了異常訊息配出一大塊記憶體
            if (base64.Length > MaxBytes / 3 * 4 + 4)
            {
                return ParseOutcome.Invalid(id, "檔案太大。");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return ParseOutcome.Invalid(id, "檔案內容不是有效的 base64。");
            }

            if (bytes.Length > MaxBytes)
            {
                return ParseOutcome.Invalid(id, "檔案太大。");
            }

            return ParseOutcome.Ok(new SaveFileRequest(id, SafeFileName(StringOf(root, "fileName")), bytes));
        }
    }

    /// <summary>
    /// 對話框的預設檔名：只留檔名部分（丟掉任何路徑）、換掉 Windows 不允許的字元，
    /// 副檔名只認 <c>.xlsx</c>（匯出只有這一種），不是就補上；空的給 <c>export.xlsx</c>。
    /// </summary>
    public static string SafeFileName(string? requested)
    {
        const string fallback = "export.xlsx";
        if (string.IsNullOrWhiteSpace(requested)) return fallback;

        // 兩種分隔符都要切：在非 Windows 的測試主機上 Path.GetFileName 不認得反斜線
        var name = requested.Trim();
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        name = string.Concat(name.Select(c => invalid.Contains(c) || c == ':' ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0) return fallback;

        return name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? name : name + ".xlsx";
    }

    /// <summary>回給前端的結果 JSON（<c>PostWebMessageAsJson</c> 用）。<paramref name="message"/> 只在失敗時帶。</summary>
    public static string ResultJson(string id, string status, string? message = null) =>
        JsonSerializer.Serialize(message is null
            ? new Dictionary<string, string> { ["type"] = ResultType, ["id"] = id, ["status"] = status }
            : new Dictionary<string, string> { ["type"] = ResultType, ["id"] = id, ["status"] = status, ["message"] = message });

    private static string? StringOf(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary><see cref="SaveFileProtocol.Parse"/> 的結果：不歸我們處理／內容壞掉／可以存。</summary>
public sealed record ParseOutcome(bool IsForUs, SaveFileRequest? Request, string? Id, string? Error)
{
    public static readonly ParseOutcome NotForUs = new(false, null, null, null);

    public static ParseOutcome Ok(SaveFileRequest request) => new(true, request, request.Id, null);

    public static ParseOutcome Invalid(string? id, string error) => new(true, null, id, error);
}
