using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingCapacityOptions
{
    public const string SectionName = "AccountingCapacity";
    public string DefaultProfile { get; set; } = AccountingCapacityProfileKeys.Small;
    public int DefaultCleanupBatchSize { get; set; } = 100;
    public int MaximumCleanupBatchSize { get; set; } = 500;
}

public sealed class AccountingCapacityService : IAccountingCapacityService
{
    private static readonly BackgroundExecutionType[] FinanceExecutionTypes =
    [
        BackgroundExecutionType.FinanceSeed,
        BackgroundExecutionType.FinanceReportRegeneration,
        BackgroundExecutionType.FinanceInsightRefresh
    ];

    private static readonly IReadOnlyList<AccountingSupportedVolumeProfileDto> Profiles =
    [
        Profile(AccountingCapacityProfileKeys.Small, "Small launch company", 25, 10,
            ("accounts", 300), ("fiscal_periods", 120), ("journals", 100_000), ("journal_lines", 400_000),
            ("customer_invoices", 100_000), ("supplier_bills", 100_000), ("payments", 100_000),
            ("allocations", 200_000), ("bank_rows", 250_000), ("evidence_links", 500_000),
            ("audits", 1_000_000), ("provider_references", 500_000), ("exports", 10_000),
            ("invoice_drafts", 100_000), ("rendered_artifacts", 100_000), ("delivery_attempts", 250_000),
            ("recurring_occurrences", 250_000), ("customer_statements", 100_000), ("collection_cases", 100_000),
            ("worker_backlog", 25_000)),
        Profile(AccountingCapacityProfileKeys.Medium, "Medium launch company", 100, 30,
            ("accounts", 1_000), ("fiscal_periods", 240), ("journals", 1_000_000), ("journal_lines", 5_000_000),
            ("customer_invoices", 1_000_000), ("supplier_bills", 1_000_000), ("payments", 1_000_000),
            ("allocations", 2_000_000), ("bank_rows", 2_500_000), ("evidence_links", 5_000_000),
            ("audits", 10_000_000), ("provider_references", 5_000_000), ("exports", 100_000),
            ("invoice_drafts", 1_000_000), ("rendered_artifacts", 1_000_000), ("delivery_attempts", 2_500_000),
            ("recurring_occurrences", 2_500_000), ("customer_statements", 1_000_000), ("collection_cases", 1_000_000),
            ("worker_backlog", 250_000))
    ];

    private static readonly IReadOnlyList<AccountingServiceObjectiveDto> Objectives =
    [
        Objective("posting_p95", "Accounting posting p95", "milliseconds", 750, 1_500,
            "Committed posting attempts", "Inspect transaction duration, sequence contention, and source-idempotency conflicts."),
        Objective("common_list_p95", "Common Finance list p95", "milliseconds", 500, 1_000,
            "Bounded first page", "Inspect tenant-leading indexes and query-plan regressions."),
        Objective("detail_p95", "Accounting detail p95", "milliseconds", 250, 500,
            "Single company-owned record", "Inspect joins, evidence fan-out, and avoidable tracking."),
        Objective("general_ledger_page_p95", "General ledger page p95", "milliseconds", 1_200, 2_500,
            "One bounded account page", "Inspect ledger period/account indexes and page size."),
        Objective("trial_balance_p95", "Trial balance p95", "milliseconds", 1_500, 3_000,
            "One fiscal period", "Inspect grouped ledger aggregation and period selectivity."),
        Objective("statements_p95", "Financial statements p95", "milliseconds", 2_000, 4_000,
            "One fiscal period", "Inspect snapshot freshness and grouped ledger reads."),
        Objective("close_validation_p95", "Close validation p95", "milliseconds", 3_000, 6_000,
            "One fiscal period", "Inspect reconciliation backlog and snapshot regeneration."),
        Objective("export_request_p95", "Export request p95", "milliseconds", 500, 1_000,
            "Durable request acceptance", "Inspect idempotency lookup and queue persistence."),
        Objective("export_completion", "Export completion", "minutes", 5, 15,
            "Completed bounded-period export", "Inspect queue age, storage failure, and export size."),
        Objective("provider_sync_lag", "Provider sync lag", "minutes", 15, 60,
            "Oldest active Finance connection", "Run or repair the provider sync before relying on provider-authority data."),
        Objective("reconciliation_backlog", "Reconciliation backlog", "items", 0, 1,
            "Ambiguous provider outcomes", "Reconcile possible provider success before any retry."),
        Objective("worker_queue_age", "Worker queue age", "minutes", 15, 30,
            "Oldest queued Finance execution", "Open Finance work recovery and resolve stalled or exhausted work."),
        Objective("expired_export_binary", "Expired export binary content", "bytes", 0, 1,
            "Expired completed export content", "Preview and run the authorized bounded export-content cleanup."),
        Objective("failed_export_jobs", "Failed export jobs", "items", 0, 1,
            "Terminal export failures", "Inspect the safe failure and request a new export after remediation."),
        Objective("invoice_list_p95", "Customer invoice list p95", "milliseconds", 500, 1_000,
            "Bounded first page at the selected supported volume", "Inspect company/status/date indexes and avoid unbounded delivery fan-out."),
        Objective("draft_preview_p95", "Invoice draft preview p95", "milliseconds", 750, 1_500,
            "One current draft with 100 lines", "Inspect tax-policy resolution, evidence loading, and line calculation allocation."),
        Objective("invoice_issue_p95", "Native invoice issue p95", "milliseconds", 1_500, 3_000,
            "Committed number, invoice, and journal transaction under supported concurrency", "Inspect number-series contention, transaction duration, and posting evidence queries."),
        Objective("invoice_render_p95", "Invoice PDF render p95", "milliseconds", 3_000, 6_000,
            "A 25-page immutable issued snapshot", "Inspect renderer pagination, font work, and object-storage latency."),
        Objective("receivables_aging_p95", "Receivables aging p95", "milliseconds", 1_500, 3_000,
            "One bounded company cutoff and currency", "Inspect invoice, allocation, correction, and collection-case indexes."),
        Objective("customer_statement_p95", "Customer statement generation p95", "milliseconds", 3_000, 6_000,
            "One customer with 5,000 bounded statement items", "Inspect source projection, deterministic rendering, and checksum allocation."),
        Objective("collections_queue_p95", "Collections queue p95", "milliseconds", 750, 1_500,
            "Prioritized first 250 open items", "Inspect due-date, customer, status, and follow-up indexes."),
        Objective("receivables_readiness_p95", "Receivables readiness p95", "milliseconds", 1_000, 2_000,
            "Ten bounded operator checks for one company", "Inspect status/updated indexes and retain the 25-item evidence cap.")
    ];

    private static readonly IReadOnlyList<AccountingRetentionClassDto> RetentionClasses =
    [
        Retention(AccountingRetentionClassKeys.AccountingTruth, "Immutable accounting truth", AccountingRetentionModes.Preserve,
            "Posted journals, lines, voucher identities, closed snapshots, finalized reports, and source links are never purged.", false, true, false),
        Retention(AccountingRetentionClassKeys.SourceEvidence, "Source and statutory evidence", AccountingRetentionModes.Preserve,
            "Source documents, hashes, evidence links, and return evidence follow the configured legal retention policy.", false, true, false),
        Retention(AccountingRetentionClassKeys.ApprovalAndAudit, "Approvals and audit explanation", AccountingRetentionModes.Preserve,
            "Approval decisions and audit evidence required to explain accounting actions remain queryable.", false, true, false),
        Retention(AccountingRetentionClassKeys.ProviderReconciliation, "Provider and reconciliation evidence", AccountingRetentionModes.Preserve,
            "Provider acknowledgements, references, ambiguous outcomes, and reconciliation decisions are preserved.", false, true, false),
        Retention(AccountingRetentionClassKeys.GeneratedExports, "Generated export content", AccountingRetentionModes.MetadataOnlyCleanup,
            "Expired binary content may be removed in bounded audited batches; checksum, file manifest, request, period, and policy metadata remain.", true, true, true),
        Retention(AccountingRetentionClassKeys.OperationalAttempts, "Operational attempts and failures", AccountingRetentionModes.Preserve,
            "Attempt history remains available for launch operations. A shorter policy requires a separate approved archive design.", false, true, false),
        Retention(AccountingRetentionClassKeys.SimulationData, "Explicit Simulation Lab data", AccountingRetentionModes.Preserve,
            "Simulation data is isolated and is never removed by production accounting cleanup.", false, true, false),
        Retention(AccountingRetentionClassKeys.EphemeralCaches, "Ephemeral read caches", AccountingRetentionModes.BoundedDelete,
            "Caches may expire through their owning cache policy and must be reproducible from company-scoped authoritative data.", false, false, true)
    ];

    private readonly VirtualCompanyDbContext _db;
    private readonly IOptions<AccountingCapacityOptions> _options;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountingCapacityService> _logger;
    private readonly ICompanyDocumentStorage? _documentStorage;

    public AccountingCapacityService(
        VirtualCompanyDbContext db,
        IOptions<AccountingCapacityOptions> options,
        IAuditEventWriter audit,
        AccountingOperationsTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<AccountingCapacityService> logger,
        ICompanyDocumentStorage? documentStorage = null)
    {
        _db = db;
        _options = options;
        _audit = audit;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _logger = logger;
        _documentStorage = documentStorage;
    }

    public async Task<AccountingCapacityReadModel> GetAsync(
        GetAccountingCapacityQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCompany(query.CompanyId);
        var profile = ResolveProfile(query.ProfileKey);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["accounts"] = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["fiscal_periods"] = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["journals"] = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["journal_lines"] = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["customer_invoices"] = await _db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["supplier_bills"] = await _db.FinanceBills.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["payments"] = await _db.Payments.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["allocations"] = await _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["bank_rows"] = await _db.BankTransactions.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["evidence_links"] = await _db.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["audits"] = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["provider_references"] = await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["exports"] = await _db.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["invoice_drafts"] = await _db.CustomerInvoiceDrafts.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["rendered_artifacts"] = await _db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["delivery_attempts"] = await _db.CustomerInvoiceEmailDeliveries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
                + await _db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
                + await _db.CustomerReminderDeliveries.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["recurring_occurrences"] = await _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["customer_statements"] = await _db.CustomerStatementSnapshots.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["collection_cases"] = await _db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x => x.CompanyId == query.CompanyId, cancellationToken),
            ["worker_backlog"] = await _db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking().LongCountAsync(x =>
                x.CompanyId == query.CompanyId && FinanceExecutionTypes.Contains(x.ExecutionType) &&
                (x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled), cancellationToken)
        };

        var volumes = profile.Volumes.Select(item =>
        {
            var current = counts.GetValueOrDefault(item.Resource);
            var status = current > item.MaximumCount
                ? AccountingCapacityStatuses.Breached
                : current >= item.MaximumCount * 0.8m
                    ? AccountingCapacityStatuses.Attention
                    : AccountingCapacityStatuses.WithinObjective;
            return new AccountingVolumeMeasurementDto(item.Resource, current, item.MaximumCount, status);
        }).ToArray();

        var oldestQueuedUtc = await _db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && FinanceExecutionTypes.Contains(x.ExecutionType) &&
                (x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled))
            .OrderBy(x => x.CreatedUtc).Select(x => (DateTime?)x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        var oldestConnectionSyncUtc = await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderBy(x => x.LastSyncUtc ?? x.CreatedUtc)
            .Select(x => (DateTime?)(x.LastSyncUtc ?? x.CreatedUtc)).FirstOrDefaultAsync(cancellationToken);
        var reconciliationBacklog = await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderExportStatuses.ReconciliationRequired, cancellationToken);
        reconciliationBacklog += await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.ReconciliationNeeded, cancellationToken);
        reconciliationBacklog += await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.ProviderReconciliationRequired, cancellationToken);
        var expiredExportBytes = await _db.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == AccountingExportStatuses.Completed &&
                x.ExpiresUtc <= now && (x.Content != null || x.StorageKey != null))
            .SumAsync(x => x.ContentLength ?? 0L, cancellationToken);
        var failedExportJobs = await _db.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingExportStatuses.Failed, cancellationToken);

        var measurements = new List<AccountingObjectiveMeasurementDto>();
        foreach (var objective in Objectives.Where(x => x.Unit == "milliseconds" || x.Key == "export_completion"))
        {
            measurements.Add(new AccountingObjectiveMeasurementDto(objective.Key, null, objective.Unit,
                AccountingCapacityStatuses.NotMeasured,
                "This latency objective is exported through the accounting telemetry meter and measured by the SQL Server performance lane.",
                objective.Remediation));
        }
        measurements.Add(Measure("worker_queue_age", MinutesSince(now, oldestQueuedUtc), "minutes", "No Finance work is queued."));
        measurements.Add(Measure("provider_sync_lag", MinutesSince(now, oldestConnectionSyncUtc), "minutes", "No Finance provider connection is configured."));
        measurements.Add(Measure("reconciliation_backlog", reconciliationBacklog, "items", "No ambiguous provider outcome is waiting for reconciliation."));
        measurements.Add(Measure("expired_export_binary", expiredExportBytes, "bytes", "No expired export binary content is eligible for cleanup."));
        measurements.Add(Measure("failed_export_jobs", failedExportJobs, "items", "No terminal export failure needs attention."));

        var alerts = volumes.Where(x => x.Status != AccountingCapacityStatuses.WithinObjective)
            .Select(x => $"{Display(x.Resource)} is at {x.CurrentCount:N0} records against the {x.SupportedCount:N0} {profile.DisplayName.ToLowerInvariant()} profile.")
            .Concat(measurements.Where(x => x.Status is AccountingCapacityStatuses.Attention or AccountingCapacityStatuses.Breached)
                .Select(x => x.Explanation))
            .ToArray();

        _telemetry.CapacityObserved(query.CompanyId, profile.Key, oldestQueuedUtc.HasValue ? MinutesSince(now, oldestQueuedUtc) : 0,
            oldestConnectionSyncUtc.HasValue ? MinutesSince(now, oldestConnectionSyncUtc) : 0,
            reconciliationBacklog, expiredExportBytes, failedExportJobs, alerts.Length);

        return new AccountingCapacityReadModel(query.CompanyId, profile.Key, now, Profiles, Objectives,
            volumes, measurements, RetentionClasses, alerts);
    }

    public async Task<AccountingRetentionPreviewDto> PreviewRetentionAsync(
        PreviewAccountingRetentionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = NormalizeBatchSize(command.BatchSize);
        var query = EligibleExports(command.CompanyId, now);
        var eligibleCount = await query.LongCountAsync(cancellationToken);
        var eligibleBytes = await query.SumAsync(x => x.ContentLength ?? 0L, cancellationToken);
        var targets = await LoadTargetsAsync(query, batchSize, cancellationToken);
        return new AccountingRetentionPreviewDto(command.CompanyId, AccountingRetentionClassKeys.GeneratedExports,
            now, ComputePreviewToken(command.CompanyId, targets), batchSize, eligibleCount, eligibleBytes,
            targets.Select(MapTarget).ToArray(), PreservedEvidence());
    }

    public async Task<AccountingRetentionCleanupResultDto> RunRetentionCleanupAsync(
        RunAccountingRetentionCleanupCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.PreviewToken)) throw new ArgumentException("Preview token is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("A plain-English cleanup reason is required.", nameof(command));

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = NormalizeBatchSize(command.BatchSize);
        var targets = await LoadTargetsAsync(EligibleExports(command.CompanyId, now), batchSize, cancellationToken, tracked: true);
        if (targets.Count > 0 && !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(ComputePreviewToken(command.CompanyId, targets)),
                Convert.FromHexString(command.PreviewToken.Trim())))
        {
            throw new AccountingLifecycleException(AccountingLifecycleReasonCodes.PreviewStale,
                "The eligible export set changed after preview. Refresh the preview before cleanup.", isConflict: true);
        }

        long releasedBytes = 0;
        foreach (var target in targets)
        {
            if (!string.IsNullOrWhiteSpace(target.StorageKey))
            {
                if (_documentStorage is null)
                    throw new InvalidOperationException("Object storage is required to expire a statutory accounting export.");
                await _documentStorage.DeleteAsync(target.StorageKey, cancellationToken);
            }
            releasedBytes += target.ExpireContent(now);
        }

        if (targets.Count > 0)
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
                command.ActorUserId, AuditEventActions.AccountingExportContentExpired,
                AuditTargetTypes.AccountingExport, AccountingRetentionClassKeys.GeneratedExports,
                AuditEventOutcomes.Succeeded, command.Reason.Trim(), Metadata: new Dictionary<string, string?>
                {
                    ["processedCount"] = targets.Count.ToString(),
                    ["releasedBytes"] = releasedBytes.ToString(),
                    ["previewToken"] = command.PreviewToken.Trim().ToLowerInvariant(),
                    ["exportIds"] = string.Join(',', targets.Select(x => x.Id.ToString("D")))
                }, CorrelationId: command.CorrelationId, OccurredUtc: now), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        _telemetry.RetentionCleanup(command.CompanyId, targets.Count, releasedBytes, "completed");
        _logger.LogInformation(
            "Accounting export retention cleanup completed for company {CompanyId}. Processed={ProcessedCount}, ReleasedBytes={ReleasedBytes}, CorrelationId={CorrelationId}.",
            command.CompanyId, targets.Count, releasedBytes, command.CorrelationId);
        return new AccountingRetentionCleanupResultDto(command.CompanyId,
            AccountingRetentionClassKeys.GeneratedExports, now, targets.Count, releasedBytes,
            targets.Select(x => x.Id).ToArray(), AuditEventActions.AccountingExportContentExpired);
    }

    private AccountingObjectiveMeasurementDto Measure(string key, decimal? value, string unit, string emptyExplanation)
    {
        var objective = Objectives.Single(x => x.Key == key);
        if (!value.HasValue)
        {
            return new AccountingObjectiveMeasurementDto(key, null, unit, AccountingCapacityStatuses.WithinObjective,
                emptyExplanation, objective.Remediation);
        }

        var status = value.Value <= objective.Objective
            ? AccountingCapacityStatuses.WithinObjective
            : value.Value <= objective.WarningThreshold
                ? AccountingCapacityStatuses.Attention
                : AccountingCapacityStatuses.Breached;
        var explanation = status == AccountingCapacityStatuses.WithinObjective
            ? $"{objective.DisplayName} is within its {objective.Objective:N0} {unit} objective."
            : $"{objective.DisplayName} is {value.Value:N0} {unit}; the objective is {objective.Objective:N0} and the breach threshold is {objective.WarningThreshold:N0}.";
        return new AccountingObjectiveMeasurementDto(key, value, unit, status, explanation, objective.Remediation);
    }

    private IQueryable<AccountingExportJob> EligibleExports(Guid companyId, DateTime now) =>
        _db.AccountingExportJobs.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Status == AccountingExportStatuses.Completed &&
                x.ExpiresUtc <= now && (x.Content != null || x.StorageKey != null));

    private static async Task<List<AccountingExportJob>> LoadTargetsAsync(
        IQueryable<AccountingExportJob> query,
        int batchSize,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        if (!tracked) query = query.AsNoTracking();
        return await query.OrderBy(x => x.ExpiresUtc).ThenBy(x => x.Id).Take(batchSize).ToListAsync(cancellationToken);
    }

    private static AccountingRetentionTargetDto MapTarget(AccountingExportJob job) =>
        new(job.Id, job.FiscalPeriodId, job.ExpiresUtc, job.FileName ?? "Accounting export",
            job.Checksum ?? string.Empty, job.ContentLength ?? job.Content?.LongLength ?? 0);

    private static string ComputePreviewToken(Guid companyId, IReadOnlyList<AccountingExportJob> targets)
    {
        var canonical = new StringBuilder(companyId.ToString("N"));
        foreach (var target in targets.OrderBy(x => x.ExpiresUtc).ThenBy(x => x.Id))
        {
            canonical.Append('|').Append(target.Id.ToString("N"))
                .Append('|').Append(target.ExpiresUtc.Ticks)
                .Append('|').Append(target.UpdatedUtc.Ticks)
                .Append('|').Append(target.ContentLength ?? target.Content?.LongLength ?? 0);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private int NormalizeBatchSize(int value)
    {
        var maximum = Math.Max(1, _options.Value.MaximumCleanupBatchSize);
        var requested = value <= 0 ? _options.Value.DefaultCleanupBatchSize : value;
        return Math.Clamp(requested, 1, maximum);
    }

    private AccountingSupportedVolumeProfileDto ResolveProfile(string? key)
    {
        var requested = string.IsNullOrWhiteSpace(key) ? _options.Value.DefaultProfile : key.Trim();
        return Profiles.FirstOrDefault(x => x.Key.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Supported profile must be 'small' or 'medium'.", nameof(key));
    }

    private static decimal MinutesSince(DateTime now, DateTime? value) =>
        value.HasValue ? Math.Max(0, (decimal)(now - value.Value).TotalMinutes) : 0;

    private static AccountingSupportedVolumeProfileDto Profile(string key, string displayName,
        int concurrentUsers, int concurrentJobs, params (string Resource, long Maximum)[] volumes) =>
        new(key, displayName, concurrentUsers, concurrentJobs,
            volumes.Select(x => new AccountingSupportedVolumeDto(x.Resource, x.Maximum)).ToArray());

    private static AccountingServiceObjectiveDto Objective(string key, string displayName, string unit,
        decimal objective, decimal warning, string scope, string remediation) =>
        new(key, displayName, unit, objective, warning, scope, remediation);

    private static AccountingRetentionClassDto Retention(string key, string displayName, string mode,
        string policy, bool preview, bool audit, bool regeneration) =>
        new(key, displayName, mode, policy, preview, audit, regeneration);

    private static IReadOnlyList<string> PreservedEvidence() =>
    [
        "Export request, company, fiscal period, requester, and idempotency identity",
        "File name, media type, original byte length, checksum, completion time, and expiry policy",
        "Posted journals, source/evidence links, approvals, audit history, closed snapshots, and provider reconciliation evidence"
    ];

    private static string Display(string value) => value.Replace('_', ' ');

    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
    }
}
