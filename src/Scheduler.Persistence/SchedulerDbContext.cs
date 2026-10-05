using System.Text;
using Microsoft.EntityFrameworkCore;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Mapping;

namespace Scheduler.Persistence;

/// <summary>
/// 唯一的 DbContext。表名與欄名一律 snake_case（<see cref="ApplySnakeCaseNames"/>），
/// 不開 lazy loading，每個查詢明寫要載什麼（ARCHITECTURE §5）。
/// Schema 的真相是 Migrations/，這裡的設定只是它的來源。
/// </summary>
public sealed class SchedulerDbContext : DbContext
{
    public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options)
        : base(options)
    {
    }

    public DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();
    public DbSet<DutyEntity> Duties => Set<DutyEntity>();
    public DbSet<BlockedDayEntity> BlockedDays => Set<BlockedDayEntity>();
    public DbSet<CarryOverEntity> CarryOvers => Set<CarryOverEntity>();
    public DbSet<CarryOverAppliedEntity> CarryOversApplied => Set<CarryOverAppliedEntity>();

    public DbSet<StaffEntity> Staff => Set<StaffEntity>();
    public DbSet<AppMetaEntity> AppMeta => Set<AppMetaEntity>();
    public DbSet<AreaTypeEntity> AreaTypes => Set<AreaTypeEntity>();
    public DbSet<AreaEntity> Areas => Set<AreaEntity>();
    public DbSet<RankGroupEntity> RankGroups => Set<RankGroupEntity>();
    public DbSet<RankEntity> Ranks => Set<RankEntity>();
    public DbSet<EligibilityEntity> Eligibility => Set<EligibilityEntity>();
    public DbSet<PointRuleEntity> PointRules => Set<PointRuleEntity>();
    public DbSet<FairnessPointTableEntity> FairnessPointTables => Set<FairnessPointTableEntity>();
    public DbSet<ConstraintDefinitionEntity> ConstraintDefinitions => Set<ConstraintDefinitionEntity>();
    public DbSet<ConstraintScopeEntryEntity> ConstraintScopeEntries => Set<ConstraintScopeEntryEntity>();
    public DbSet<MonthlyOverrideEntity> MonthlyOverrides => Set<MonthlyOverrideEntity>();
    public DbSet<CalendarDayEntity> CalendarDays => Set<CalendarDayEntity>();

    public DbSet<SolverJobEntity> SolverJobs => Set<SolverJobEntity>();
    public DbSet<SolverJobWarningEntity> SolverJobWarnings => Set<SolverJobWarningEntity>();
    public DbSet<VariantEntity> Variants => Set<VariantEntity>();
    public DbSet<VariantWeightEntity> VariantWeights => Set<VariantWeightEntity>();
    public DbSet<VariantDutyEntity> VariantDuties => Set<VariantDutyEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 時間戳一律 UTC ISO-8601 字串，理由見 UtcTimestampConverter
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTimestampConverter>().HaveMaxLength(28);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ---- 值班表 ----
        modelBuilder.Entity<ScheduleEntity>(e =>
        {
            e.HasKey(x => new { x.Year, x.Month });
            e.Property(x => x.Status).HasMaxLength(16);
        });

        modelBuilder.Entity<DutyEntity>(e =>
        {
            e.HasKey(x => new { x.Year, x.Month, x.AreaId, x.Date });
            e.HasOne<ScheduleEntity>().WithMany().HasForeignKey(x => new { x.Year, x.Month }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StaffId);
            e.HasIndex(x => x.Date);
            // 同人同日兩區是允許存的中間狀態（違規 X1，發布與匯出時才擋，#68），
            // 所以刻意不建 (date, staff_id) 唯一索引。
        });

        modelBuilder.Entity<BlockedDayEntity>(e =>
        {
            e.HasKey(x => new { x.StaffId, x.Date });
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<CarryOverEntity>(e =>
        {
            e.HasKey(x => new { x.Year, x.Month, x.StaffId });
            e.HasOne<ScheduleEntity>().WithMany().HasForeignKey(x => new { x.Year, x.Month }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CarryOverAppliedEntity>(e =>
        {
            e.HasKey(x => new { x.Year, x.Month, x.StaffId });
            e.HasOne<ScheduleEntity>().WithMany().HasForeignKey(x => new { x.Year, x.Month }).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- 人員與設定 ----
        modelBuilder.Entity<StaffEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EmployeeNo).IsUnique();
            e.Property(x => x.Status).HasMaxLength(16);
        });

        modelBuilder.Entity<AppMetaEntity>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });

        modelBuilder.Entity<AreaTypeEntity>(e => e.HasKey(x => x.Code));

        modelBuilder.Entity<AreaEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<AreaTypeEntity>().WithMany().HasForeignKey(x => x.AreaTypeCode).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RankGroupEntity>(e => e.HasKey(x => x.Code));

        modelBuilder.Entity<RankEntity>(e =>
        {
            e.HasKey(x => x.Code);
            e.HasOne<RankGroupEntity>().WithMany().HasForeignKey(x => x.GroupCode).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.PointType).HasMaxLength(8);
        });

        // 資格矩陣是獨立的一份 PUT，不外鍵到 rank / area_type：
        // 身分或區域類型被刪時的 RANK_IN_USE / AREA_TYPE_IN_USE 由 Application 判斷。
        modelBuilder.Entity<EligibilityEntity>(e => e.HasKey(x => new { x.RankCode, x.AreaTypeCode }));

        modelBuilder.Entity<PointRuleEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<FairnessPointTableEntity>(e =>
        {
            e.HasKey(x => new { x.PointType, x.Today, x.Tomorrow });
            e.Property(x => x.PointType).HasMaxLength(8);
            e.Property(x => x.Today).HasMaxLength(16);
            e.Property(x => x.Tomorrow).HasMaxLength(16);
        });

        modelBuilder.Entity<ConstraintDefinitionEntity>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Primitive).HasMaxLength(32);
            e.Property(x => x.Severity).HasMaxLength(8);
            e.Property(x => x.Metric).HasMaxLength(32);
            e.Property(x => x.ParamDirection).HasMaxLength(16);
        });

        modelBuilder.Entity<ConstraintScopeEntryEntity>(e =>
        {
            e.HasKey(x => new { x.ConstraintCode, x.Dimension, x.Value });
            e.Property(x => x.Dimension).HasMaxLength(16);
            e.HasOne<ConstraintDefinitionEntity>().WithMany().HasForeignKey(x => x.ConstraintCode).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonthlyOverrideEntity>(e => e.HasKey(x => new { x.Year, x.Month, x.RankCode }));

        modelBuilder.Entity<CalendarDayEntity>(e =>
        {
            e.HasKey(x => x.Date);
            e.Property(x => x.HolidayName).HasMaxLength(64);
        });

        // ---- 求解紀錄 ----
        modelBuilder.Entity<SolverJobEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Status).HasMaxLength(16);
        });

        modelBuilder.Entity<SolverJobWarningEntity>(e =>
        {
            e.HasKey(x => new { x.JobId, x.Seq });
            e.HasOne<SolverJobEntity>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VariantEntity>(e =>
        {
            e.HasKey(x => new { x.JobId, x.Id });
            e.HasOne<SolverJobEntity>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VariantWeightEntity>(e =>
        {
            e.HasKey(x => new { x.JobId, x.VariantId, x.ConstraintCode });
            e.HasOne<VariantEntity>().WithMany().HasForeignKey(x => new { x.JobId, x.VariantId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VariantDutyEntity>(e =>
        {
            e.HasKey(x => new { x.JobId, x.VariantId, x.AreaId, x.Date });
            e.HasOne<VariantEntity>().WithMany().HasForeignKey(x => new { x.JobId, x.VariantId }).OnDelete(DeleteBehavior.Cascade);
        });

        ApplySnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// 表名：entity 類別名去掉 <c>Entity</c> 後綴轉 snake_case；欄名：屬性名轉 snake_case。
    /// 集中在一處，不逐欄寫 <c>HasColumnName</c>。
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrName = entity.ClrType.Name;
            if (clrName.EndsWith("Entity", StringComparison.Ordinal))
            {
                clrName = clrName[..^"Entity".Length];
            }

            entity.SetTableName(ToSnakeCase(clrName));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
