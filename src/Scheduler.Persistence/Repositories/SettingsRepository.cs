using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Mapping;

namespace Scheduler.Persistence.Repositories;

/// <summary>
/// 五份設定文件的存取。每份「整份取代」都用 <see cref="DbSetSync"/> 就地同步，
/// 主鍵相同的列保留、其餘刪、缺的補。Domain ↔ entity 的互轉全在這裡。
/// </summary>
internal sealed class SettingsRepository : ISettingsRepository
{
    private readonly SchedulerDbContext _db;

    public SettingsRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    // ---- 區域 ----

    public async Task<AreaSettings> GetAreasAsync(CancellationToken cancellationToken = default)
    {
        var types = await _db.AreaTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);
        var areas = await _db.Areas.AsNoTracking().OrderBy(a => a.SortOrder).ToListAsync(cancellationToken);
        return new AreaSettings(
            types.Select(t => new AreaType(t.Code, t.Name)).ToArray(),
            areas.Select(a => new Area(a.Id, a.Code, a.Name, a.AreaTypeCode, a.RequiredPerDay)).ToArray());
    }

    public async Task ReplaceAreasAsync(AreaSettings settings, CancellationToken cancellationToken = default)
    {
        DbSetSync.Sync(
            _db.AreaTypes,
            await _db.AreaTypes.ToListAsync(cancellationToken),
            settings.AreaTypes.Select((t, i) => new AreaTypeEntity { Code = t.Code, Name = t.Name, SortOrder = i }),
            t => t.Code,
            (from, into) =>
            {
                into.Name = from.Name;
                into.SortOrder = from.SortOrder;
            });

        DbSetSync.Sync(
            _db.Areas,
            await _db.Areas.ToListAsync(cancellationToken),
            settings.Areas.Select((a, i) => new AreaEntity
            {
                Id = a.Id,
                Code = a.Code,
                Name = a.Name,
                AreaTypeCode = a.AreaTypeCode,
                RequiredPerDay = a.RequiredPerDay,
                SortOrder = i,
            }),
            a => a.Id,
            (from, into) =>
            {
                into.Code = from.Code;
                into.Name = from.Name;
                into.AreaTypeCode = from.AreaTypeCode;
                into.RequiredPerDay = from.RequiredPerDay;
                into.SortOrder = from.SortOrder;
            });
    }

    // ---- 身分 ----

    public async Task<RankSettings> GetRanksAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _db.RankGroups.AsNoTracking().OrderBy(g => g.SortOrder).ToListAsync(cancellationToken);
        var ranks = await _db.Ranks.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(cancellationToken);
        return new RankSettings(
            groups.Select(g => new RankGroup(g.Code, g.Name)).ToArray(),
            ranks.Select(r => new Rank(r.Code, r.Name, r.GroupCode, r.QuotaCap, EnumNames.ToPointType(r.PointType))).ToArray());
    }

    public async Task ReplaceRanksAsync(RankSettings settings, CancellationToken cancellationToken = default)
    {
        DbSetSync.Sync(
            _db.RankGroups,
            await _db.RankGroups.ToListAsync(cancellationToken),
            settings.Groups.Select((g, i) => new RankGroupEntity { Code = g.Code, Name = g.Name, SortOrder = i }),
            g => g.Code,
            (from, into) =>
            {
                into.Name = from.Name;
                into.SortOrder = from.SortOrder;
            });

        DbSetSync.Sync(
            _db.Ranks,
            await _db.Ranks.ToListAsync(cancellationToken),
            settings.Ranks.Select((r, i) => new RankEntity
            {
                Code = r.Code,
                Name = r.Name,
                GroupCode = r.GroupCode,
                QuotaCap = r.QuotaCap,
                PointType = EnumNames.Of(r.PointType),
                SortOrder = i,
            }),
            r => r.Code,
            (from, into) =>
            {
                into.Name = from.Name;
                into.GroupCode = from.GroupCode;
                into.QuotaCap = from.QuotaCap;
                into.PointType = from.PointType;
                into.SortOrder = from.SortOrder;
            });
    }

    // ---- 資格矩陣 ----

    public async Task<EligibilityMatrix> GetEligibilityAsync(CancellationToken cancellationToken = default)
    {
        var cells = await _db.Eligibility.AsNoTracking()
            .OrderBy(e => e.RankCode).ThenBy(e => e.AreaTypeCode)
            .ToListAsync(cancellationToken);

        var matrix = cells
            .GroupBy(c => c.RankCode)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, bool>)g.ToDictionary(c => c.AreaTypeCode, c => c.Eligible));
        return new EligibilityMatrix(matrix);
    }

    public async Task ReplaceEligibilityAsync(EligibilityMatrix matrix, CancellationToken cancellationToken = default)
    {
        DbSetSync.Sync(
            _db.Eligibility,
            await _db.Eligibility.ToListAsync(cancellationToken),
            matrix.Matrix.SelectMany(row => row.Value.Select(cell => new EligibilityEntity
            {
                RankCode = row.Key,
                AreaTypeCode = cell.Key,
                Eligible = cell.Value,
            })),
            e => (e.RankCode, e.AreaTypeCode),
            (from, into) => into.Eligible = from.Eligible);
    }

    // ---- 點數規則 ----

    public async Task<PointRules> GetPointRulesAsync(CancellationToken cancellationToken = default)
    {
        var scalar = await _db.PointRules.AsNoTracking().SingleAsync(p => p.Id == PointRuleEntity.SingletonId, cancellationToken);
        var rows = await _db.FairnessPointTables.AsNoTracking()
            .OrderBy(r => r.PointType).ThenBy(r => r.Today).ThenBy(r => r.Tomorrow)
            .ToListAsync(cancellationToken);

        var tables = rows
            .GroupBy(r => EnumNames.ToPointType(r.PointType)!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<FairnessTableEntry>)g
                    .Select(r => new FairnessTableEntry(EnumNames.ToDayKind(r.Today), EnumNames.ToDayKind(r.Tomorrow), r.Points))
                    .ToArray());

        return new PointRules(
            new QuotaPointRule(scalar.QuotaWeekday, scalar.QuotaHoliday),
            new FairnessPointRule(tables, new ConsecutiveSaturdayBonus(scalar.SaturdayBonusPoints, scalar.SaturdayBonusWindowDays)));
    }

    public async Task ReplacePointRulesAsync(PointRules rules, CancellationToken cancellationToken = default)
    {
        var scalar = await _db.PointRules.FindAsync(new object[] { PointRuleEntity.SingletonId }, cancellationToken);
        if (scalar is null)
        {
            scalar = new PointRuleEntity { Id = PointRuleEntity.SingletonId };
            _db.PointRules.Add(scalar);
        }

        scalar.QuotaWeekday = rules.Quota.Weekday;
        scalar.QuotaHoliday = rules.Quota.Holiday;
        scalar.SaturdayBonusPoints = rules.Fairness.ConsecutiveSaturdayBonus.Points;
        scalar.SaturdayBonusWindowDays = rules.Fairness.ConsecutiveSaturdayBonus.WindowDays;

        DbSetSync.Sync(
            _db.FairnessPointTables,
            await _db.FairnessPointTables.ToListAsync(cancellationToken),
            rules.Fairness.Tables.SelectMany(t => t.Value.Select(e => new FairnessPointTableEntity
            {
                PointType = EnumNames.Of(t.Key)!,
                Today = EnumNames.Of(e.Today),
                Tomorrow = EnumNames.Of(e.Tomorrow),
                Points = e.Points,
            })),
            r => (r.PointType, r.Today, r.Tomorrow),
            (from, into) => into.Points = from.Points);
    }

    // ---- 約束 ----

    public async Task<ConstraintSettings> GetConstraintsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _db.ConstraintDefinitions.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var scopeEntries = await _db.ConstraintScopeEntries.AsNoTracking().ToListAsync(cancellationToken);
        var scopeByCode = scopeEntries.ToLookup(s => s.ConstraintCode);

        var all = definitions.Select(d => ToDomain(d, scopeByCode[d.Code])).ToArray();
        return new ConstraintSettings(
            all.Where(c => c.Severity == Severity.Hard).ToArray(),
            all.Where(c => c.Severity == Severity.Soft).ToArray());
    }

    public async Task ReplaceConstraintsAsync(ConstraintSettings settings, CancellationToken cancellationToken = default)
    {
        var all = settings.All.ToArray();

        DbSetSync.Sync(
            _db.ConstraintDefinitions,
            await _db.ConstraintDefinitions.ToListAsync(cancellationToken),
            all.Select((c, i) => new ConstraintDefinitionEntity
            {
                Code = c.Code,
                Name = c.Name,
                Primitive = EnumNames.Of(c.Primitive),
                Severity = EnumNames.Of(c.Severity),
                Enabled = c.Enabled,
                Weight = c.Weight,
                Metric = EnumNames.Of(c.Metric),
                ParamDays = c.Params.Days,
                ParamCap = c.Params.Cap,
                ParamDirection = EnumNames.Of(c.Params.Direction),
                SortOrder = i,
            }),
            c => c.Code,
            (from, into) =>
            {
                into.Name = from.Name;
                into.Primitive = from.Primitive;
                into.Severity = from.Severity;
                into.Enabled = from.Enabled;
                into.Weight = from.Weight;
                into.Metric = from.Metric;
                into.ParamDays = from.ParamDays;
                into.ParamCap = from.ParamCap;
                into.ParamDirection = from.ParamDirection;
                into.SortOrder = from.SortOrder;
            });

        DbSetSync.Sync(
            _db.ConstraintScopeEntries,
            await _db.ConstraintScopeEntries.ToListAsync(cancellationToken),
            all.SelectMany(c => ScopeEntriesOf(c.Code, c.Scope)),
            s => (s.ConstraintCode, s.Dimension, s.Value),
            (_, _) => { });
    }

    private static IEnumerable<ConstraintScopeEntryEntity> ScopeEntriesOf(string code, ConstraintScope scope)
    {
        IEnumerable<ConstraintScopeEntryEntity> Entries(string dimension, IEnumerable<string>? values) =>
            (values ?? Enumerable.Empty<string>())
                .Select(v => new ConstraintScopeEntryEntity { ConstraintCode = code, Dimension = dimension, Value = v });

        return Entries(ScopeDimension.Rank, scope.RankCodes)
            .Concat(Entries(ScopeDimension.ExemptRank, scope.ExemptRankCodes))
            .Concat(Entries(ScopeDimension.AreaType, scope.AreaTypeCodes))
            .Concat(Entries(ScopeDimension.DayKind, scope.DayKinds?.Select(EnumNames.Of)));
    }

    private static ConstraintDefinition ToDomain(ConstraintDefinitionEntity d, IEnumerable<ConstraintScopeEntryEntity> scopeEntries)
    {
        var byDimension = scopeEntries.ToLookup(s => s.Dimension, s => s.Value);

        IReadOnlySet<string>? Strings(string dimension) =>
            byDimension.Contains(dimension) ? byDimension[dimension].ToHashSet() : null;

        var scope = new ConstraintScope(
            RankCodes: Strings(ScopeDimension.Rank),
            ExemptRankCodes: Strings(ScopeDimension.ExemptRank),
            AreaTypeCodes: Strings(ScopeDimension.AreaType),
            DayKinds: byDimension.Contains(ScopeDimension.DayKind)
                ? byDimension[ScopeDimension.DayKind].Select(EnumNames.ToDayKind).ToHashSet()
                : null);

        return new ConstraintDefinition(
            d.Code,
            d.Name,
            EnumNames.ToPrimitive(d.Primitive),
            EnumNames.ToSeverity(d.Severity),
            d.Enabled,
            d.Weight,
            scope,
            EnumNames.ToMetric(d.Metric),
            new ConstraintParams(d.ParamDays, d.ParamCap, EnumNames.ToDirection(d.ParamDirection)));
    }

    // ---- 逐月覆寫 ----

    public async Task<MonthlyOverride> GetMonthlyOverrideAsync(YearMonth yearMonth, CancellationToken cancellationToken = default)
    {
        var rows = await _db.MonthlyOverrides.AsNoTracking()
            .Where(o => o.Year == yearMonth.Year && o.Month == yearMonth.Month)
            .OrderBy(o => o.RankCode)
            .ToListAsync(cancellationToken);
        return new MonthlyOverride(yearMonth, rows.ToDictionary(o => o.RankCode, o => o.QuotaCap));
    }

    public async Task ReplaceMonthlyOverrideAsync(MonthlyOverride monthlyOverride, CancellationToken cancellationToken = default)
    {
        var ym = monthlyOverride.YearMonth;
        DbSetSync.Sync(
            _db.MonthlyOverrides,
            await _db.MonthlyOverrides.Where(o => o.Year == ym.Year && o.Month == ym.Month).ToListAsync(cancellationToken),
            monthlyOverride.QuotaCapByRank.Select(kv => new MonthlyOverrideEntity
            {
                Year = ym.Year,
                Month = ym.Month,
                RankCode = kv.Key,
                QuotaCap = kv.Value,
            }),
            o => o.RankCode,
            (from, into) => into.QuotaCap = from.QuotaCap);
    }
}
