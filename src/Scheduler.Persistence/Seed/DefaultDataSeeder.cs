using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Application.Settings;
using Scheduler.Domain.Defaults;
using Scheduler.Persistence.Repositories;

namespace Scheduler.Persistence.Seed;

/// <summary>
/// 首次啟動的 seed。設定全部從 <c>Scheduler.Domain.Defaults</c> 抄（唯一來源是
/// <c>docs/constraint-defaults.md</c>），行事曆從 <see cref="BuiltInCalendar"/>。
/// 人員名冊不 seed——真實名單由排班者在人員頁建立。
/// 「空」的判斷以區域類型表與行事曆表為準，各自獨立；使用者清空自己的設定不會被重新 seed，
/// 因為 PUT 整份取代不會讓表變空。
/// </summary>
public static class DefaultDataSeeder
{
    public static async Task SeedIfEmptyAsync(SchedulerDbContext db, CancellationToken cancellationToken = default)
    {
        var settings = new SettingsRepository(db);
        var calendar = new CalendarRepository(db);

        if (!await db.AreaTypes.AnyAsync(cancellationToken))
        {
            await settings.ReplaceAreasAsync(new AreaSettings(DefaultAreas.AreaTypes, DefaultAreas.Areas), cancellationToken);
            await settings.ReplaceRanksAsync(new RankSettings(DefaultRanks.Groups, DefaultRanks.Ranks), cancellationToken);
            await settings.ReplaceEligibilityAsync(DefaultRanks.Eligibility, cancellationToken);
            await settings.ReplacePointRulesAsync(DefaultPointRules.Rules, cancellationToken);
            await settings.ReplaceConstraintsAsync(DefaultConstraints.Settings, cancellationToken);
        }

        if (!await db.CalendarDays.AnyAsync(cancellationToken))
        {
            foreach (var day in BuiltInCalendar.Days)
            {
                await calendar.UpsertAsync(new CalendarException(day, Overridden: false), cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
