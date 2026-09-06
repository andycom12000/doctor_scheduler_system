using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Scheduler.Persistence.Mapping;

/// <summary>
/// <see cref="DateTimeOffset"/> 一律以 UTC 的 ISO-8601 字串儲存（<c>2026-09-06T03:51:54.1234567Z</c>）。
/// SQLite provider 預設的 DateTimeOffset 寫法帶時區偏移、字串順序不等於時間順序，
/// 而且 provider 直接拒絕拿它排序或比大小。固定 UTC、固定寬度之後，字串順序就是時間順序，
/// 資料庫裡也看得懂。讀回來是 UTC 的 DateTimeOffset，要顯示時再轉本地時間。
/// </summary>
internal sealed class UtcTimestampConverter : ValueConverter<DateTimeOffset, string>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public UtcTimestampConverter()
        : base(
            v => v.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
            s => new DateTimeOffset(DateTime.ParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), TimeSpan.Zero))
    {
    }
}
