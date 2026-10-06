using Scheduler.Domain.Model;

namespace Scheduler.Application.Persistence;

/// <summary>行事曆上的一個例外日，以及它是內建的還是使用者覆寫的。</summary>
public sealed record CalendarException(CalendarDay Day, bool Overridden);

/// <summary>
/// 行事曆只存例外日：國定假日、補班日、使用者覆寫（ARCHITECTURE §5）。
/// 週六日不在資料庫裡，由讀取端以 <see cref="CalendarDay.Plain"/> 為底再套例外。
/// </summary>
public interface ICalendarRepository
{
    /// <summary><paramref name="from"/> 到 <paramref name="to"/>（含）之間的例外日，依日期遞增。</summary>
    Task<IReadOnlyList<CalendarException>> GetExceptionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<CalendarException?> FindAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>新增或整筆覆寫某一天。</summary>
    Task UpsertAsync(CalendarException exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自動更新專用：只在該日不是使用者覆寫時才新增／覆寫，回傳有沒有寫。寫入前再確認一次
    /// <c>Overridden</c>，擋掉比對與寫入之間使用者剛好覆寫的競態（#112）。
    /// </summary>
    Task<bool> UpsertIfNotOverriddenAsync(CalendarException exception, CancellationToken cancellationToken = default);

    /// <summary>自動更新專用：只在該日不是使用者覆寫時才移除，回傳有沒有移除。</summary>
    Task<bool> RemoveIfNotOverriddenAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>移除某一天的例外，讓它回到「只看星期幾」。不存在時不動作。</summary>
    Task RemoveAsync(DateOnly date, CancellationToken cancellationToken = default);
}
