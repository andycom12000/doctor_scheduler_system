using System.Globalization;

namespace Scheduler.Domain.Model;

/// <summary>值班表的識別子：一月一份。字串形式為 <c>yyyy-MM</c>。</summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException($"年月格式應為 yyyy-MM：{text}");
        }

        return value;
    }

    public static bool TryParse(string? text, out YearMonth value)
    {
        value = default;
        if (text is null || text.Length != 7 || text[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            month is < 1 or > 12)
        {
            return false;
        }

        value = new YearMonth(year, month);
        return true;
    }

    public static YearMonth Of(DateOnly date) => new(date.Year, date.Month);

    public int DayCount => DateTime.DaysInMonth(Year, Month);

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DayCount);

    public YearMonth Previous => Month == 1 ? new YearMonth(Year - 1, 12) : new YearMonth(Year, Month - 1);

    public YearMonth Next => Month == 12 ? new YearMonth(Year + 1, 1) : new YearMonth(Year, Month + 1);

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public IEnumerable<DateOnly> Days()
    {
        for (var day = 1; day <= DayCount; day++)
        {
            yield return new DateOnly(Year, Month, day);
        }
    }

    public int CompareTo(YearMonth other) => (Year, Month).CompareTo((other.Year, other.Month));

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
