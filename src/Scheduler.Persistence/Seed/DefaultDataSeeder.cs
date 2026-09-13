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
/// 出廠資料庫會帶這份假名單，**目前沒有可執行的清除路徑**：種子在客戶機器上第一次啟動時才跑，
/// 有值班紀錄的人 <c>StaffCommands</c> 會回 <c>STAFF_HAS_DUTIES</c> 刪不掉，
/// 這裡的判空閘門也無法區分「被清空」與「全新」，清了下次啟動又會復活。
/// 正式交付前要依 #37 把這段 seed 關掉。
/// 每份設定文件各自判空、各自 seed：契約允許 PUT 空的區域清單，若共用一個閘門，
/// 使用者清空區域後下次啟動會把身分、點數、約束全部蓋回出廠值。
/// </summary>
public static class DefaultDataSeeder
{
    public static async Task SeedIfEmptyAsync(SchedulerDbContext db, CancellationToken cancellationToken = default)
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

        if (!await db.CalendarDays.AnyAsync(cancellationToken))
        {
            foreach (var day in BuiltInCalendar.Days)
            {
                await calendar.UpsertAsync(new CalendarException(day, Overridden: false), cancellationToken);
            }
        }

        if (!await db.Staff.AnyAsync(cancellationToken))
        {
            foreach (var member in ReferenceRoster.Build())
            {
                await staff.AddAsync(member, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
