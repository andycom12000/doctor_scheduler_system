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
/// 每份設定文件各自判空、各自 seed：契約允許 PUT 空的區域清單，若共用一個閘門，
/// 使用者清空區域後下次啟動會把身分、點數、約束全部蓋回出廠值。
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
        }

        if (!await db.RankGroups.AnyAsync(cancellationToken))
        {
            await settings.ReplaceRanksAsync(new RankSettings(DefaultRanks.Groups, DefaultRanks.Ranks), cancellationToken);
        }

        if (!await db.Eligibility.AnyAsync(cancellationToken))
        {
            await settings.ReplaceEligibilityAsync(DefaultRanks.Eligibility, cancellationToken);
        }

        if (!await db.PointRules.AnyAsync(cancellationToken))
        {
            await settings.ReplacePointRulesAsync(DefaultPointRules.Rules, cancellationToken);
        }

        if (!await db.ConstraintDefinitions.AnyAsync(cancellationToken))
        {
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
