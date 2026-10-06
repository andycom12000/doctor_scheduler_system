using System.Globalization;
using System.Text;
using System.Text.Json;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Calendars.Sync;

/// <summary>官方來源的一天：放不放假與備註，尚未對應到領域的假日分類。</summary>
public sealed record OfficialDay(DateOnly Date, bool IsOffDay, string Remark);

public enum CalendarSyncFailure
{
    /// <summary>來源明確表示這一年還沒公告（metadata 裡沒有、鏡像 404）。不是故障。</summary>
    NotPublished,

    /// <summary>網路、逾時、TLS、格式不符、驗證不過。</summary>
    Failed,
}

/// <summary>同步過程的預期失敗。訊息只寫進 <c>data/calendar-sync.log</c>，不給使用者看。</summary>
public sealed class CalendarSyncException : Exception
{
    public CalendarSyncException(CalendarSyncFailure kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public CalendarSyncFailure Kind { get; }
}

/// <summary>
/// 人事行政總處辦公日曆表（CSV）與社群鏡像（JSON）的解析、驗證，以及對應到領域的例外日。
/// 驗證全部通過才回結果：一年 365／366 列、日期從 1/1 連續到 12/31、星期對得上。
/// 任何一步失敗就丟 <see cref="CalendarSyncException"/>，呼叫端整年都不寫。
/// </summary>
public static class OfficialCalendar
{
    private const string WeekdayChars = "日一二三四五六"; // 對應 DayOfWeek 0..6

    static OfficialCalendar()
    {
        // Big5（cp950）在 .NET Core 要註冊 provider；System.Text.Encoding.CodePages 在 shared framework，不需要套件
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>先試 UTF-8（含 BOM，遇到壞位元組就失敗），失敗退 Big5。</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("big5", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                .GetString(bytes);
        }
    }

    /// <summary>CSV：<c>西元日期,星期,是否放假,備註</c>，第一列是表頭。</summary>
    public static IReadOnlyList<OfficialDay> ParseCsv(string text, int year)
    {
        var lines = text.Split('\n').Select(l => l.Trim('\r', ' ', '\uFEFF')).Where(l => l.Length > 0).ToList();
        if (lines.Count < 2)
        {
            throw Bad($"CSV 沒有資料列（{lines.Count} 列）");
        }

        var days = new List<OfficialDay>(lines.Count - 1);
        var weekdays = new List<string>(lines.Count - 1);
        foreach (var line in lines.Skip(1))
        {
            // 備註可能含逗號以外的字元；官方檔備註不含逗號，多餘欄位併回備註
            var cols = line.Split(',', 4);
            if (cols.Length < 3)
            {
                throw Bad($"欄位不足：{Truncate(line)}");
            }

            if (!DateOnly.TryParseExact(cols[0].Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw Bad($"日期格式不符：{Truncate(cols[0])}");
            }

            var flag = cols[2].Trim();
            if (flag is not ("0" or "2"))
            {
                throw Bad($"{date:yyyy-MM-dd} 的「是否放假」不是 0 或 2：{Truncate(flag)}");
            }

            days.Add(new OfficialDay(date, flag == "2", cols.Length > 3 ? cols[3].Trim() : ""));
            weekdays.Add(cols[1].Trim());
        }

        return Validate(days, weekdays, year);
    }

    /// <summary>鏡像 JSON：<c>[{"date":"20270101","week":"五","isHoliday":true,"description":"開國紀念日"}]</c>。</summary>
    public static IReadOnlyList<OfficialDay> ParseMirrorJson(string json, int year)
    {
        var days = new List<OfficialDay>();
        var weekdays = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw Bad("鏡像 JSON 根節點不是陣列");
            }

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var dateText = e.GetProperty("date").GetString() ?? "";
                if (!DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw Bad($"日期格式不符：{Truncate(dateText)}");
                }

                var description = e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
                days.Add(new OfficialDay(date, e.GetProperty("isHoliday").GetBoolean(), description.Trim()));
                weekdays.Add(e.GetProperty("week").GetString() ?? "");
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Bad("鏡像 JSON 格式不符：" + ex.Message, ex);
        }

        return Validate(days, weekdays, year);
    }

    /// <summary>
    /// 對應到領域的例外日，只回「不等於純週末推算」的日子（與資料庫只存例外日一致）。
    /// 放假：平日一律國定假日（含補假）；週末要有備註才算國定假日，沒有備註就是一般週末。
    /// 上班：週六日才是補班日；平日上班不是例外。
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, CalendarDay> ToExceptions(IReadOnlyList<OfficialDay> days)
    {
        var result = new Dictionary<DateOnly, CalendarDay>();
        foreach (var d in days)
        {
            var weekend = d.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var name = string.IsNullOrWhiteSpace(d.Remark) ? null : d.Remark;
            if (d.IsOffDay)
            {
                if (!weekend || name is not null)
                {
                    result[d.Date] = new CalendarDay(d.Date, IsHoliday: true, IsPublicHoliday: true, IsMakeUpWorkday: false, name);
                }
            }
            else if (weekend)
            {
                result[d.Date] = new CalendarDay(d.Date, IsHoliday: false, IsPublicHoliday: false, IsMakeUpWorkday: true, name);
            }
        }

        return result;
    }

    private static IReadOnlyList<OfficialDay> Validate(List<OfficialDay> days, List<string> weekdays, int year)
    {
        var expected = DateTime.IsLeapYear(year) ? 366 : 365;
        if (days.Count != expected)
        {
            throw Bad($"{year} 年應有 {expected} 列，實際 {days.Count} 列");
        }

        var start = new DateOnly(year, 1, 1);
        for (var i = 0; i < days.Count; i++)
        {
            var want = start.AddDays(i);
            if (days[i].Date != want)
            {
                throw Bad($"日期不連續：第 {i + 1} 列應為 {want:yyyy-MM-dd}，實際 {days[i].Date:yyyy-MM-dd}");
            }

            var wantChar = WeekdayChars[(int)want.DayOfWeek].ToString();
            if (weekdays[i] != wantChar && weekdays[i] != "週" + wantChar && weekdays[i] != "星期" + wantChar)
            {
                throw Bad($"{want:yyyy-MM-dd} 的星期對不上（應為{wantChar}）");
            }
        }

        return days;
    }

    private static CalendarSyncException Bad(string message, Exception? inner = null) =>
        new(CalendarSyncFailure.Failed, message, inner);

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "…";
}

/// <summary>data.gov.tw 資料集 14718 的 metadata 解析：找出某年「辦公日曆表」的下載網址。</summary>
public static class OfficialDatasetMetadata
{
    /// <summary>
    /// 描述以「{民國年}年中華民國政府行政機關辦公日曆表」開頭、不含「Google」的項目。
    /// 同一年多份時取最新：描述括號內的更新日期（例如 <c>(1141020更新)</c>）較大者；沒有日期的視為最舊，
    /// 日期相同或都沒有時取清單裡較後面的。找不到回 null（官方尚未公告）。
    /// </summary>
    public static string? PickDownloadUrl(string metadataJson, int year)
    {
        var prefix = $"{year - 1911}年中華民國政府行政機關辦公日曆表";
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var distribution = doc.RootElement.GetProperty("result").GetProperty("distribution");
            string? best = null;
            var bestStamp = -1L;
            foreach (var item in distribution.EnumerateArray())
            {
                var description = item.TryGetProperty("resourceDescription", out var d) ? d.GetString() ?? "" : "";
                if (!description.StartsWith(prefix, StringComparison.Ordinal)
                    || description.Contains("Google", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = item.TryGetProperty("resourceDownloadUrl", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var stamp = UpdateStamp(description);
                if (stamp >= bestStamp)
                {
                    best = url;
                    bestStamp = stamp;
                }
            }

            return best;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new CalendarSyncException(CalendarSyncFailure.Failed, "metadata 格式不符：" + ex.Message, ex);
        }
    }

    private static long UpdateStamp(string description)
    {
        var m = System.Text.RegularExpressions.Regex.Match(description, @"(\d{6,7})\s*[^\d)）]*(更新|修正)");
        return m.Success && long.TryParse(m.Groups[1].Value, out var v) ? v : 0;
    }
}
