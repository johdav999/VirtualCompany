using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceSeedBackfillRun
{
    private const int ConfigurationSnapshotMaxLength = 4000;
    private const int ErrorDetailsMaxLength = 2000;

    private FinanceSeedBackfillRun()
    {
    }

    public FinanceSeedBackfillRun(Guid id, DateTime startedUtc, string configurationSnapshotJson)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        StartedUtc = NormalizeUtc(startedUtc, nameof(startedUtc));
        Status = FinanceSeedBackfillRunStatus.Running;
        ConfigurationSnapshotJson = NormalizeRequired(configurationSnapshotJson, nameof(configurationSnapshotJson), ConfigurationSnapshotMaxLength);
    }

    public Guid Id { get; private set; }
    public FinanceSeedBackfillRunStatus Status { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public int ScannedCount { get; private set; }
    public int QueuedCount { get; private set; }
    public int SucceededCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string ConfigurationSnapshotJson { get; private set; } = null!;
    public string? ErrorDetails { get; private set; }
    public ICollection<FinanceSeedBackfillAttempt> Attempts { get; } = new List<FinanceSeedBackfillAttempt>();

    public void UpdateProgress(int scannedCount, int queuedCount, int succeededCount, int skippedCount, int failedCount)
    {
        ScannedCount = ValidateCount(scannedCount, nameof(scannedCount));
        QueuedCount = ValidateCount(queuedCount, nameof(queuedCount));
        SucceededCount = ValidateCount(succeededCount, nameof(succeededCount));
        SkippedCount = ValidateCount(skippedCount, nameof(skippedCount));
        FailedCount = ValidateCount(failedCount, nameof(failedCount));
    }

    public void MarkCompleted(DateTime completedUtc, bool completedWithErrors)
    {
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        Status = completedWithErrors
            ? FinanceSeedBackfillRunStatus.CompletedWithErrors
            : FinanceSeedBackfillRunStatus.Completed;
        ErrorDetails = completedWithErrors && string.IsNullOrWhiteSpace(ErrorDetails)
            ? "One or more finance seed backfill attempts failed."
            : ErrorDetails;
    }

    public void MarkFailed(DateTime completedUtc, string? errorDetails)
    {
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        Status = FinanceSeedBackfillRunStatus.Failed;
        ErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), ErrorDetailsMaxLength);
    }

    public void SetErrorDetails(string? errorDetails)
    {
        ErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), ErrorDetailsMaxLength);
    }

    private static int ValidateCount(int value, string name) =>
        value < 0
            ? throw new ArgumentOutOfRangeException(name, "Backfill counts cannot be negative.")
            : value;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

public sealed class FinanceSeedBackfillAttempt
{
    private const int SkipReasonMaxLength = 256;
    private const int ErrorDetailsMaxLength = 2000;
    private const int IdempotencyKeyMaxLength = 200;

    private FinanceSeedBackfillAttempt()
    {
    }

    public FinanceSeedBackfillAttempt(
        Guid id,
        Guid runId,
        Guid companyId,
        DateTime startedUtc,
        FinanceSeedingState seedStateBefore)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId is required.", nameof(runId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RunId = runId;
        CompanyId = companyId;
        StartedUtc = NormalizeUtc(startedUtc, nameof(startedUtc));
        SeedStateBefore = seedStateBefore;
        Status = FinanceSeedBackfillAttemptStatus.Queued;
    }

    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? BackgroundExecutionId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public FinanceSeedBackfillAttemptStatus Status { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public string? SkipReason { get; private set; }
    public string? ErrorDetails { get; private set; }
    public FinanceSeedingState SeedStateBefore { get; private set; }
    public FinanceSeedingState? SeedStateAfter { get; private set; }
    public FinanceSeedBackfillRun Run { get; private set; } = null!;
    public Company Company { get; private set; } = null!;

    public void MarkQueued(Guid backgroundExecutionId, string idempotencyKey, DateTime completedUtc, FinanceSeedingState seedStateAfter)
    {
        BackgroundExecutionId = backgroundExecutionId == Guid.Empty
            ? throw new ArgumentException("Background execution id is required.", nameof(backgroundExecutionId))
            : backgroundExecutionId;
        IdempotencyKey = NormalizeRequired(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
        Status = FinanceSeedBackfillAttemptStatus.Queued;
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        SkipReason = null;
        ErrorDetails = null;
        SeedStateAfter = seedStateAfter;
    }

    public void MarkSkipped(DateTime completedUtc, string skipReason, FinanceSeedingState seedStateAfter)
    {
        Status = FinanceSeedBackfillAttemptStatus.Skipped;
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        SkipReason = NormalizeRequired(skipReason, nameof(skipReason), SkipReasonMaxLength);
        ErrorDetails = null;
        SeedStateAfter = seedStateAfter;
    }

    public void MarkInProgress(DateTime startedUtc, FinanceSeedingState seedStateAfter)
    {
        Status = FinanceSeedBackfillAttemptStatus.InProgress;
        StartedUtc = NormalizeUtc(startedUtc, nameof(startedUtc));
        CompletedUtc = null;
        SkipReason = null;
        SeedStateAfter = seedStateAfter;
    }

    public void MarkSucceeded(DateTime completedUtc, FinanceSeedingState seedStateAfter)
    {
        Status = FinanceSeedBackfillAttemptStatus.Succeeded;
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        ErrorDetails = null;
        SkipReason = null;
        SeedStateAfter = seedStateAfter;
    }

    public void MarkFailed(DateTime completedUtc, string? errorDetails, FinanceSeedingState seedStateAfter)
    {
        Status = FinanceSeedBackfillAttemptStatus.Failed;
        CompletedUtc = NormalizeUtc(completedUtc, nameof(completedUtc));
        ErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), ErrorDetailsMaxLength);
        SkipReason = null;
        SeedStateAfter = seedStateAfter;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, name, maxLength);

    private static DateTime NormalizeUtc(DateTime value, string name) =>
        value == default
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
}