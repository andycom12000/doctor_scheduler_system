namespace Scheduler.Domain.Model;

/// <summary>
/// 日類：約束適用範圍的第三個維度。「NP 避開假日」靠它表達。
/// <see cref="Holiday"/> 是週六、週日與國定假日的總稱；
/// <see cref="PublicHoliday"/> 只有日曆上紅字的法定假日，不含一般週末。
/// </summary>
public enum DayKind
{
    Weekday,
    Holiday,
    PublicHoliday,
}

/// <summary>行事曆上的一天。</summary>
/// <param name="IsHoliday">週六、週日或國定假日。決定額度點數與公平性點數查表。</param>
/// <param name="IsPublicHoliday">國定假日，不含一般週末。只用於連值兩個週六的加分判斷。</param>
/// <param name="IsMakeUpWorkday">補班日，視為平日，<see cref="IsHoliday"/> 為 false。</param>
public sealed record CalendarDay(
    DateOnly Date,
    bool IsHoliday,
    bool IsPublicHoliday,
    bool IsMakeUpWorkday = false,
    string? HolidayName = null)
{
    public DayOfWeek Weekday => Date.DayOfWeek;

    /// <summary>某個日類是否涵蓋這一天。國定假日同時也是假日。</summary>
    public bool Is(DayKind kind) => kind switch
    {
        DayKind.Weekday => !IsHoliday,
        DayKind.Holiday => IsHoliday,
        DayKind.PublicHoliday => IsPublicHoliday,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>只依星期幾判斷的一天：六、日為假日，沒有國定假日。內建行事曆與測試 fixture 的起點。</summary>
    public static CalendarDay Plain(DateOnly date) =>
        new(date, IsHoliday: date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday, IsPublicHoliday: false);
}

/// <summary>
/// 每一天是平日、假日、國定假日或補班日的事實來源。
/// 查不到的日期直接擲例外——寧可失敗，也不要悄悄把國定假日當平日算點數。
/// </summary>
public sealed class Calendar
{
    private readonly IReadOnlyDictionary<DateOnly, CalendarDay> _days;

    public Calendar(IEnumerable<CalendarDay> days)
    {
        _days = days.ToDictionary(d => d.Date);
    }

    public CalendarDay this[DateOnly date] =>
        _days.TryGetValue(date, out var day)
            ? day
            : throw new KeyNotFoundException($"行事曆沒有 {date:yyyy-MM-dd} 這一天");

    public bool Covers(DateOnly date) => _days.ContainsKey(date);

    /// <summary>只有週末、沒有國定假日的行事曆，涵蓋 <paramref name="from"/> 到 <paramref name="to"/>（含）。</summary>
    public static Calendar Plain(DateOnly from, DateOnly to)
    {
        var days = new List<CalendarDay>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            days.Add(CalendarDay.Plain(d));
        }

        return new Calendar(days);
    }

    /// <summary>以週末為底、再套上指定覆寫的行事曆。</summary>
    public Calendar With(params CalendarDay[] overrides)
    {
        var merged = _days.Values.ToDictionary(d => d.Date);
        foreach (var o in overrides)
        {
            merged[o.Date] = o;
        }

        return new Calendar(merged.Values);
    }
}
