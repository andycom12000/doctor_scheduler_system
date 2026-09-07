using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Scheduler.Application.Errors;

namespace Scheduler.Api.Http;

/// <summary>
/// 請求本體的讀取。與 <see cref="Parse"/> 同一個理由：Minimal API 的 <c>[FromBody]</c> 遇到壞 JSON 回沒有
/// <c>ErrorResponse</c> 形狀的 400，所以自己讀、壞掉統一是 422 <c>INVALID_REQUEST</c>。
/// DTO 的欄位刻意全是 nullable，缺欄位由 <see cref="RequestMapper"/> 逐一判定，而不是讓反序列化默默塞預設值。
/// </summary>
internal static class RequestBody
{
    /// <summary>必要本體。空本體或壞 JSON 都是 422。</summary>
    public static async Task<T> ReadAsync<T>(HttpContext http) where T : class =>
        await ReadOptionalAsync<T>(http) ?? throw new SchedulerException(ErrorCode.InvalidRequest, "請求本體不得為空");

    /// <summary>可省略的本體（契約 <c>required: false</c>）。沒送、空本體或 JSON <c>null</c> 都回 null。</summary>
    public static async Task<T?> ReadOptionalAsync<T>(HttpContext http) where T : class
    {
        // 沒送本體：長度 0，或長度未知且沒有 JSON 的 Content-Type（fetch 帶了 header 卻沒帶 body 也算沒送）
        if (http.Request.ContentLength == 0 || (http.Request.ContentLength is null && !http.Request.HasJsonContentType()))
        {
            return null;
        }

        if (!http.Request.HasJsonContentType())
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "請求本體必須是 application/json");
        }

        try
        {
            return await http.Request.ReadFromJsonAsync<T>(Options(http), http.RequestAborted);
        }
        catch (JsonException e)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, $"請求本體不是合法的 JSON：{e.Message}");
        }
    }

    /// <summary>以 JSON 物件讀本體：欄位「沒送」與「送 null」要分開時用（行事曆覆寫的 <c>holidayName</c>）。</summary>
    public static async Task<JsonObject> ReadObjectAsync(HttpContext http)
    {
        var node = await ReadAsync<JsonNode>(http);
        return node as JsonObject ?? throw new SchedulerException(ErrorCode.InvalidRequest, "請求本體必須是 JSON 物件");
    }

    private static JsonSerializerOptions Options(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
}
