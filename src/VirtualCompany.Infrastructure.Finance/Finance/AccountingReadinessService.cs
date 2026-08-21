using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingReadinessService : IAccountingReadinessService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly TimeProvider _timeProvider;

    public AccountingReadinessService(
        VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingReadinessDto> EvaluateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var signals = new List<AccountingReadinessSignalDto>();
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var configurationValid = configuration is not null &&
            configuration.SetupState == AccountingSetupStateValues.Ready &&
            _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out _);
        signals.Add(Signal("configuration", configurationValid ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            configurationValid ? 0 : 1, null,
            configurationValid
                ? "Accounting setup and its policy-pack version are available."
                : "Accounting setup is incomplete or its selected policy-pack version is unavailable.",
            "Complete accounting setup and validate the selected policy pack.", configuration?.Id));

        var latestRun = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.RequestedUtc)
            .Select(x => new { x.Id, x.Status }).FirstOrDefaultAsync(cancellationToken);
        var conflictIds = await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == AccountingMigrationConflictStatuses.Open)
            .OrderBy(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var migrationBlocked = latestRun is not null &&
            (latestRun.Status == AccountingMigrationRunStatuses.Failed || conflictIds.Length > 0);
        var migrationPending = latestRun is not null &&
            (latestRun.Status == AccountingMigrationRunStatuses.Queued || latestRun.Status == AccountingMigrationRunStatuses.Running);
        signals.Add(Signal("migration_conflicts",
            migrationBlocked ? AccountingReadinessStatuses.Blocked : migrationPending ? AccountingReadinessStatuses.Attention : AccountingReadinessStatuses.Ready,
            conflictIds.Length, null,
            migrationBlocked ? "Historical migration has unresolved conflicts or failed work."
                : migrationPending ? "Historical migration is still processing." : "No unresolved historical migration conflicts are recorded.",
            "Review each conflict, correct the source evidence, and rerun migration before cutover.", conflictIds));

        var failureSince = nowUtc.AddDays(-30);
        var postingFailureIds = await _dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.OccurredUtc >= failureSince &&
                x.Outcome == AuditEventOutcomes.Failed && x.Action.StartsWith("accounting."))
            .OrderByDescending(x => x.OccurredUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("posting_failures", postingFailureIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            postingFailureIds.Length, null,
            postingFailureIds.Length == 0 ? "No recent failed accounting actions are recorded." : "Recent accounting actions failed and need operator review.",
            "Use the correlation identifier and safe audit reason to diagnose each failed action.", postingFailureIds));

        var accountingTargetTypes = new[]
        {
            ApprovalTargetEntityType.ManualJournalDraft.ToStorageValue(),
            ApprovalTargetEntityType.CustomerInvoiceAccounting.ToStorageValue(),
            ApprovalTargetEntityType.SupplierBillAccounting.ToStorageValue(),
            ApprovalTargetEntityType.FinanceIntegrationWrite.ToStorageValue()
        };
        var staleBefore = nowUtc.AddDays(-7);
        var staleApprovalIds = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending &&
                x.UpdatedUtc <= staleBefore && accountingTargetTypes.Contains(x.TargetEntityType))
            .OrderBy(x => x.UpdatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("stale_approvals", staleApprovalIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            staleApprovalIds.Length, null,
            staleApprovalIds.Length == 0 ? "No accounting approval has waited more than seven days." : "Accounting approvals have been pending for more than seven days.",
            "Approve, reject, cancel, or refresh stale accounting requests before posting.", staleApprovalIds));

        var suspenseRows = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.FinanceAccount.ControlAccountRole == AccountingAccountRoleKeys.Suspense)
            .Select(x => new { x.LedgerEntryId, x.DebitAmount, x.CreditAmount }).ToArrayAsync(cancellationToken);
        var suspenseBalance = suspenseRows.Sum(x => x.DebitAmount - x.CreditAmount);
        signals.Add(Signal("suspense_balance", suspenseBalance == 0m ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            suspenseRows.Select(x => x.LedgerEntryId).Distinct().Count(), suspenseBalance,
            suspenseBalance == 0m ? "The suspense account has no outstanding balance." : "The suspense account contains amounts that still need classification.",
            "Review suspense journals and create evidence-backed reclassifications in an open period.", suspenseRows.Select(x => x.LedgerEntryId).Distinct().Take(25).ToArray()));

        var reconciliationIds = await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == BankReconciliationFollowUpStatuses.Open)
            .OrderBy(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("reconciliation_backlog", reconciliationIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            reconciliationIds.Length, null,
            reconciliationIds.Length == 0 ? "No bank reconciliation follow-up is open." : "Bank reconciliation follow-up is still open.",
            "Resolve unmatched, partial, conflict, and suspense items before close.", reconciliationIds));

        var draftJournalIds = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == LedgerEntryStatuses.Draft)
            .OrderBy(x => x.EntryUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("close_blockers", draftJournalIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            draftJournalIds.Length, null,
            draftJournalIds.Length == 0 ? "No draft journal is blocking period-close review." : "Draft journals need posting, correction, or removal before close.",
            "Review the close checklist for each open period and resolve the linked records.", draftJournalIds));

        var exportIds = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != AccountingExportStatuses.Completed)
            .OrderBy(x => x.RequestedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var providerExportIds = await _dbContext.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.Status == AccountingProviderExportStatuses.AwaitingApproval ||
                 x.Status == AccountingProviderExportStatuses.Approved ||
                 x.Status == AccountingProviderExportStatuses.Executing ||
                 x.Status == AccountingProviderExportStatuses.ReconciliationRequired))
            .OrderBy(x => x.RequestedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var allExportIds = exportIds.Concat(providerExportIds).Take(25).ToArray();
        signals.Add(Signal("export_backlog", allExportIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            exportIds.Length + providerExportIds.Length, null,
            allExportIds.Length == 0 ? "No accounting export or provider reconciliation is outstanding." : "Accounting exports or provider outcomes still need completion.",
            "Retry safe failures and reconcile ambiguous provider outcomes before treating delivery as complete.", allExportIds));

        var snapshotFailureIds = await _dbContext.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                (x.Status == BackgroundExecutionStatus.Failed || x.Status == BackgroundExecutionStatus.Blocked ||
                 x.Status == BackgroundExecutionStatus.Escalated))
            .OrderByDescending(x => x.UpdatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("snapshot_failures", snapshotFailureIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            snapshotFailureIds.Length, null,
            snapshotFailureIds.Length == 0 ? "No report snapshot regeneration failure is outstanding." : "One or more report snapshots failed to regenerate.",
            "Inspect the background failure code, correct its cause, and regenerate the affected period.", snapshotFailureIds));

        var duplicateSourceIdentityCount = await _dbContext.LedgerPostingIdentities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.Action, x.SourceType, x.SourceId, x.SourceVersion })
            .Where(x => x.Count() > 1)
            .CountAsync(cancellationToken);
        var duplicateIdempotencyCount = await _dbContext.LedgerPostingIdentities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.IdempotencyKey)
            .Where(x => x.Count() > 1)
            .CountAsync(cancellationToken);
        var duplicateReplayCount = duplicateSourceIdentityCount + duplicateIdempotencyCount;
        signals.Add(Signal("idempotent_replays",
            duplicateReplayCount == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            duplicateReplayCount, null,
            duplicateReplayCount == 0
                ? "Posting identities are unique by source version and idempotency key."
                : "Duplicate posting identities were found and accounting integrity cannot be confirmed.",
            "Stop posting, preserve the database, and reconcile duplicate identities to one evidence-backed journal before release."));

        var status = signals.Any(x => x.Status == AccountingReadinessStatuses.Blocked)
            ? AccountingReadinessStatuses.Blocked
            : signals.Any(x => x.Status == AccountingReadinessStatuses.Attention)
                ? AccountingReadinessStatuses.Attention
                : AccountingReadinessStatuses.Ready;
        return new AccountingReadinessDto(companyId, status, status != AccountingReadinessStatuses.Blocked, nowUtc, signals);
    }

    private static AccountingReadinessSignalDto Signal(
        string key, string status, int count, decimal? amount, string explanation, string operatorAction) =>
        new(key, status, count, amount, explanation, operatorAction, Array.Empty<Guid>());

    private static AccountingReadinessSignalDto Signal(
        string key, string status, int count, decimal? amount, string explanation, string operatorAction,
        Guid? subjectId) => Signal(key, status, count, amount, explanation, operatorAction,
        subjectId.HasValue ? new[] { subjectId.Value } : Array.Empty<Guid>());

    private static AccountingReadinessSignalDto Signal(
        string key, string status, int count, decimal? amount, string explanation, string operatorAction,
        IReadOnlyList<Guid> subjectIds) =>
        new(key, status, count, amount, explanation, operatorAction, subjectIds ?? Array.Empty<Guid>());
}
