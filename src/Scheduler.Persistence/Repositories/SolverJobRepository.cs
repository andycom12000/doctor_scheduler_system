using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Application.Solving;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Mapping;

namespace Scheduler.Persistence.Repositories;

internal sealed class SolverJobRepository : ISolverJobRepository
{
    private readonly SchedulerDbContext _db;

    public SolverJobRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<SolverJobRecord?> FindAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.SolverJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var warnings = await WarningsOf(new[] { jobId }, cancellationToken);
        return ToDomain(entity, warnings[jobId]);
    }

    public async Task<IReadOnlyList<SolverJobRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.SolverJobs.AsNoTracking().OrderByDescending(j => j.CreatedAt).ToListAsync(cancellationToken);
        var warnings = await WarningsOf(entities.Select(j => j.Id).ToArray(), cancellationToken);
        return entities.Select(j => ToDomain(j, warnings[j.Id])).ToArray();
    }

    public Task AddAsync(SolverJobRecord job, CancellationToken cancellationToken = default)
    {
        var entity = new SolverJobEntity { Id = job.JobId, CreatedAt = job.CreatedAt };
        CopyInto(job, entity);
        _db.SolverJobs.Add(entity);
        _db.SolverJobWarnings.AddRange(WarningEntities(job));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(SolverJobRecord job, CancellationToken cancellationToken = default)
    {
        var entity = await _db.SolverJobs.FindAsync(new object[] { job.JobId }, cancellationToken)
            ?? throw new KeyNotFoundException($"沒有 id 為 {job.JobId} 的求解工作");
        CopyInto(job, entity);

        DbSetSync.Sync(
            _db.SolverJobWarnings,
            await _db.SolverJobWarnings.Where(w => w.JobId == job.JobId).ToListAsync(cancellationToken),
            WarningEntities(job),
            w => w.Seq,
            (from, into) => into.Message = from.Message);
    }

    public async Task AddVariantAsync(VariantRecord variant, CancellationToken cancellationToken = default)
    {
        // 已落盤的 + 同一個工作單元裡剛加、還沒 commit 的，兩者都算，順序才不會撞號
        var sortOrder = await _db.Variants.CountAsync(v => v.JobId == variant.JobId, cancellationToken)
            + _db.Variants.Local.Count(v => v.JobId == variant.JobId && _db.Entry(v).State == EntityState.Added);
        _db.Variants.Add(new VariantEntity
        {
            JobId = variant.JobId,
            Id = variant.Id,
            Label = variant.Label,
            HardViolationCount = variant.HardViolationCount,
            SoftScore = variant.SoftScore,
            MetricVacancies = variant.Metrics.Vacancies,
            MetricQuotaFairness = variant.Metrics.QuotaFairness,
            MetricAreaConsistency = variant.Metrics.AreaConsistency,
            MetricRankPreference = variant.Metrics.RankPreference,
            MetricFairnessPoint = variant.Metrics.FairnessPoint,
            SortOrder = sortOrder,
        });
        _db.VariantWeights.AddRange(variant.WeightProfile.Select(kv => new VariantWeightEntity
        {
            JobId = variant.JobId,
            VariantId = variant.Id,
            ConstraintCode = kv.Key,
            Multiplier = kv.Value,
        }));
        _db.VariantDuties.AddRange(variant.Duties.Select(d => new VariantDutyEntity
        {
            JobId = variant.JobId,
            VariantId = variant.Id,
            AreaId = d.AreaId,
            Date = d.Date,
            StaffId = d.StaffId,
        }));
    }

    public async Task<IReadOnlyList<VariantRecord>> GetVariantsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var variants = await _db.Variants.AsNoTracking()
            .Where(v => v.JobId == jobId)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);
        return await HydrateAsync(variants, cancellationToken);
    }

    public async Task<VariantRecord?> FindVariantAsync(string jobId, string variantId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Variants.AsNoTracking()
            .SingleOrDefaultAsync(v => v.JobId == jobId && v.Id == variantId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var hydrated = await HydrateAsync(new[] { entity }, cancellationToken);
        return hydrated[0];
    }

    public async Task<int> FailUnfinishedAsync(string reason, DateTimeOffset finishedAt, CancellationToken cancellationToken = default)
    {
        var queued = EnumNames.Of(SolverJobStatus.Queued);
        var running = EnumNames.Of(SolverJobStatus.Running);
        var failed = EnumNames.Of(SolverJobStatus.Failed);
        var unfinished = await _db.SolverJobs
            .Where(j => j.Status == queued || j.Status == running)
            .ToListAsync(cancellationToken);

        foreach (var job in unfinished)
        {
            job.Status = failed;
            job.FailureReason = reason;
            job.FinishedAt = finishedAt;
        }

        return unfinished.Count;
    }

    // ---- helpers ----

    private async Task<IReadOnlyList<VariantRecord>> HydrateAsync(IReadOnlyList<VariantEntity> variants, CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return Array.Empty<VariantRecord>();
        }

        var jobId = variants[0].JobId;
        var ids = variants.Select(v => v.Id).ToArray();

        var weights = (await _db.VariantWeights.AsNoTracking()
                .Where(w => w.JobId == jobId && ids.Contains(w.VariantId))
                .ToListAsync(cancellationToken))
            .ToLookup(w => w.VariantId);
        var duties = (await _db.VariantDuties.AsNoTracking()
                .Where(d => d.JobId == jobId && ids.Contains(d.VariantId))
                .OrderBy(d => d.Date).ThenBy(d => d.AreaId)
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.VariantId);

        return variants.Select(v => new VariantRecord(
            v.JobId,
            v.Id,
            v.Label,
            weights[v.Id].ToDictionary(w => w.ConstraintCode, w => w.Multiplier),
            new VariantMetrics(v.MetricVacancies, v.MetricQuotaFairness, v.MetricAreaConsistency, v.MetricRankPreference, v.MetricFairnessPoint),
            v.HardViolationCount,
            v.SoftScore,
            duties[v.Id].Select(d => new Duty(d.AreaId, d.Date, d.StaffId)).ToArray())).ToArray();
    }

    private async Task<ILookup<string, string>> WarningsOf(IReadOnlyList<string> jobIds, CancellationToken cancellationToken)
    {
        var rows = await _db.SolverJobWarnings.AsNoTracking()
            .Where(w => jobIds.Contains(w.JobId))
            .OrderBy(w => w.Seq)
            .ToListAsync(cancellationToken);
        return rows.ToLookup(w => w.JobId, w => w.Message);
    }

    private static IEnumerable<SolverJobWarningEntity> WarningEntities(SolverJobRecord job) =>
        job.Warnings.Select((message, i) => new SolverJobWarningEntity { JobId = job.JobId, Seq = i, Message = message });

    private static void CopyInto(SolverJobRecord job, SolverJobEntity entity)
    {
        entity.Year = job.YearMonth.Year;
        entity.Month = job.YearMonth.Month;
        entity.Status = EnumNames.Of(job.Status);
        entity.VariantCount = job.VariantCount;
        entity.TimeLimitSecPerVariant = job.TimeLimitSecPerVariant;
        entity.StartedAt = job.StartedAt;
        entity.FinishedAt = job.FinishedAt;
        entity.ElapsedSec = job.ElapsedSec;
        entity.FailureReason = job.FailureReason;
        entity.ScaleStaff = job.Scale?.Staff;
        entity.ScaleAreas = job.Scale?.Areas;
        entity.ScaleDays = job.Scale?.Days;
        entity.ScaleVariables = job.Scale?.Variables;
        entity.HardConstraintCount = job.ConstraintCount?.Hard;
        entity.SoftConstraintCount = job.ConstraintCount?.Soft;
        entity.LastVariantIndex = job.LastVariantIndex;
        entity.LastSolutionCount = job.LastSolutionCount;
        entity.LastBestObjective = job.LastBestObjective;
        entity.LastBestBound = job.LastBestBound;
    }

    private static SolverJobRecord ToDomain(SolverJobEntity j, IEnumerable<string> warnings) =>
        new(
            j.Id,
            new YearMonth(j.Year, j.Month),
            EnumNames.ToSolverJobStatus(j.Status),
            j.VariantCount,
            j.TimeLimitSecPerVariant,
            j.CreatedAt,
            j.StartedAt,
            j.FinishedAt,
            j.ElapsedSec,
            j.FailureReason,
            warnings.ToArray(),
            j.ScaleStaff is int st && j.ScaleAreas is int ar && j.ScaleDays is int dy && j.ScaleVariables is int vr
                ? new SolverScale(st, ar, dy, vr)
                : null,
            j.HardConstraintCount is int h && j.SoftConstraintCount is int s ? new ConstraintCount(h, s) : null,
            j.LastVariantIndex,
            j.LastSolutionCount,
            j.LastBestObjective,
            j.LastBestBound);
}
