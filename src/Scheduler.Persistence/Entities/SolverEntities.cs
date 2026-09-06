namespace Scheduler.Persistence.Entities;

// 求解紀錄。全部保留、不清理；只寫狀態轉換，不寫逐秒進度（ARCHITECTURE §4.8、§5）。

public sealed class SolverJobEntity
{
    public string Id { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; set; } = "";
    public int VariantCount { get; set; }
    public int TimeLimitSecPerVariant { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public double? ElapsedSec { get; set; }
    public string? FailureReason { get; set; }
    public int? ScaleStaff { get; set; }
    public int? ScaleAreas { get; set; }
    public int? ScaleDays { get; set; }
    public int? ScaleVariables { get; set; }
    public int? HardConstraintCount { get; set; }
    public int? SoftConstraintCount { get; set; }
}

public sealed class SolverJobWarningEntity
{
    public string JobId { get; set; } = "";
    public int Seq { get; set; }
    public string Message { get; set; } = "";
}

public sealed class VariantEntity
{
    public string JobId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int HardViolationCount { get; set; }
    public double SoftScore { get; set; }
    public int MetricVacancies { get; set; }
    public double MetricQuotaFairness { get; set; }
    public double MetricAreaConsistency { get; set; }
    public double MetricRankPreference { get; set; }
    public double? MetricFairnessPoint { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>變體的權重乘數，一列一條軟約束。未列出的代碼乘數為 1。</summary>
public sealed class VariantWeightEntity
{
    public string JobId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string ConstraintCode { get; set; } = "";
    public double Multiplier { get; set; }
}

public sealed class VariantDutyEntity
{
    public string JobId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string AreaId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string StaffId { get; set; } = "";
}
