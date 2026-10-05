using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Application.Settings;
using Scheduler.Domain.Defaults;
using Scheduler.Persistence.Repositories;

namespace Scheduler.Persistence.Seed;

/// <summary>
/// 首次啟動的 seed。設定全部從 <c>Scheduler.Domain.Defaults</c> 抄（唯一來源是
/// <c>docs/constraint-defaults.md</c>），行事曆從 <see cref="BuiltInCalendar"/>，
/// 人員名冊從 <see cref="ReferenceRoster"/>（33 位醫師 + 1 位 NP 的假名單）。
/// 參考名單只在開發期需要（前端手動測試、demo）；正式發佈包由
/// <c>ApiHostOptions.SeedReferenceRoster</c>（Shell 端依 DEBUG/RELEASE 編譯期決定，見 #37）關閉，
/// <paramref name="seedReferenceRoster"/> 為 false 時完全不寫人員表，行事曆與約束等出廠設定不受影響。
/// 每份設定文件各自判空、各自 seed：契約允許 PUT 空的區域清單，若共用一個閘門，
/// 使用者清空區域後下次啟動會把身分、點數、約束全部蓋回出廠值。
/// </summary>
public static class DefaultDataSeeder
{
    public static async Task SeedIfEmptyAsync(
        SchedulerDbContext db, bool seedReferenceRoster = true, CancellationToken cancellationToken = default)
    {
        var settings = new SettingsRepository(db);
        var calendar = new CalendarRepository(db);
        var staff = new StaffRepository(db);

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

        // 逐日補缺：內建表有、資料庫沒有的日子才寫；資料庫已有的（含使用者覆寫）一律不動，
        // 這樣新版加進來的年份舊資料庫也拿得到。
        var existingDates = (await db.CalendarDays.Select(d => d.Date).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var day in BuiltInCalendar.Days.Where(d => !existingDates.Contains(d.Date)))
        {
            await calendar.UpsertAsync(new CalendarException(day, Overridden: false), cancellationToken);
        }

        if (seedReferenceRoster && !await db.Staff.AnyAsync(cancellationToken))
        {
            foreach (var member in ReferenceRoster.Build())
            {
                await staff.AddAsync(member, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
