using Scheduler.Domain.Model;

namespace Scheduler.Persistence.Seed;

/// <summary>
/// 內建的行事曆例外日。2026、2027 年。2026 依人事總處「115 年政府行政機關辦公日曆表」
/// （https://www.dgpa.gov.tw/information?uid=82&amp;pid=12574），2027 依「116 年政府行政機關辦公日曆表」
/// （院授人培字第 1153026132 號，https://www.dgpa.gov.tw/information?uid=30&amp;pid=12982）。2026 起放假日逢例假日改為補假、
/// 不再調移上班，所以 2026 沒有補班日。與 <c>frontend/src/mocks/fixtures/calendar.ts</c> 逐日相同。
///
/// 落在週六／週日的國定假日照樣列進來——它同時是「假日」也是「國定假日」，
/// 連值週六加分的排除條件看的是後者。2028 年起先由使用者逐日覆寫。
/// </summary>
public static class BuiltInCalendar
{
    public static IReadOnlyList<CalendarDay> Days { get; } = new[]
    {
        PublicHoliday(2026, 1, 1, "元旦"),
        PublicHoliday(2026, 2, 15, "小年夜"),
        PublicHoliday(2026, 2, 16, "除夕"),
        PublicHoliday(2026, 2, 17, "春節"),
        PublicHoliday(2026, 2, 18, "春節"),
        PublicHoliday(2026, 2, 19, "春節"),
        PublicHoliday(2026, 2, 20, "小年夜補假"),
        PublicHoliday(2026, 2, 27, "和平紀念日補假"),
        PublicHoliday(2026, 2, 28, "和平紀念日"),
        PublicHoliday(2026, 4, 3, "兒童節補假"),
        PublicHoliday(2026, 4, 4, "兒童節"),
        PublicHoliday(2026, 4, 5, "清明節"),
        PublicHoliday(2026, 4, 6, "清明節補假"),
        PublicHoliday(2026, 5, 1, "勞動節"),
        PublicHoliday(2026, 6, 19, "端午節"),
        PublicHoliday(2026, 9, 25, "中秋節"),
        PublicHoliday(2026, 9, 28, "教師節"),
        PublicHoliday(2026, 10, 9, "國慶日補假"),
        PublicHoliday(2026, 10, 10, "國慶日"),
        PublicHoliday(2026, 10, 25, "臺灣光復暨金門古寧頭大捷紀念日"),
        PublicHoliday(2026, 10, 26, "光復節補假"),
        PublicHoliday(2026, 12, 25, "行憲紀念日"),
        PublicHoliday(2027, 1, 1, "元旦"),
        PublicHoliday(2027, 2, 4, "小年夜"),
        PublicHoliday(2027, 2, 5, "除夕"),
        PublicHoliday(2027, 2, 6, "春節"),
        PublicHoliday(2027, 2, 7, "春節"),
        PublicHoliday(2027, 2, 8, "春節"),
        PublicHoliday(2027, 2, 9, "春節補假"),
        PublicHoliday(2027, 2, 10, "春節補假"),
        PublicHoliday(2027, 2, 28, "和平紀念日"),
        PublicHoliday(2027, 3, 1, "和平紀念日補假"),
        PublicHoliday(2027, 4, 4, "兒童節"),
        PublicHoliday(2027, 4, 5, "清明節"),
        PublicHoliday(2027, 4, 6, "兒童節補假"),
        PublicHoliday(2027, 4, 30, "勞動節補假"),
        PublicHoliday(2027, 5, 1, "勞動節"),
        PublicHoliday(2027, 6, 9, "端午節"),
        PublicHoliday(2027, 9, 15, "中秋節"),
        PublicHoliday(2027, 9, 28, "教師節"),
        PublicHoliday(2027, 10, 10, "國慶日"),
        PublicHoliday(2027, 10, 11, "國慶日補假"),
        PublicHoliday(2027, 10, 25, "臺灣光復暨金門古寧頭大捷紀念日"),
        PublicHoliday(2027, 12, 24, "行憲紀念日補假"),
        PublicHoliday(2027, 12, 25, "行憲紀念日"),
        PublicHoliday(2027, 12, 31, "元旦補假（117 年元旦逢週六）"),
    };

    private static CalendarDay PublicHoliday(int year, int month, int day, string name) =>
        new(new DateOnly(year, month, day), IsHoliday: true, IsPublicHoliday: true, IsMakeUpWorkday: false, HolidayName: name);
}
