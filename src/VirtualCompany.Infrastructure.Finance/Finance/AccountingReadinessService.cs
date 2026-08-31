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
    private readonly IAccountingPolicyPackValidationRegistry _validationRegistry;
    private readonly TimeProvider _timeProvider;

    public AccountingReadinessService(
        VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver,
        IAccountingPolicyPackValidationRegistry validationRegistry,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _validationRegistry = validationRegistry;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingReadinessDto> EvaluateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);
        var signals = new List<AccountingReadinessSignalDto>();
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        IAccountingPolicyPack? selectedPack = null;
        var selectedPackAvailable = configuration is not null &&
            _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out selectedPack);
        var configurationValid = configuration is not null &&
            configuration.SetupState == AccountingSetupStateValues.Ready &&
            selectedPackAvailable;
        signals.Add(Signal("configuration", configurationValid ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            configurationValid ? 0 : 1, null,
            configurationValid
                ? "Accounting setup and its policy-pack version are available."
                : "Accounting setup is incomplete or its selected policy-pack version is unavailable.",
            "Complete accounting setup and validate the selected policy pack.", configuration?.Id));

        var isSwedishPack = string.Equals(selectedPack?.Definition.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase);
        if (isSwedishPack)
        {
            var validation = _validationRegistry.Evaluate(selectedPack!, today);
            signals.Add(Signal("policy_pack_validation",
                validation.IsValidated ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
                validation.IsValidated ? 0 : 1,
                null,
                validation.Explanation,
                validation.IsValidated
                    ? "Revalidate after any policy, fixture, document-rule, export-format, or reviewed-scope change."
                    : "Keep Swedish statutory claims disabled and obtain qualified review for a new immutable pack version whose exact hash is recorded.",
                configuration?.Id));

            var profile = await _dbContext.CompanyStatutoryProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
            var missingFacts = CompanyStatutoryProfileService.BuildMissingFacts(profile);
            signals.Add(Signal("statutory_profile_completeness",
                missingFacts.Count == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
                missingFacts.Count,
                null,
                missingFacts.Count == 0
                    ? "The Swedish statutory profile contains the required formatted and user-attested facts."
                    : $"The Swedish statutory profile is missing {missingFacts.Count} required fact(s): {string.Join(", ", missingFacts)}.",
                "Complete and attest the company statutory profile before enabling Swedish statutory workflows.",
                profile?.Id));

            var unsupportedCapabilities = selectedPack!.Definition.CapabilityStates?
                .Where(entry => entry.Value.StartsWith("unsupported", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Key)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            signals.Add(Signal("unsupported_configured_capabilities",
                unsupportedCapabilities.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
                unsupportedCapabilities.Length,
                null,
                unsupportedCapabilities.Length == 0
                    ? "The selected Swedish pack has no explicitly unsupported configured capability."
                    : $"The selected Swedish pack explicitly does not support: {string.Join(", ", unsupportedCapabilities)}.",
                "Do not use unsupported cases; select a later reviewed pack only after its exact scope is approved."));
        }

        var functionalCurrency = configuration?.BaseCurrency ?? string.Empty;
        var enabledForeignCurrencies = await _dbContext.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsEnabled && x.Code != functionalCurrency)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code })
            .ToArrayAsync(cancellationToken);
        var coveredForeignCurrencies = configuration is null || enabledForeignCurrencies.Length == 0
            ? Array.Empty<string>()
            : await _dbContext.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.RateSet.Status == ExchangeRateSetStatuses.Approved &&
                    x.EffectiveDate <= today &&
                    (x.BaseCurrency == configuration.BaseCurrency || x.QuoteCurrency == configuration.BaseCurrency))
                .Select(x => x.BaseCurrency == configuration.BaseCurrency ? x.QuoteCurrency : x.BaseCurrency)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        var coveredCurrencySet = coveredForeignCurrencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingRateCurrencies = enabledForeignCurrencies
            .Where(x => !coveredCurrencySet.Contains(x.Code))
            .ToArray();
        signals.Add(Signal("exchange_rate_coverage",
            missingRateCurrencies.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            missingRateCurrencies.Length, null,
            missingRateCurrencies.Length == 0
                ? "Every enabled foreign currency has approved historical rate evidence against the functional currency."
                : $"Approved functional-currency rate evidence is missing for: {string.Join(", ", missingRateCurrencies.Select(x => x.Code))}.",
            "Import or refresh authoritative observations, approve any manual rate set, and verify the historical lookup before foreign-currency posting.",
            missingRateCurrencies.Select(x => x.Id).ToArray()));

        var incompleteRevaluationIds = await _dbContext.CurrencyRevaluationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.Status == CurrencyRevaluationRunStatuses.Failed ||
                 x.Status == CurrencyRevaluationRunStatuses.AwaitingApproval ||
                 x.Status == CurrencyRevaluationRunStatuses.NeedsReview ||
                 x.Status == CurrencyRevaluationRunStatuses.Draft))
            .OrderByDescending(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("currency_revaluation_operations",
            incompleteRevaluationIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            incompleteRevaluationIds.Length, null,
            incompleteRevaluationIds.Length == 0
                ? "No failed, superseded, or incomplete period-end currency revaluation is outstanding."
                : "Period-end currency revaluation work is failed, superseded, or awaiting completion.",
            "Open the affected run, resolve rate or review issues, and complete an approved posting or linked replacement.",
            incompleteRevaluationIds));

        var dimensionConflictIds = await _dbContext.AccountingDimensionMappingConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == "open")
            .OrderBy(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("dimension_governance",
            dimensionConflictIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            dimensionConflictIds.Length, null,
            dimensionConflictIds.Length == 0
                ? "No unresolved accounting-dimension mapping conflict is recorded."
                : "Accounting-dimension values still require an explicit company-scoped mapping decision.",
            "Resolve each external or legacy dimension conflict before the affected source is posted.",
            dimensionConflictIds));

        var scheduleExceptionIds = await _dbContext.AccountingScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.Status == AccountingScheduleOccurrenceStatuses.Blocked ||
                 x.Status == AccountingScheduleOccurrenceStatuses.Failed))
            .OrderBy(x => x.PostingDate).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("accounting_schedule_operations",
            scheduleExceptionIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            scheduleExceptionIds.Length, null,
            scheduleExceptionIds.Length == 0
                ? "No blocked or failed accounting-schedule occurrence is outstanding."
                : "Accounting-schedule occurrences require recovery before their periods can close.",
            "Inspect the retained occurrence and version evidence, correct the blocker, and regenerate safely.",
            scheduleExceptionIds));

        var fixedAssetConflictIds = await _dbContext.FixedAssetMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == "open")
            .OrderBy(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var failedDepreciationRunIds = await _dbContext.FixedAssetDepreciationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.Status == "blocked" || x.Status == "posted_with_exceptions"))
            .OrderByDescending(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var fixedAssetIssueIds = fixedAssetConflictIds.Concat(failedDepreciationRunIds).Take(25).ToArray();
        signals.Add(Signal("fixed_asset_operations",
            fixedAssetIssueIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            fixedAssetConflictIds.Length + failedDepreciationRunIds.Length, null,
            fixedAssetIssueIds.Length == 0
                ? "No fixed-asset migration conflict or depreciation-run exception is outstanding."
                : "The fixed-asset register contains migration conflicts or depreciation-run exceptions.",
            "Resolve legacy book facts explicitly and correct or reverse failed depreciation through the retained asset workflow.",
            fixedAssetIssueIds));

        var activeSeriesPolicyIds = await _dbContext.AccountingSeriesPolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.ScopeKey).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("accounting_series_governance",
            configurationValid && activeSeriesPolicyIds.Length == 0
                ? AccountingReadinessStatuses.Attention
                : AccountingReadinessStatuses.Ready,
            activeSeriesPolicyIds.Length, null,
            activeSeriesPolicyIds.Length == 0
                ? "No active transaction-scoped series policy is configured; legacy default-series behavior remains in effect."
                : "Active voucher or statutory-document series policies are retained with company-scoped allocation rules.",
            "Configure explicit source and transaction-type series policies before relying on advanced multi-series allocation.",
            activeSeriesPolicyIds));

        signals.Add(Signal("inventory_accounting_capability", AccountingReadinessStatuses.Ready, 0, null,
            "Native inventory quantity, valuation, and cost-of-goods-sold accounting are explicitly unsupported; Finance accepts only the versioned commerce accounting boundary.",
            "Keep quantity state in the commerce system and reject events that require native inventory accounting until a complete reviewed subledger is delivered."));

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
            ApprovalTargetEntityType.FinanceIntegrationWrite.ToStorageValue(),
            ApprovalTargetEntityType.CurrencyRevaluationRun.ToStorageValue(),
            ApprovalTargetEntityType.AccountingAllocation.ToStorageValue(),
            ApprovalTargetEntityType.AccountingSchedule.ToStorageValue()
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

        var staleVatReturnIds = await _dbContext.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FilingPeriod.EndDate < today &&
                x.Status != VatReturnStatuses.Locked && x.Status != VatReturnStatuses.Corrected)
            .OrderBy(x => x.FilingPeriod.EndDate).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("stale_vat_returns",
            staleVatReturnIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            staleVatReturnIds.Length,
            null,
            staleVatReturnIds.Length == 0
                ? "No ended VAT filing period has an unfinished return."
                : "One or more ended VAT filing periods have an unfinished return.",
            "Recalculate, review, approve, and finalize each current VAT return or complete its correction workflow.",
            staleVatReturnIds));

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

        var failedOrExpiredStatutoryExportIds = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.ExportType == AccountingExportTypeValues.Sie4B ||
                 x.ExportType == AccountingExportTypeValues.SwedishStatutoryArchive) &&
                (x.Status == AccountingExportStatuses.Failed ||
                 x.Status == AccountingExportStatuses.Completed && x.ExpiresUtc <= nowUtc))
            .OrderBy(x => x.ExpiresUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("failed_or_expired_statutory_exports",
            failedOrExpiredStatutoryExportIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Attention,
            failedOrExpiredStatutoryExportIds.Length,
            null,
            failedOrExpiredStatutoryExportIds.Length == 0
                ? "No failed or expired Swedish statutory export is recorded."
                : "A Swedish statutory export failed or its retained downloadable content expired.",
            "Regenerate from immutable accounting facts, verify the new checksum, and retain the prior export metadata and failure evidence.",
            failedOrExpiredStatutoryExportIds));

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

        var approvedRatePairs = await _dbContext.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.EffectiveDate <= today &&
                x.RateSet.Status == ExchangeRateSetStatuses.Approved &&
                x.RateSet.EffectiveThrough >= today)
            .Select(x => new { x.BaseCurrency, x.QuoteCurrency }).Distinct().ToArrayAsync(cancellationToken);
        var missingRateCurrencyIds = configuration is null
            ? Array.Empty<Guid>()
            : enabledForeignCurrencies.Where(currency => !approvedRatePairs.Any(pair =>
                    pair.BaseCurrency == configuration.BaseCurrency && pair.QuoteCurrency == currency.Code ||
                    pair.QuoteCurrency == configuration.BaseCurrency && pair.BaseCurrency == currency.Code))
                .Select(x => x.Id).ToArray();
        var failedRateRefreshIds = await _dbContext.ExchangeRateRefreshJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ExchangeRateRefreshJobStatuses.Failed)
            .OrderByDescending(x => x.UpdatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var rateBlockers = missingRateCurrencyIds.Concat(failedRateRefreshIds).Take(25).ToArray();
        signals.Add(Signal("advanced_rate_coverage",
            rateBlockers.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            missingRateCurrencyIds.Length + failedRateRefreshIds.Length, null,
            rateBlockers.Length == 0
                ? "Every enabled foreign currency has current approved rate coverage and no failed refresh job remains."
                : "Enabled foreign currencies lack current approved coverage or a rate refresh failed.",
            "Restore or approve the exact dated rate set, resolve failed refresh evidence, and rerun readiness before posting or close.",
            rateBlockers));

        var unreconciledRevaluationIds = await _dbContext.CurrencyRevaluationReconciliations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (!x.IsReconciled || x.Difference != 0m))
            .OrderBy(x => x.RunId).Take(25).Select(x => x.RunId).ToArrayAsync(cancellationToken);
        var currencyControlIds = incompleteRevaluationIds.Concat(unreconciledRevaluationIds).Distinct().Take(25).ToArray();
        signals.Add(Signal("advanced_currency_controls",
            currencyControlIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            incompleteRevaluationIds.Length + unreconciledRevaluationIds.Length, null,
            currencyControlIds.Length == 0
                ? "Currency revaluation runs and retained control reconciliations have no unresolved difference."
                : "Currency revaluation has failed, review-bound, approval-bound, or unreconciled state.",
            "Resolve the retained population, rate, proposal, approval, journal, and reconciliation evidence; never replace it with a manual current-rate entry.",
            currencyControlIds));

        var allocationDifferenceIds = await _dbContext.AccountingAllocationApplications.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SourceAmount != x.AllocatedAmount)
            .OrderBy(x => x.CreatedUtc).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var dimensionControlIds = dimensionConflictIds.Concat(allocationDifferenceIds).Take(25).ToArray();
        signals.Add(Signal("advanced_dimension_controls",
            dimensionControlIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            dimensionConflictIds.Length + allocationDifferenceIds.Length, null,
            dimensionControlIds.Length == 0
                ? "Dimension mappings are resolved and allocation applications reconcile to their source amounts."
                : "Dimension mapping conflicts or allocation differences remain unresolved.",
            "Resolve provider mappings and regenerate the exact versioned allocation from retained evidence before close.",
            dimensionControlIds));

        var overdueScheduleIds = await _dbContext.AccountingScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                x.PostingDate < today &&
                 (x.Status == AccountingScheduleOccurrenceStatuses.Pending ||
                  x.Status == AccountingScheduleOccurrenceStatuses.Processing))
            .OrderBy(x => x.PostingDate).Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var scheduleControlIds = scheduleExceptionIds.Concat(overdueScheduleIds).Distinct().Take(25).ToArray();
        signals.Add(Signal("advanced_schedule_controls",
            scheduleControlIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            scheduleControlIds.Length, null,
            scheduleControlIds.Length == 0
                ? "No failed, blocked, or overdue accounting schedule occurrence remains."
                : "Accounting schedule occurrences are failed, blocked, or overdue.",
            "Recover the durable occurrence by its stable identity, correct the visible cause, and regenerate without duplicating posted or reversal journals.",
            scheduleControlIds));

        var failedDepreciationItemIds = await _dbContext.FixedAssetDepreciationRunItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == "failed")
            .Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var assetControlIds = fixedAssetConflictIds.Concat(failedDepreciationItemIds).Take(25).ToArray();
        signals.Add(Signal("advanced_asset_controls",
            assetControlIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            fixedAssetConflictIds.Length + failedDepreciationItemIds.Length, null,
            assetControlIds.Length == 0
                ? "The fixed-asset subledger has no open migration conflict or failed depreciation item."
                : "The fixed-asset subledger has migration ambiguity or failed depreciation work.",
            "Resolve source evidence or correct and replay the retained depreciation item; asset history must remain linked through adjustments or reversals.",
            assetControlIds));

        var voucherRows = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == LedgerEntryStatuses.Posted &&
                x.VoucherSeriesId.HasValue && x.VoucherFiscalYear.HasValue && x.VoucherSequenceNumber.HasValue)
            .Select(x => new { SeriesId = x.VoucherSeriesId!.Value, FiscalYear = x.VoucherFiscalYear!.Value,
                Sequence = x.VoucherSequenceNumber!.Value }).ToArrayAsync(cancellationToken);
        var gapRows = await _dbContext.AccountingVoucherGapEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.VoucherSeriesId, x.FiscalYear, x.MissingNumber }).ToArrayAsync(cancellationToken);
        var unexplainedGapSeries = voucherRows.GroupBy(x => new { x.SeriesId, x.FiscalYear })
            .Where(group => CountUnexplainedGaps(group.Select(x => x.Sequence),
                gapRows.Where(gap => gap.VoucherSeriesId == group.Key.SeriesId && gap.FiscalYear == group.Key.FiscalYear)
                    .Select(gap => gap.MissingNumber)) > 0)
            .Select(group => group.Key.SeriesId).Distinct().Take(25).ToArray();
        signals.Add(Signal("advanced_series_controls",
            unexplainedGapSeries.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            unexplainedGapSeries.Length, null,
            unexplainedGapSeries.Length == 0
                ? "Issued voucher identities are unique and every detected gap has retained operator evidence."
                : "One or more voucher series contains an unexplained issued-number gap.",
            "Record evidence for the actual unused number; never renumber an issued document or voucher.",
            unexplainedGapSeries));

        var invalidCommerceReceiptIds = await _dbContext.AccountingCommerceEventReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "accepted")
            .Take(25).Select(x => x.Id).ToArrayAsync(cancellationToken);
        signals.Add(Signal("inventory_capability_boundary",
            invalidCommerceReceiptIds.Length == 0 ? AccountingReadinessStatuses.Ready : AccountingReadinessStatuses.Blocked,
            invalidCommerceReceiptIds.Length, null,
            invalidCommerceReceiptIds.Length == 0
                ? "Inventory quantity, valuation, and COGS remain explicitly unsupported; accepted commerce facts contain no inventory state."
                : "A commerce receipt has an unsupported state at the inventory capability boundary.",
            "Keep quantity and valuation in the commerce system and submit only finance-commerce.v1 facts that do not request inventory accounting.",
            invalidCommerceReceiptIds));

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

    private static long CountUnexplainedGaps(IEnumerable<long> issuedNumbers, IEnumerable<long> explainedNumbers)
    {
        var issued = issuedNumbers.Distinct().OrderBy(x => x).ToArray();
        if (issued.Length < 2) return 0;
        var explained = explainedNumbers.ToHashSet();
        long total = 0;
        for (var index = 1; index < issued.Length; index++)
        {
            var firstMissing = issued[index - 1] + 1;
            var lastMissing = issued[index] - 1;
            if (lastMissing < firstMissing) continue;
            var gapSize = lastMissing - firstMissing + 1;
            var explainedCount = explained.LongCount(number => number >= firstMissing && number <= lastMissing);
            total += Math.Max(0, gapSize - explainedCount);
        }
        return total;
    }
}
