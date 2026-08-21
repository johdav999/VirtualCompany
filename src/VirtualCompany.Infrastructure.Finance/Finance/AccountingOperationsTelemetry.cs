using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingOperationsTelemetry
{
    internal const string MeterName = "VirtualCompany.Accounting";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> MigrationRuns = Meter.CreateCounter<long>("accounting.migration.runs");
    private static readonly Counter<long> MigrationRecords = Meter.CreateCounter<long>("accounting.migration.records");
    private static readonly Counter<long> MigrationConflicts = Meter.CreateCounter<long>("accounting.migration.conflicts");
    private static readonly Counter<long> RecoveryVerifications = Meter.CreateCounter<long>("accounting.recovery.verifications");
    private static readonly Histogram<double> MigrationDuration = Meter.CreateHistogram<double>("accounting.migration.duration", "ms");
    private readonly ILogger<AccountingOperationsTelemetry> _logger;

    public AccountingOperationsTelemetry(ILogger<AccountingOperationsTelemetry> logger) => _logger = logger;

    public void MigrationStarted(Guid companyId, Guid runId, string? correlationId)
    {
        MigrationRuns.Add(1, new KeyValuePair<string, object?>[] { new("status", "started") });
        _logger.LogInformation(
            "Accounting migration {MigrationRunId} started for company {CompanyId}. CorrelationId={CorrelationId}.",
            runId, companyId, correlationId);
    }

    public void MigrationBatch(Guid companyId, Guid runId, string phase, int scanned, int updated, int conflicts)
    {
        MigrationRecords.Add(scanned, new KeyValuePair<string, object?>[] { new("phase", phase) });
        MigrationConflicts.Add(conflicts, new KeyValuePair<string, object?>[] { new("phase", phase) });
        _logger.LogInformation(
            "Accounting migration {MigrationRunId} processed a {Phase} batch for company {CompanyId}. Scanned={ScannedCount}, Updated={UpdatedCount}, Conflicts={ConflictCount}.",
            runId, phase, companyId, scanned, updated, conflicts);
    }

    public void MigrationCompleted(Guid companyId, Guid runId, string status, TimeSpan duration, int conflicts, string? correlationId)
    {
        MigrationRuns.Add(1, new KeyValuePair<string, object?>[] { new("status", status) });
        MigrationDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>[] { new("status", status) });
        _logger.LogInformation(
            "Accounting migration {MigrationRunId} completed for company {CompanyId} with status {Status} and {ConflictCount} conflicts after {DurationMs} ms. CorrelationId={CorrelationId}.",
            runId, companyId, status, conflicts, duration.TotalMilliseconds, correlationId);
    }

    public void MigrationConflict(Guid companyId, Guid runId, string reasonCode, string entityType,
        string entityId, string? correlationId)
    {
        MigrationConflicts.Add(1, new KeyValuePair<string, object?>[] { new("reason_code", reasonCode) });
        _logger.LogWarning(
            "Accounting migration {MigrationRunId} recorded conflict {ReasonCode} for {EntityType} {EntityId} in company {CompanyId}. CorrelationId={CorrelationId}.",
            runId, reasonCode, entityType, entityId, companyId, correlationId);
    }

    public void MigrationFailed(Guid companyId, Guid runId, string failureCode, string? correlationId, Exception exception)
    {
        MigrationRuns.Add(1, new("status", "failed"), new("failure_code", failureCode));
        _logger.LogError(exception,
            "Accounting migration {MigrationRunId} failed for company {CompanyId}. FailureCode={FailureCode}, CorrelationId={CorrelationId}.",
            runId, companyId, failureCode, correlationId);
    }

    public void MigrationLeaseExhausted(Guid companyId, Guid runId, string failureCode, string? correlationId)
    {
        MigrationRuns.Add(1, new("status", "failed"), new("failure_code", failureCode));
        _logger.LogError(
            "Accounting migration {MigrationRunId} exhausted its lease recovery attempts for company {CompanyId}. FailureCode={FailureCode}, CorrelationId={CorrelationId}.",
            runId, companyId, failureCode, correlationId);
    }

    public void RecoveryVerified(Guid companyId, Guid? periodId, bool isValid, int issueCount, bool objectContentVerified, string? correlationId)
    {
        RecoveryVerifications.Add(1, new("valid", isValid), new("objects_verified", objectContentVerified));
        _logger.LogInformation(
            "Accounting recovery verification completed for company {CompanyId} and period {FiscalPeriodId}. Valid={IsValid}, IssueCount={IssueCount}, ObjectContentVerified={ObjectContentVerified}, CorrelationId={CorrelationId}.",
            companyId, periodId, isValid, issueCount, objectContentVerified, correlationId);
    }
}
