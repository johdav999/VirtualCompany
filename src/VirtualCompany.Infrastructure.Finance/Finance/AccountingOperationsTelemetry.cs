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
    private static readonly Counter<long> ProviderSwitchCutovers = Meter.CreateCounter<long>("accounting.provider_switch.cutovers");
    private static readonly Counter<long> ProviderSwitchBlocks = Meter.CreateCounter<long>("accounting.provider_switch.blocks");
    private static readonly Counter<long> ProviderSwitchReconciliations = Meter.CreateCounter<long>("accounting.provider_switch.reconciliations");
    private static readonly Counter<long> ProviderSwitchMonitoringRuns = Meter.CreateCounter<long>("accounting.provider_switch.monitoring_runs");
    private static readonly Counter<long> ProviderSwitchMonitoringViolations = Meter.CreateCounter<long>("accounting.provider_switch.monitoring_violations");
    private static readonly Histogram<double> MigrationDuration = Meter.CreateHistogram<double>("accounting.migration.duration", "ms");
    private static readonly Histogram<double> ProviderSwitchStageDuration = Meter.CreateHistogram<double>("accounting.provider_switch.stage_duration", "ms");
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

    public void ProviderSwitchCutover(Guid companyId, Guid switchId, Guid executionId, string status,
        string direction, string? providerKey, string? correlationId)
    {
        ProviderSwitchCutovers.Add(1, new("status", status), new("direction", direction),
            new("provider", providerKey ?? "internal"));
        _logger.LogInformation(
            "Accounting provider-switch cutover {ExecutionId} for switch {SwitchId} in company {CompanyId} reached {Status}. Direction={Direction}, Provider={ProviderKey}, CorrelationId={CorrelationId}.",
            executionId, switchId, companyId, status, direction, providerKey ?? "internal", correlationId);
    }

    public void ProviderSwitchBlocked(Guid companyId, Guid switchId, Guid executionId, string stage,
        string reasonCode, bool retryIsSafe, bool reconciliationRequired, string? correlationId)
    {
        ProviderSwitchBlocks.Add(1, new("stage", stage), new("reason_code", reasonCode),
            new("retry_safe", retryIsSafe), new("reconciliation_required", reconciliationRequired));
        _logger.LogWarning(
            "Accounting provider-switch cutover {ExecutionId} for switch {SwitchId} in company {CompanyId} was blocked at {Stage}. ReasonCode={ReasonCode}, RetryIsSafe={RetryIsSafe}, ReconciliationRequired={ReconciliationRequired}, CorrelationId={CorrelationId}.",
            executionId, switchId, companyId, stage, reasonCode, retryIsSafe, reconciliationRequired, correlationId);
    }

    public void ProviderSwitchReconciled(Guid companyId, Guid switchId, Guid executionId, bool passed,
        int checkCount, string? correlationId)
    {
        ProviderSwitchReconciliations.Add(1,
            new KeyValuePair<string, object?>[] { new("passed", passed) });
        _logger.LogInformation(
            "Accounting provider-switch final reconciliation for execution {ExecutionId}, switch {SwitchId}, company {CompanyId} completed. Passed={Passed}, CheckCount={CheckCount}, CorrelationId={CorrelationId}.",
            executionId, switchId, companyId, passed, checkCount, correlationId);
    }

    public void ProviderSwitchStageCompleted(string stage, TimeSpan duration, string direction, string? providerKey)
        => ProviderSwitchStageDuration.Record(duration.TotalMilliseconds, new("stage", stage),
            new("direction", direction), new("provider", providerKey ?? "internal"));

    public void ProviderSwitchMonitoring(Guid companyId, Guid switchId, Guid monitoringRunId, string status,
        int checkSequence, int violationCount, string? correlationId)
    {
        ProviderSwitchMonitoringRuns.Add(1,
            new KeyValuePair<string, object?>[] { new("status", status) });
        ProviderSwitchMonitoringViolations.Add(violationCount);
        _logger.LogInformation(
            "Accounting provider-switch monitoring {MonitoringRunId} for switch {SwitchId} in company {CompanyId} completed check {CheckSequence}. Status={Status}, Violations={ViolationCount}, CorrelationId={CorrelationId}.",
            monitoringRunId, switchId, companyId, checkSequence, status, violationCount, correlationId);
    }

    public void ProviderSwitchMonitoringFailed(Guid companyId, Guid switchId, Guid monitoringRunId,
        string failureCode, int consecutiveFailures, string? correlationId, Exception exception)
    {
        ProviderSwitchMonitoringRuns.Add(1, new("status", "failed"), new("failure_code", failureCode));
        _logger.LogError(exception,
            "Accounting provider-switch monitoring {MonitoringRunId} for switch {SwitchId} in company {CompanyId} failed. FailureCode={FailureCode}, ConsecutiveFailures={ConsecutiveFailures}, CorrelationId={CorrelationId}.",
            monitoringRunId, switchId, companyId, failureCode, consecutiveFailures, correlationId);
    }
}
