using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ConnectedBankingReadinessOptions
{
    public const string SectionName = "Finance:ConnectedBankingReadiness";

    public int ConsentExpiryWarningDays { get; set; } = 14;
    public int FeedLagWarningMinutes { get; set; } = 60;
    public int UnreconciledWarningDays { get; set; } = 7;
    public int StaleApprovalWarningDays { get; set; } = 2;
    public int UnsettledBatchWarningDays { get; set; } = 2;
    public int WorkerBacklogWarningMinutes { get; set; } = 15;
}

public sealed class ConnectedBankingReadinessService(
    VirtualCompanyDbContext db,
    IAccountingReportingService accountingReporting,
    IOptions<ConnectedBankingReadinessOptions> options,
    TimeProvider timeProvider,
    ICompanyContextAccessor? companyContext = null) : IConnectedBankingReadinessService
{
    private const int MaximumEvidenceIds = 25;

    private static readonly string[] PaymentOutboxTopics =
    [
        CompanyOutboxTopics.PaymentBatchSubmissionRequested,
        CompanyOutboxTopics.PaymentBatchStatusPollRequested,
        CompanyOutboxTopics.PaymentBatchCancellationRequested,
        CompanyOutboxTopics.PaymentRemittanceDeliveryRequested
    ];

    private static readonly IReadOnlyList<ConnectedBankingCapacityProfileDto> Profiles =
    [
        Profile(ConnectedBankingCapacityProfileKeys.Small, "Small connected-banking company", 25, 4, 4,
            (ConnectedBankingCapacityResourceKeys.Connections, 25),
            (ConnectedBankingCapacityResourceKeys.FeedAccounts, 100),
            (ConnectedBankingCapacityResourceKeys.FeedTransactions, 250_000),
            (ConnectedBankingCapacityResourceKeys.MatchingCandidates, 50_000),
            (ConnectedBankingCapacityResourceKeys.PaymentBatches, 25_000),
            (ConnectedBankingCapacityResourceKeys.WebhookReceipts, 250_000),
            (ConnectedBankingCapacityResourceKeys.OpenWorkerItems, 25_000)),
        Profile(ConnectedBankingCapacityProfileKeys.Medium, "Medium connected-banking company", 100, 16, 16,
            (ConnectedBankingCapacityResourceKeys.Connections, 100),
            (ConnectedBankingCapacityResourceKeys.FeedAccounts, 500),
            (ConnectedBankingCapacityResourceKeys.FeedTransactions, 2_500_000),
            (ConnectedBankingCapacityResourceKeys.MatchingCandidates, 500_000),
            (ConnectedBankingCapacityResourceKeys.PaymentBatches, 250_000),
            (ConnectedBankingCapacityResourceKeys.WebhookReceipts, 2_500_000),
            (ConnectedBankingCapacityResourceKeys.OpenWorkerItems, 250_000))
    ];

    private static readonly IReadOnlyList<ConnectedBankingServiceObjectiveDto> Objectives =
    [
        Objective("feed_page_commit_p95", "Bank-feed page commit p95", "milliseconds", 1_500, 3_000,
            "One provider page, protected source object, normalization, and checkpoint commit",
            "Inspect provider latency, transaction duration, account-leading indexes, and object-storage latency."),
        Objective("feed_recovery_completion", "Interrupted feed recovery", "minutes", 15, 30,
            "Expired lease or cursor recovery through restored gap-free coverage",
            "Inspect lease takeover, cursor evidence, bounded backfill, and provider throttling."),
        Objective("matching_candidates_p95", "Matching-candidate queue p95", "milliseconds", 1_500, 3_000,
            "First 250 company-scoped candidates at the selected profile",
            "Inspect candidate status/date indexes and rule-evidence fan-out."),
        Objective("payment_batch_validation_p95", "Payment-batch validation p95", "milliseconds", 2_000, 4_000,
            "One batch containing 1,000 current instructions",
            "Inspect source-version, beneficiary, approval, and cash-evidence lookups."),
        Objective("webhook_acceptance_p95", "Signed webhook acceptance p95", "milliseconds", 500, 1_000,
            "Signature, replay identity, acknowledgement persistence, and durable continuation",
            "Inspect signature verification, provider-identity indexes, and outbox contention."),
        Objective("treasury_workspace_p95", "Daily treasury workspace p95", "milliseconds", 1_500, 3_000,
            "Bounded daily workspace with 50 accounts, 50 payment items, and 50 exceptions",
            "Inspect bounded projections, tenant-leading indexes, and stale provider evidence."),
        Objective("worker_queue_age", "Connected-banking worker queue age", "minutes", 15, 30,
            "Oldest active feed or payment execution item",
            "Recover expired leases, resolve failed provider work, and reconcile ambiguous outcomes before retry.")
    ];

    public async Task<ConnectedBankingReadinessReadModel> GetAsync(
        GetConnectedBankingReadinessQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var profile = ResolveProfile(query.ProfileKey);
        var settings = options.Value;
        var nowUtc = NormalizeUtc(query.AsOfUtc) ?? timeProvider.GetUtcNow().UtcDateTime;

        var counts = await LoadVolumeCountsAsync(query.CompanyId, nowUtc, cancellationToken);
        var volumes = profile.Volumes.Select(volume => new ConnectedBankingVolumeMeasurementDto(
            volume.Resource,
            counts.GetValueOrDefault(volume.Resource),
            volume.MaximumCount,
            ResolveCapacityStatus(counts.GetValueOrDefault(volume.Resource), volume.MaximumCount)))
            .ToArray();

        var checks = new List<ConnectedBankingReadinessCheckDto>(12)
        {
            await CheckConsentExpiryAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckFeedGapsAsync(query.CompanyId, cancellationToken),
            await CheckFeedLagAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckDuplicateIdentityAsync(query.CompanyId, cancellationToken),
            await CheckUnreconciledAgingAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckSuspenseAsync(query.CompanyId, cancellationToken),
            await CheckStaleApprovalsAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckPaymentExecutionsAsync(query.CompanyId, PaymentExecutionStatuses.ReconciliationRequired,
                ConnectedBankingReadinessCheckKeys.AmbiguousSubmissions, ConnectedBankingReadinessStatuses.Blocked,
                "No ambiguous provider submission requires reconciliation.",
                "Reconcile provider and retained request evidence before any retry or cancellation.", cancellationToken),
            await CheckPaymentExecutionsAsync(query.CompanyId, PaymentExecutionStatuses.Rejected,
                ConnectedBankingReadinessCheckKeys.RejectedInstructions, ConnectedBankingReadinessStatuses.Attention,
                "No provider-rejected instruction remains in the review queue.",
                "Review the safe rejection reason, correct the instruction, and create a new approved version if appropriate.", cancellationToken),
            await CheckUnsettledBatchesAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckWorkerBacklogAsync(query.CompanyId, nowUtc, settings, cancellationToken),
            await CheckControlAccountsAsync(query.CompanyId, nowUtc, cancellationToken)
        };

        var capacityBreached = volumes.Any(volume => volume.Status == AccountingCapacityStatuses.Breached);
        var status = checks.Any(check => check.Status is ConnectedBankingReadinessStatuses.Blocked or ConnectedBankingReadinessStatuses.NotMeasured) || capacityBreached
            ? ConnectedBankingReadinessStatuses.Blocked
            : checks.Any(check => check.Status == ConnectedBankingReadinessStatuses.Attention) ||
              volumes.Any(volume => volume.Status == AccountingCapacityStatuses.Attention)
                ? ConnectedBankingReadinessStatuses.Attention
                : ConnectedBankingReadinessStatuses.Ready;

        return new ConnectedBankingReadinessReadModel(
            query.CompanyId,
            status,
            status != ConnectedBankingReadinessStatuses.Blocked,
            profile.Key,
            nowUtc,
            Profiles,
            Objectives,
            volumes,
            checks);
    }

    private async Task<Dictionary<string, long>> LoadVolumeCountsAsync(
        Guid companyId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var activeOutboxStatuses = new[]
        {
            CompanyOutboxMessageStatus.Pending,
            CompanyOutboxMessageStatus.InProgress,
            CompanyOutboxMessageStatus.RetryScheduled
        };
        var activeBackgroundStatuses = new[]
        {
            BackgroundExecutionStatus.Pending,
            BackgroundExecutionStatus.InProgress,
            BackgroundExecutionStatus.RetryScheduled
        };
        var activeFeedStatuses = new[]
        {
            BankFeedCheckpointStatuses.Queued,
            BankFeedCheckpointStatuses.Running,
            BankFeedCheckpointStatuses.Failed
        };

        var openSuggestions = await db.ReconciliationSuggestionRecords.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.Status == ReconciliationSuggestionStatuses.Open,
                cancellationToken);
        var proposedGroups = await db.AdvancedReconciliationGroups.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.Status == AdvancedReconciliationGroupStatuses.Proposed,
                cancellationToken);
        var openOutbox = await db.CompanyOutboxMessages.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && PaymentOutboxTopics.Contains(x.Topic) &&
                                 activeOutboxStatuses.Contains(x.Status), cancellationToken);
        var openBackground = await db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && activeBackgroundStatuses.Contains(x.Status),
                cancellationToken);
        var openFeeds = await db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && activeFeedStatuses.Contains(x.Status) &&
                                 (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= nowUtc), cancellationToken);

        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [ConnectedBankingCapacityResourceKeys.Connections] = await db.BankConnections.IgnoreQueryFilters()
                .AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken),
            [ConnectedBankingCapacityResourceKeys.FeedAccounts] = await db.BankFeedCheckpoints.IgnoreQueryFilters()
                .AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken),
            [ConnectedBankingCapacityResourceKeys.FeedTransactions] = await db.BankFeedSourceTransactions.IgnoreQueryFilters()
                .AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken),
            [ConnectedBankingCapacityResourceKeys.MatchingCandidates] = openSuggestions + proposedGroups,
            [ConnectedBankingCapacityResourceKeys.PaymentBatches] = await db.PaymentBatches.IgnoreQueryFilters()
                .AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken),
            [ConnectedBankingCapacityResourceKeys.WebhookReceipts] = await db.PaymentProviderWebhookReceipts.IgnoreQueryFilters()
                .AsNoTracking().LongCountAsync(x => x.CompanyId == companyId, cancellationToken),
            [ConnectedBankingCapacityResourceKeys.OpenWorkerItems] = openOutbox + openBackground + openFeeds
        };
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckConsentExpiryAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var warningUtc = nowUtc.AddDays(settings.ConsentExpiryWarningDays);
        var query = db.BankConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        (x.Status == BankConnectionStatuses.Active || x.Status == BankConnectionStatuses.AttentionRequired) &&
                        x.ConsentExpiresUtc.HasValue && x.ConsentExpiresUtc <= warningUtc)
            .Select(x => new { x.Id, x.ConsentExpiresUtc });
        var count = await query.CountAsync(cancellationToken);
        var expired = await query.CountAsync(row => row.ConsentExpiresUtc <= nowUtc, cancellationToken);
        var rows = await query.OrderBy(x => x.ConsentExpiresUtc)
            .Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        var status = expired > 0
            ? ConnectedBankingReadinessStatuses.Blocked
            : count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready;
        return Check(ConnectedBankingReadinessCheckKeys.ConsentExpiry, status, count,
            rows.Length == 0 ? null : (decimal?)(rows[0].ConsentExpiresUtc!.Value - nowUtc).TotalDays,
            "days", settings.ConsentExpiryWarningDays,
            rows.Length == 0
                ? "No active bank consent is expired or inside the renewal warning window."
                : expired > 0
                    ? $"{expired} active bank consent(s) are expired; provider access must not continue."
                    : "An active bank consent is approaching expiry and needs planned renewal.",
            "Renew consent through the authorized bank-connection flow; do not bypass provider acknowledgement.",
            rows.Select(row => row.Id).ToArray());
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckFeedGapsAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        var query = db.BankFeedGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == BankFeedGapStatuses.Open)
            .OrderBy(x => x.DetectedUtc).Select(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        return Check(ConnectedBankingReadinessCheckKeys.FeedGaps,
            count > 0 ? ConnectedBankingReadinessStatuses.Blocked : ConnectedBankingReadinessStatuses.Ready,
            count, null, null, 0,
            count == 0 ? "No open bank-feed coverage gap is recorded." : "Bank-feed coverage contains an unresolved missing range or cursor regression.",
            "Run the bounded recovery flow and verify retained source evidence closes the exact range without duplicates.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckFeedLagAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var warningUtc = nowUtc.AddMinutes(-settings.FeedLagWarningMinutes);
        var query = from checkpoint in db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
                          join connection in db.BankConnections.IgnoreQueryFilters().AsNoTracking()
                              on new { checkpoint.CompanyId, Id = checkpoint.ConnectionId }
                              equals new { connection.CompanyId, connection.Id }
                          where checkpoint.CompanyId == companyId && connection.Status == BankConnectionStatuses.Active &&
                                (!checkpoint.LastSuccessfulSyncUtc.HasValue || checkpoint.LastSuccessfulSyncUtc <= warningUtc)
                          select new { checkpoint.Id, checkpoint.LastSuccessfulSyncUtc };
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(row => row.LastSuccessfulSyncUtc)
            .Take(MaximumEvidenceIds).ToArrayAsync(cancellationToken);
        var maximumLagMinutes = rows.Length == 0
            ? (decimal?)null
            : rows.Max(row => row.LastSuccessfulSyncUtc.HasValue
                ? (decimal)(nowUtc - row.LastSuccessfulSyncUtc.Value).TotalMinutes
                : decimal.MaxValue);
        return Check(ConnectedBankingReadinessCheckKeys.FeedLag,
            count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, maximumLagMinutes == decimal.MaxValue ? null : maximumLagMinutes, "minutes",
            settings.FeedLagWarningMinutes,
            rows.Length == 0 ? "Every active feed has current successful coverage evidence." : "One or more active feeds have no current successful synchronization evidence.",
            "Inspect provider health, checkpoint failure, queue age, and consent before requesting safe synchronization.",
            rows.Select(row => row.Id).ToArray());
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckDuplicateIdentityAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        var feedDuplicateGroups = await db.BankFeedSourceTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.CheckpointId, x.StableIdentity })
            .Where(group => group.Count() > 1)
            .CountAsync(cancellationToken);
        var bankDuplicateGroups = await db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RowIdentity != null && x.ImportSource != null)
            .GroupBy(x => new { x.BankAccountId, x.ImportSource, x.RowIdentity })
            .Where(group => group.Count() > 1)
            .CountAsync(cancellationToken);
        var duplicateCount = feedDuplicateGroups + bankDuplicateGroups;
        return Check(ConnectedBankingReadinessCheckKeys.DuplicateIdentity,
            duplicateCount > 0 ? ConnectedBankingReadinessStatuses.Blocked : ConnectedBankingReadinessStatuses.Ready,
            duplicateCount, null, null, 0,
            duplicateCount == 0 ? "Feed and normalized bank-row identities are unique within their company/account scope." : "Duplicate provider or normalized bank-row identities were detected.",
            "Stop ingestion, preserve source objects, and reconcile each duplicate identity before release.", []);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckUnreconciledAgingAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var cutoff = nowUtc.AddDays(-settings.UnreconciledWarningDays);
        var query = db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != BankTransactionReconciliationStatuses.Reconciled &&
                        x.BookingDate <= cutoff)
            .OrderBy(x => x.BookingDate).Select(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        return Check(ConnectedBankingReadinessCheckKeys.UnreconciledAging,
            count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, null, "items", settings.UnreconciledWarningDays,
            count == 0 ? "No unreconciled bank row is older than the configured review window." : "Bank rows have remained unreconciled beyond the configured review window.",
            "Review matching evidence, residuals, and suspense outcomes; retain explicit human decisions.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckSuspenseAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        var bankQuery = db.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PostingState == BankTransactionPostingStates.Suspense)
            .OrderBy(x => x.UpdatedUtc).Select(x => x.BankTransactionId);
        var bankCount = await bankQuery.CountAsync(cancellationToken);
        var bankRows = await bankQuery.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        var ledgerQuery = db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                        x.FinanceAccount.ControlAccountRole == AccountingAccountRoleKeys.Suspense)
            .Select(x => new { x.LedgerEntryId, x.DebitAmount, x.CreditAmount });
        var ledgerCount = await ledgerQuery.Select(x => x.LedgerEntryId).Distinct().CountAsync(cancellationToken);
        var ledgerRows = await ledgerQuery.Select(x => x.LedgerEntryId).Distinct()
            .Take(MaximumEvidenceIds).ToArrayAsync(cancellationToken);
        var balance = decimal.Round(await ledgerQuery
            .SumAsync(x => (decimal?)(x.DebitAmount - x.CreditAmount), cancellationToken) ?? 0m, 2);
        var ids = bankRows.Concat(ledgerRows).Distinct().Take(MaximumEvidenceIds).ToArray();
        var count = bankCount + ledgerCount;
        var hasSuspense = count > 0 || balance != 0m;
        return Check(ConnectedBankingReadinessCheckKeys.Suspense,
            hasSuspense ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, balance, "currency_units", 0,
            hasSuspense ? "Connected-banking evidence still has an unresolved suspense classification or balance." : "No connected-banking suspense classification or balance is outstanding.",
            "Reclassify with source evidence in an open period; preserve the original suspense and correction chain.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckStaleApprovalsAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var cutoff = nowUtc.AddDays(-settings.StaleApprovalWarningDays);
        var query = db.PaymentBatchApprovalBindings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == PaymentBatchApprovalBindingStatuses.Pending &&
                        x.CreatedUtc <= cutoff)
            .OrderBy(x => x.CreatedUtc).Select(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        return Check(ConnectedBankingReadinessCheckKeys.StaleApprovals,
            count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, null, "items", settings.StaleApprovalWarningDays,
            count == 0 ? "No payment-batch approval is older than the configured review window." : "Payment-batch approvals have waited beyond the configured review window.",
            "Approve, reject, cancel, or regenerate from current source and beneficiary versions.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckPaymentExecutionsAsync(
        Guid companyId,
        string executionStatus,
        string checkKey,
        string failureStatus,
        string readyExplanation,
        string operatorAction,
        CancellationToken cancellationToken)
    {
        var query = db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == executionStatus)
            .OrderBy(x => x.UpdatedUtc).Select(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        return Check(checkKey, count > 0 ? failureStatus : ConnectedBankingReadinessStatuses.Ready,
            count, null, null, 0,
            count == 0 ? readyExplanation : $"{count} payment execution(s) are in '{executionStatus}' state.",
            operatorAction, ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckUnsettledBatchesAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var cutoff = nowUtc.AddDays(-settings.UnsettledBatchWarningDays);
        var unsettledStatuses = new[]
        {
            PaymentExecutionStatuses.ProviderAccepted,
            PaymentExecutionStatuses.Processing,
            PaymentExecutionStatuses.ProviderCompleted
        };
        var query = db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && unsettledStatuses.Contains(x.Status) && x.UpdatedUtc <= cutoff)
            .OrderBy(x => x.UpdatedUtc).Select(x => x.Id);
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        return Check(ConnectedBankingReadinessCheckKeys.UnsettledBatches,
            count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, null, "items", settings.UnsettledBatchWarningDays,
            count == 0 ? "No provider-accepted or completed payment batch is overdue for settlement evidence." : "Payment batches have exceeded the settlement-evidence review window.",
            "Poll only when safe, inspect bank evidence, and reconcile without claiming beneficiary settlement.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckWorkerBacklogAsync(
        Guid companyId,
        DateTime nowUtc,
        ConnectedBankingReadinessOptions settings,
        CancellationToken cancellationToken)
    {
        var cutoff = nowUtc.AddMinutes(-settings.WorkerBacklogWarningMinutes);
        var feedQuery = db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        (x.Status == BankFeedCheckpointStatuses.AttentionRequired ||
                         x.Status == BankFeedCheckpointStatuses.Failed && x.UpdatedUtc <= cutoff ||
                         x.Status == BankFeedCheckpointStatuses.Running && x.LeaseExpiresUtc <= nowUtc ||
                         x.Status == BankFeedCheckpointStatuses.Queued && x.UpdatedUtc <= cutoff))
            .OrderBy(x => x.UpdatedUtc).Select(x => x.Id);
        var feedCount = await feedQuery.CountAsync(cancellationToken);
        var feedRows = await feedQuery.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        var outboxQuery = db.CompanyOutboxMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && PaymentOutboxTopics.Contains(x.Topic) &&
                        (x.Status == CompanyOutboxMessageStatus.Failed ||
                         x.Status != CompanyOutboxMessageStatus.Dispatched && x.CreatedUtc <= cutoff))
            .OrderBy(x => x.CreatedUtc).Select(x => x.Id);
        var outboxCount = await outboxQuery.CountAsync(cancellationToken);
        var outboxRows = await outboxQuery.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        var backgroundQuery = db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        (x.Status == BackgroundExecutionStatus.Failed || x.Status == BackgroundExecutionStatus.Blocked ||
                         x.Status == BackgroundExecutionStatus.Escalated ||
                         x.Status == BackgroundExecutionStatus.InProgress && x.LeaseExpiresUtc <= nowUtc ||
                         (x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled) &&
                         x.CreatedUtc <= cutoff))
            .OrderBy(x => x.CreatedUtc).Select(x => x.Id);
        var backgroundCount = await backgroundQuery.CountAsync(cancellationToken);
        var backgroundRows = await backgroundQuery.Take(MaximumEvidenceIds)
            .ToArrayAsync(cancellationToken);
        var ids = feedRows.Concat(outboxRows).Concat(backgroundRows).Distinct().Take(MaximumEvidenceIds).ToArray();
        var count = checked(feedCount + outboxCount + backgroundCount);
        return Check(ConnectedBankingReadinessCheckKeys.WorkerBacklog,
            count > 0 ? ConnectedBankingReadinessStatuses.Attention : ConnectedBankingReadinessStatuses.Ready,
            count, null, "items", settings.WorkerBacklogWarningMinutes,
            count == 0 ? "No connected-banking worker item is failed, lease-expired, or older than the queue objective." : "Connected-banking feed or payment work is failed, lease-expired, or too old.",
            "Recover expired leases, inspect safe failure classification, and reconcile ambiguity before retry.", ids);
    }

    private async Task<ConnectedBankingReadinessCheckDto> CheckControlAccountsAsync(
        Guid companyId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var periodId = await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.StartUtc <= nowUtc && x.EndUtc > nowUtc)
            .OrderByDescending(x => x.StartUtc).Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.EndUtc <= nowUtc)
                .OrderByDescending(x => x.EndUtc).Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (!periodId.HasValue)
        {
            return Check(ConnectedBankingReadinessCheckKeys.ControlAccountDifferences,
                ConnectedBankingReadinessStatuses.NotMeasured, 0, null, null, 0,
                "Control-account reconciliation was not measured because no current or completed fiscal period exists.",
                "Complete accounting setup and run control-account reconciliation before release.", []);
        }

        var reconciliation = await accountingReporting.GetControlAccountReconciliationAsync(
            new GetControlAccountReconciliationQuery(companyId, periodId.Value), cancellationToken);
        if (reconciliation.Accounts.Count == 0)
        {
            return Check(ConnectedBankingReadinessCheckKeys.ControlAccountDifferences,
                ConnectedBankingReadinessStatuses.NotMeasured, 0, null, null, 0,
                "Control-account reconciliation returned no configured AR, AP, or bank control account.",
                "Configure control-account roles and rerun reconciliation before release.", []);
        }

        var differences = reconciliation.Accounts.Where(account => !account.IsReconciled).ToArray();
        var amount = differences.Sum(account => Math.Abs(account.Difference));
        var subjectIds = differences.SelectMany(account => account.DifferenceJournalEntryIds)
            .Distinct().Take(MaximumEvidenceIds).ToArray();
        return Check(ConnectedBankingReadinessCheckKeys.ControlAccountDifferences,
            differences.Length > 0 ? ConnectedBankingReadinessStatuses.Blocked : ConnectedBankingReadinessStatuses.Ready,
            differences.Length, amount, "currency_units", 0,
            differences.Length == 0 ? "Configured AR, AP, and bank control accounts reconcile to their source postings." : "One or more configured control accounts differ from supported source postings.",
            "Stop release, preserve evidence, and reconcile every difference to its source journal before proceeding.", subjectIds);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (companyContext is { IsResolved: true, CompanyId: Guid scopedCompanyId } && scopedCompanyId != companyId)
            throw new UnauthorizedAccessException("The requested company is outside the resolved tenant context.");
    }

    private static ConnectedBankingCapacityProfileDto ResolveProfile(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key)
            ? ConnectedBankingCapacityProfileKeys.Small
            : key.Trim().ToLowerInvariant();
        return Profiles.SingleOrDefault(profile => profile.Key == normalized)
               ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Use the small or medium connected-banking profile.");
    }

    private static ConnectedBankingCapacityProfileDto Profile(string key, string displayName,
        int concurrentUsers, int concurrentFeedWorkers, int concurrentPaymentWorkers,
        params (string Resource, long MaximumCount)[] volumes) =>
        new(key, displayName, concurrentUsers, concurrentFeedWorkers, concurrentPaymentWorkers,
            volumes.Select(volume => new ConnectedBankingSupportedVolumeDto(volume.Resource, volume.MaximumCount)).ToArray());

    private static ConnectedBankingServiceObjectiveDto Objective(string key, string displayName, string unit,
        decimal objective, decimal warningThreshold, string scope, string remediation) =>
        new(key, displayName, unit, objective, warningThreshold, scope, remediation);

    private static ConnectedBankingReadinessCheckDto Check(string key, string status, int count,
        decimal? value, string? unit, decimal? threshold, string explanation, string operatorAction,
        IReadOnlyList<Guid> subjectIds) =>
        new(key, status, count, value, unit, threshold, explanation, operatorAction,
            subjectIds ?? Array.Empty<Guid>());

    private static string ResolveCapacityStatus(long current, long supported) =>
        current > supported
            ? AccountingCapacityStatuses.Breached
            : current >= decimal.ToInt64(decimal.Ceiling(supported * 0.8m))
                ? AccountingCapacityStatuses.Attention
                : AccountingCapacityStatuses.WithinObjective;

    private static DateTime? NormalizeUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
    };
}
