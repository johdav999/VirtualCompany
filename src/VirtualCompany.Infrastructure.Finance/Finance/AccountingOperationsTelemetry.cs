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
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("accounting.operation.duration", "ms");
    private static readonly Counter<long> ServiceObjectiveBreaches = Meter.CreateCounter<long>("accounting.slo.breaches");
    private static readonly Histogram<double> WorkerQueueAge = Meter.CreateHistogram<double>("accounting.worker.queue_age", "min");
    private static readonly Histogram<double> ProviderSyncLag = Meter.CreateHistogram<double>("accounting.provider.sync_lag", "min");
    private static readonly Histogram<long> ReconciliationBacklog = Meter.CreateHistogram<long>("accounting.reconciliation.backlog", "items");
    private static readonly Histogram<long> ExpiredExportBytes = Meter.CreateHistogram<long>("accounting.export.expired_content", "By");
    private static readonly Histogram<long> ObjectFailures = Meter.CreateHistogram<long>("accounting.object.failures", "items");
    private static readonly Counter<long> RetentionCleanupOutcomes = Meter.CreateCounter<long>("accounting.cleanup.outcomes");
    private static readonly Counter<long> RetentionCleanupItems = Meter.CreateCounter<long>("accounting.cleanup.items");
    private static readonly Counter<long> RetentionCleanupBytes = Meter.CreateCounter<long>("accounting.cleanup.bytes", "By");
    private static readonly Counter<long> StatutoryProfileChanges = Meter.CreateCounter<long>("accounting.statutory_profile.changes");
    private static readonly Counter<long> BlockedTaxDecisions = Meter.CreateCounter<long>("accounting.tax_decision.blocks");
    private static readonly Counter<long> StatutoryDocumentSeriesChanges = Meter.CreateCounter<long>("accounting.statutory_document.series_changes");
    private static readonly Counter<long> StatutoryDocumentAllocations = Meter.CreateCounter<long>("accounting.statutory_document.allocations");
    private static readonly Counter<long> StatutoryDocumentRegistrations = Meter.CreateCounter<long>("accounting.statutory_document.registrations");
    private static readonly Counter<long> StatutoryDocumentBlocks = Meter.CreateCounter<long>("accounting.statutory_document.blocks");
    private static readonly Counter<long> VatReturnCalculations = Meter.CreateCounter<long>("accounting.vat_return.calculations");
    private static readonly Counter<long> VatReturnFinalizations = Meter.CreateCounter<long>("accounting.vat_return.finalizations");
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

    public void OperationCompleted(Guid companyId, string operation, TimeSpan duration, double budgetMilliseconds,
        string outcome)
    {
        var status = duration.TotalMilliseconds <= budgetMilliseconds ? "within_objective" : "breached";
        OperationDuration.Record(duration.TotalMilliseconds, new("operation", operation), new("outcome", outcome),
            new("status", status));
        if (status == "breached")
        {
            ServiceObjectiveBreaches.Add(1,
                new KeyValuePair<string, object?>[] { new("operation", operation) });
            _logger.LogWarning(
                "Accounting operation {Operation} breached its service objective for company {CompanyId}. DurationMs={DurationMs}, BudgetMs={BudgetMs}, Outcome={Outcome}.",
                operation, companyId, duration.TotalMilliseconds, budgetMilliseconds, outcome);
        }
    }

    public void CapacityObserved(Guid companyId, string profile, decimal queueAgeMinutes,
        decimal syncLagMinutes, long reconciliationBacklog, long expiredExportBytes,
        long objectFailures, int alertCount)
    {
        WorkerQueueAge.Record((double)queueAgeMinutes,
            new KeyValuePair<string, object?>[] { new("profile", profile) });
        ProviderSyncLag.Record((double)syncLagMinutes,
            new KeyValuePair<string, object?>[] { new("profile", profile) });
        ReconciliationBacklog.Record(reconciliationBacklog,
            new KeyValuePair<string, object?>[] { new("profile", profile) });
        ExpiredExportBytes.Record(expiredExportBytes,
            new KeyValuePair<string, object?>[] { new("profile", profile) });
        ObjectFailures.Record(objectFailures,
            new KeyValuePair<string, object?>[] { new("object", "accounting_export") });
        if (alertCount > 0)
        {
            ServiceObjectiveBreaches.Add(alertCount,
                new KeyValuePair<string, object?>[] { new("operation", "capacity_snapshot") });
            _logger.LogWarning(
                "Accounting capacity snapshot for company {CompanyId} and profile {Profile} contains {AlertCount} remediation signals. QueueAgeMinutes={QueueAgeMinutes}, SyncLagMinutes={SyncLagMinutes}, ReconciliationBacklog={ReconciliationBacklog}, ExpiredExportBytes={ExpiredExportBytes}, ObjectFailures={ObjectFailures}.",
                companyId, profile, alertCount, queueAgeMinutes, syncLagMinutes, reconciliationBacklog,
                expiredExportBytes, objectFailures);
        }
    }

    public void RetentionCleanup(Guid companyId, int processedCount, long releasedBytes, string outcome)
    {
        RetentionCleanupOutcomes.Add(1, new("retention_class", "generated_exports"), new("outcome", outcome));
        RetentionCleanupItems.Add(processedCount,
            new KeyValuePair<string, object?>[] { new("retention_class", "generated_exports") });
        RetentionCleanupBytes.Add(releasedBytes,
            new KeyValuePair<string, object?>[] { new("retention_class", "generated_exports") });
        _logger.LogInformation(
            "Accounting retention cleanup telemetry recorded for company {CompanyId}. Outcome={Outcome}, Processed={ProcessedCount}, ReleasedBytes={ReleasedBytes}.",
            companyId, outcome, processedCount, releasedBytes);
    }

    public void StatutoryProfileChanged(Guid companyId, string operation, bool formatComplete,
        bool userAttested, string verificationStatus, string? correlationId)
    {
        StatutoryProfileChanges.Add(1, new("operation", operation), new("format_complete", formatComplete),
            new("user_attested", userAttested), new("verification_status", verificationStatus));
        _logger.LogInformation(
            "Statutory profile {Operation} for company {CompanyId}. FormatComplete={FormatComplete}, UserAttested={UserAttested}, VerificationStatus={VerificationStatus}, CorrelationId={CorrelationId}.",
            operation, companyId, formatComplete, userAttested, verificationStatus, correlationId);
    }

    public void TaxDecisionBlocked(Guid companyId, string direction, string reasonCode,
        string policyPackKey, string policyPackVersion)
    {
        BlockedTaxDecisions.Add(1, new("direction", direction), new("reason_code", reasonCode),
            new("policy_pack", policyPackKey), new("policy_version", policyPackVersion));
        _logger.LogWarning(
            "Tax decision blocked for company {CompanyId}. Direction={Direction}, ReasonCode={ReasonCode}, PolicyPack={PolicyPackKey}, PolicyVersion={PolicyPackVersion}.",
            companyId, direction, reasonCode, policyPackKey, policyPackVersion);
    }

    public void StatutoryDocumentSeriesChanged(Guid companyId, string operation, string documentType, string? correlationId)
    {
        StatutoryDocumentSeriesChanges.Add(1, new("operation", operation), new("document_type", documentType));
        _logger.LogInformation("Statutory document series {Operation} for company {CompanyId}. DocumentType={DocumentType}, CorrelationId={CorrelationId}.", operation, companyId, documentType, correlationId);
    }

    public void StatutoryDocumentNumberAllocated(Guid companyId, string documentType, string outcome, string? correlationId)
    {
        StatutoryDocumentAllocations.Add(1, new("document_type", documentType), new("outcome", outcome));
        _logger.LogInformation("Statutory document number allocation recorded for company {CompanyId}. DocumentType={DocumentType}, Outcome={Outcome}, CorrelationId={CorrelationId}.", companyId, documentType, outcome, correlationId);
    }

    public void StatutoryDocumentRegistered(Guid companyId, string documentType, string authority, string? correlationId)
    {
        StatutoryDocumentRegistrations.Add(1, new("document_type", documentType), new("authority", authority));
        _logger.LogInformation("Statutory document registered for company {CompanyId}. DocumentType={DocumentType}, Authority={Authority}, CorrelationId={CorrelationId}.", companyId, documentType, authority, correlationId);
    }

    public void StatutoryDocumentBlocked(Guid companyId, string documentType, string reasonCode)
    {
        StatutoryDocumentBlocks.Add(1, new("document_type", documentType), new("reason_code", reasonCode));
        _logger.LogWarning("Statutory document decision blocked for company {CompanyId}. DocumentType={DocumentType}, ReasonCode={ReasonCode}.", companyId, documentType, reasonCode);
    }

    public void VatReturnCalculated(Guid companyId, string status, int sourceCount, int issueCount)
    {
        VatReturnCalculations.Add(1, new("status", status), new("has_issues", issueCount > 0));
        _logger.LogInformation("VAT return calculated for company {CompanyId}. Status={Status}, SourceCount={SourceCount}, IssueCount={IssueCount}.",
            companyId, status, sourceCount, issueCount);
    }

    public void VatReturnFinalized(Guid companyId, Guid vatReturnId, int version, string checksum)
    {
        VatReturnFinalizations.Add(1,
            new KeyValuePair<string, object?>[] { new("status", "locked") });
        _logger.LogInformation("VAT return {VatReturnId} version {Version} finalized for company {CompanyId}. PackageChecksum={PackageChecksum}.",
            vatReturnId, version, companyId, checksum);
    }
}
