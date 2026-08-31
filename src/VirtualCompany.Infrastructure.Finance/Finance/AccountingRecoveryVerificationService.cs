using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingRecoveryVerificationService : IAccountingRecoveryVerificationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyDocumentStorage _documentStorage;
    private readonly IAuditEventWriter _auditWriter;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;

    public AccountingRecoveryVerificationService(
        VirtualCompanyDbContext dbContext,
        ICompanyDocumentStorage documentStorage,
        IAuditEventWriter auditWriter,
        AccountingOperationsTelemetry telemetry,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _documentStorage = documentStorage;
        _auditWriter = auditWriter;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingRecoveryVerificationDto> VerifyAsync(
        VerifyAccountingRecoveryCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(command));
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(command));
        if (command.FiscalPeriodId == Guid.Empty) throw new ArgumentException("FiscalPeriodId cannot be empty.", nameof(command));

        FiscalPeriod? requestedPeriod = null;
        if (command.FiscalPeriodId.HasValue)
        {
            requestedPeriod = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Accounting period was not found.");
        }

        var entryQuery = _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.Status == LedgerEntryStatuses.Posted);
        if (command.FiscalPeriodId.HasValue)
            entryQuery = entryQuery.Where(x => x.FiscalPeriodId == command.FiscalPeriodId.Value);
        var entries = await entryQuery.OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber)
            .Select(x => new RecoveryEntryRow(x.Id, x.FiscalPeriodId, x.EntryNumber, x.EntryUtc,
                x.VoucherSeriesId, x.VoucherFiscalYear, x.VoucherSequenceNumber, x.SourceType, x.SourceId,
                x.SourceVersion, x.IdempotencyKey, x.PolicyPackKey, x.PolicyPackVersion))
            .ToArrayAsync(cancellationToken);
        var entryIds = entries.Select(x => x.Id).ToArray();
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .OrderBy(x => x.LedgerEntryId).ThenBy(x => x.Id)
            .Select(x => new RecoveryLineRow(x.Id, x.LedgerEntryId, x.FinanceAccountId,
                x.DebitAmount, x.CreditAmount, x.Currency, x.DocumentDebitAmount,
                x.DocumentCreditAmount, x.DocumentCurrency, x.TaxFactsJson)).ToArrayAsync(cancellationToken);
        var sourceLinks = await _dbContext.LedgerEntrySourceMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new { x.LedgerEntryId, x.SourceType, x.SourceId }).ToArrayAsync(cancellationToken);
        var evidenceLinks = await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new RecoveryEvidenceRow(x.Id, x.LedgerEntryId, x.DocumentId, x.ContentHash,
                x.Document.StorageKey, x.Document.OriginalFileName)).ToArrayAsync(cancellationToken);
        var auditTargets = await _dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.TargetType == AuditTargetTypes.AccountingJournal)
            .Select(x => x.TargetId).ToArrayAsync(cancellationToken);
        var auditTargetSet = auditTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var statutoryExportsQuery = _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.Status == AccountingExportStatuses.Completed &&
                (x.ExportType == AccountingExportTypeValues.Sie4B || x.ExportType == AccountingExportTypeValues.SwedishStatutoryArchive));
        if (command.FiscalPeriodId.HasValue) statutoryExportsQuery = statutoryExportsQuery.Where(x => x.FiscalPeriodId == command.FiscalPeriodId.Value);
        var statutoryExports = await statutoryExportsQuery.OrderBy(x => x.RequestedUtc)
            .Select(x => new RecoveryArchiveRow(x.Id, x.FiscalPeriodId, x.ExportType, x.StorageKey, x.Checksum,
                x.InputChecksum, x.ManifestJson, x.ContentLength)).ToArrayAsync(cancellationToken);
        var vatPackageQuery = _dbContext.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.Status == VatReturnStatuses.Locked);
        if (command.FiscalPeriodId.HasValue) vatPackageQuery = vatPackageQuery.Where(x => x.FilingPeriod.FiscalPeriodId == command.FiscalPeriodId.Value);
        var vatPackages = await vatPackageQuery.OrderBy(x => x.FinalizedUtc)
            .Select(x => new RecoveryVatPackageRow(x.Id, x.FilingPeriodId, x.PackageStorageKey, x.PackageChecksum, x.PackageContentLength))
            .ToArrayAsync(cancellationToken);

        var dimensionFacts = await _dbContext.LedgerEntryLineDimensions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && lines.Select(line => line.Id).Contains(x.LedgerEntryLineId))
            .OrderBy(x => x.LedgerEntryLineId).ThenBy(x => x.DimensionTypeCodeSnapshot)
            .Select(x => new RecoveryDimensionRow(x.Id, x.LedgerEntryLineId, x.DimensionTypeId,
                x.DimensionMemberId, x.DimensionTypeCodeSnapshot, x.MemberCodeSnapshot, x.HierarchyPathSnapshot))
            .ToArrayAsync(cancellationToken);
        var allocations = await _dbContext.AccountingAllocationApplications.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Select(x => new RecoveryAllocationRow(x.Id, x.TemplateVersionId, x.SourceType, x.SourceId,
                x.SourceVersion, x.PayloadHash, x.SourceAmount, x.AllocatedAmount, x.Currency))
            .ToArrayAsync(cancellationToken);

        var revaluationQuery = _dbContext.CurrencyRevaluationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId);
        if (command.FiscalPeriodId.HasValue)
            revaluationQuery = revaluationQuery.Where(x => x.FiscalPeriodId == command.FiscalPeriodId.Value);
        var revaluations = await revaluationQuery.OrderBy(x => x.FiscalPeriodId).ThenBy(x => x.RunNumber)
            .Select(x => new RecoveryRevaluationRow(x.Id, x.FiscalPeriodId, x.RunNumber, x.Status,
                x.ProposalChecksum, x.LedgerEntryId, x.ReversalLedgerEntryId, x.ProposedAdjustmentTotal))
            .ToArrayAsync(cancellationToken);
        var revaluationIds = revaluations.Select(x => x.Id).ToArray();
        var revaluationReconciliations = await _dbContext.CurrencyRevaluationReconciliations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && revaluationIds.Contains(x.RunId))
            .OrderBy(x => x.RunId).ThenBy(x => x.ReconciliationType)
            .Select(x => new RecoveryRevaluationReconciliationRow(x.Id, x.RunId, x.ReconciliationType,
                x.ProposedAdjustment, x.ProposalLineAdjustment, x.Difference, x.Currency, x.Checksum, x.IsReconciled))
            .ToArrayAsync(cancellationToken);

        var occurrenceQuery = _dbContext.AccountingScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId);
        if (requestedPeriod is not null)
        {
            var periodStart = DateOnly.FromDateTime(requestedPeriod.StartUtc);
            var periodEndExclusive = DateOnly.FromDateTime(requestedPeriod.EndUtc);
            occurrenceQuery = occurrenceQuery.Where(x => x.PostingDate >= periodStart && x.PostingDate < periodEndExclusive);
        }
        var scheduleOccurrences = await occurrenceQuery.OrderBy(x => x.PostingDate).ThenBy(x => x.Id)
            .Select(x => new RecoveryScheduleRow(x.Id, x.ScheduleId, x.ScheduleVersionId, x.ScheduleVersionHash,
                x.PostingDate, x.ScheduledAmount, x.ReleasedAmount, x.ReversedAmount, x.Currency, x.Status,
                x.LedgerEntryId, x.ReversalLedgerEntryId, x.FailureCode))
            .ToArrayAsync(cancellationToken);

        var assetEventQuery = _dbContext.FixedAssetBookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId);
        if (command.FiscalPeriodId.HasValue)
            assetEventQuery = assetEventQuery.Where(x => x.FiscalPeriodId == command.FiscalPeriodId.Value);
        var assetEvents = await assetEventQuery.OrderBy(x => x.EffectiveDate).ThenBy(x => x.Id)
            .Select(x => new RecoveryAssetEventRow(x.Id, x.AssetId, x.EventType, x.EffectiveDate, x.Amount,
                x.DepreciationMovement, x.ImpairmentMovement, x.Proceeds, x.GainLoss, x.SnapshotHash,
                x.Status, x.LedgerEntryId, x.OriginalEventId))
            .ToArrayAsync(cancellationToken);
        var depreciationRunQuery = _dbContext.FixedAssetDepreciationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId);
        if (command.FiscalPeriodId.HasValue)
            depreciationRunQuery = depreciationRunQuery.Where(x => x.FiscalPeriodId == command.FiscalPeriodId.Value);
        var depreciationRuns = await depreciationRunQuery.OrderBy(x => x.PeriodStart).ThenBy(x => x.Id)
            .Select(x => new RecoveryDepreciationRunRow(x.Id, x.FiscalPeriodId, x.PeriodStart, x.PeriodEnd,
                x.PopulationHash, x.Status, x.TotalAmount, x.PostedItemCount, x.ExceptionCount))
            .ToArrayAsync(cancellationToken);
        var depreciationRunIds = depreciationRuns.Select(x => x.Id).ToArray();
        var depreciationItems = await _dbContext.FixedAssetDepreciationRunItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && depreciationRunIds.Contains(x.RunId))
            .OrderBy(x => x.RunId).ThenBy(x => x.AssetId)
            .Select(x => new RecoveryDepreciationItemRow(x.Id, x.RunId, x.AssetId, x.AssetVersion,
                x.AssetClassHash, x.Amount, x.Status, x.LedgerEntryId, x.FailureCode))
            .ToArrayAsync(cancellationToken);

        var voucherGapEvidence = await _dbContext.AccountingVoucherGapEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .OrderBy(x => x.VoucherSeriesId).ThenBy(x => x.FiscalYear).ThenBy(x => x.MissingNumber)
            .Select(x => new RecoveryVoucherGapRow(x.Id, x.VoucherSeriesId, x.FiscalYear,
                x.MissingNumber, x.Reason, x.RecordedUtc))
            .ToArrayAsync(cancellationToken);
        var commerceReceipts = await _dbContext.AccountingCommerceEventReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .OrderBy(x => x.EventId).ThenBy(x => x.EventVersion)
            .Select(x => new RecoveryCommerceRow(x.Id, x.EventId, x.EventVersion, x.ContractVersion,
                x.EventType, x.SourceSystem, x.Status))
            .ToArrayAsync(cancellationToken);
        var issues = new List<AccountingRecoveryIssueDto>();

        foreach (var currencyGroup in lines.Where(x => !string.IsNullOrWhiteSpace(x.DocumentCurrency))
                     .GroupBy(x => x.DocumentCurrency!, StringComparer.OrdinalIgnoreCase))
        {
            var difference = currencyGroup.Sum(x => x.DocumentDebitAmount - x.DocumentCreditAmount);
            if (Math.Abs(difference) > 0.0001m)
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreDocumentCurrencyUnbalanced,
                    $"Restored journal lines do not balance in document currency {currencyGroup.Key}.",
                    "document_currency", currencyGroup.Key));
        }

        foreach (var allocation in allocations.Where(x => Math.Abs(x.SourceAmount - x.AllocatedAmount) > 0.0001m))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreAllocationUnbalanced,
                "A restored accounting allocation does not reconcile to its retained source amount.",
                "accounting_allocation", allocation.Id));

        foreach (var reconciliation in revaluationReconciliations.Where(x => !x.IsReconciled || Math.Abs(x.Difference) > 0.0001m))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreRevaluationUnreconciled,
                "A restored currency revaluation control does not reconcile to its proposal lines.",
                "currency_revaluation_reconciliation", reconciliation.Id));
        foreach (var run in revaluations.Where(x =>
                     x.Status == CurrencyRevaluationRunStatuses.Posted && !x.LedgerEntryId.HasValue ||
                     x.Status == CurrencyRevaluationRunStatuses.Reversed &&
                     (!x.LedgerEntryId.HasValue || !x.ReversalLedgerEntryId.HasValue)))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreRevaluationJournalMissing,
                "A restored posted or reversed currency revaluation is missing its immutable journal link.",
                "currency_revaluation_run", run.Id));

        foreach (var occurrence in scheduleOccurrences.Where(x =>
                     x.Status == AccountingScheduleOccurrenceStatuses.Posted &&
                     (!x.LedgerEntryId.HasValue || x.ReleasedAmount != x.ScheduledAmount) ||
                     x.Status == AccountingScheduleOccurrenceStatuses.Reversed &&
                     (!x.LedgerEntryId.HasValue || !x.ReversalLedgerEntryId.HasValue || x.ReversedAmount != x.ReleasedAmount) ||
                     x.Status is AccountingScheduleOccurrenceStatuses.Blocked or AccountingScheduleOccurrenceStatuses.Failed))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreScheduleOccurrenceIncomplete,
                "A restored accounting schedule occurrence is incomplete, failed, or missing a journal link.",
                "accounting_schedule_occurrence", occurrence.Id));

        var journalRequiredAssetEvents = new[]
        {
            FixedAssetEventTypes.Capitalization, FixedAssetEventTypes.Depreciation,
            FixedAssetEventTypes.Impairment, FixedAssetEventTypes.Disposal, FixedAssetEventTypes.Reversal
        };
        foreach (var assetEvent in assetEvents.Where(x => x.Status == "posted" &&
                     journalRequiredAssetEvents.Contains(x.EventType) && !x.LedgerEntryId.HasValue))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreAssetPostingIncomplete,
                "A restored fixed-asset book event is missing its immutable journal link.",
                "fixed_asset_book_event", assetEvent.Id));
        foreach (var item in depreciationItems.Where(x =>
                     x.Status == "posted" && !x.LedgerEntryId.HasValue || x.Status == "failed"))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreAdvancedWorkerFailure,
                "A restored depreciation item failed or is missing its immutable journal link.",
                "fixed_asset_depreciation_item", item.Id));

        foreach (var export in statutoryExports.Where(x => string.IsNullOrWhiteSpace(x.StorageKey) ||
                     string.IsNullOrWhiteSpace(x.Checksum) || string.IsNullOrWhiteSpace(x.InputChecksum) ||
                     string.IsNullOrWhiteSpace(x.ManifestJson) || x.ContentLength is null or <= 0))
            issues.Add(Issue(AccountingOperationsReasonCodes.RestoreStatutoryArchiveMetadataMissing,
                "A restored statutory export is missing its object reference, checksums, manifest, or content length.",
                "accounting_export", export.Id));

        foreach (var duplicate in entries.Where(x => x.VoucherSeriesId.HasValue && x.VoucherFiscalYear.HasValue && x.VoucherSequenceNumber.HasValue)
                     .GroupBy(x => new { x.VoucherSeriesId, x.VoucherFiscalYear, x.VoucherSequenceNumber })
                     .Where(x => x.Count() > 1))
        {
            foreach (var entry in duplicate)
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreVoucherDuplicate,
                    "A voucher series, fiscal year, and sequence number is used by more than one journal.",
                    "ledger_entry", entry.Id));
        }

        foreach (var entry in entries)
        {
            var entryLines = lines.Where(x => x.LedgerEntryId == entry.Id).ToArray();
            if (entryLines.Length < 2 || Math.Abs(entryLines.Sum(x => x.DebitAmount) - entryLines.Sum(x => x.CreditAmount)) > 0.0001m)
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreJournalUnbalanced,
                    "The restored journal does not contain at least two balanced lines.", "ledger_entry", entry.Id));
            if (!string.IsNullOrWhiteSpace(entry.SourceType) && !string.IsNullOrWhiteSpace(entry.SourceId) &&
                !sourceLinks.Any(x => x.LedgerEntryId == entry.Id &&
                    string.Equals(x.SourceType, entry.SourceType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.SourceId, entry.SourceId, StringComparison.OrdinalIgnoreCase)))
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreSourceLinkMissing,
                    "The restored journal source does not have a matching immutable source link.", "ledger_entry", entry.Id));
            if (!auditTargetSet.Contains(entry.Id.ToString("N")) && !auditTargetSet.Contains(entry.Id.ToString("D")))
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreAuditReferenceMissing,
                    "The restored journal has no accounting audit reference.", "ledger_entry", entry.Id));
        }

        if (command.VerifyObjectContent)
        {
            foreach (var evidence in evidenceLinks)
            {
                try
                {
                    await using var stream = await _documentStorage.OpenReadAsync(evidence.StorageKey, cancellationToken);
                    var actualHash = await ComputeSha256Async(stream, cancellationToken);
                    if (!string.Equals(actualHash, evidence.ContentHash, StringComparison.OrdinalIgnoreCase))
                        issues.Add(Issue(AccountingOperationsReasonCodes.RestoreDocumentHashMismatch,
                            "The restored source document content does not match the hash retained with the journal.",
                            "document", evidence.DocumentId));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    issues.Add(Issue(AccountingOperationsReasonCodes.RestoreDocumentMissing,
                        "The restored source document cannot be opened from object storage.", "document", evidence.DocumentId));
                }
            }
            foreach (var export in statutoryExports.Where(x => !string.IsNullOrWhiteSpace(x.StorageKey) && !string.IsNullOrWhiteSpace(x.Checksum)))
                await VerifyStoredObjectAsync(export.StorageKey!, export.Checksum!, export.Id,
                    AccountingOperationsReasonCodes.RestoreStatutoryArchiveMissing,
                    AccountingOperationsReasonCodes.RestoreStatutoryArchiveHashMismatch,
                    "accounting_export", issues, cancellationToken);
            foreach (var package in vatPackages.Where(x => !string.IsNullOrWhiteSpace(x.StorageKey) && !string.IsNullOrWhiteSpace(x.Checksum)))
                await VerifyStoredObjectAsync(package.StorageKey!, package.Checksum!, package.Id,
                    AccountingOperationsReasonCodes.RestoreVatPackageMissing,
                    AccountingOperationsReasonCodes.RestoreVatPackageHashMismatch,
                    "vat_return", issues, cancellationToken);
            foreach (var package in vatPackages.Where(x => string.IsNullOrWhiteSpace(x.StorageKey) || string.IsNullOrWhiteSpace(x.Checksum) || x.ContentLength is null or <= 0))
                issues.Add(Issue(AccountingOperationsReasonCodes.RestoreVatPackageMissing,
                    "A finalized VAT package is missing its object reference, checksum, or content length.", "vat_return", package.Id));
        }

        var periods = command.FiscalPeriodId.HasValue
            ? new[] { requestedPeriod! }
            : await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId).OrderBy(x => x.StartUtc).ToArrayAsync(cancellationToken);
        var snapshotCount = 0;
        foreach (var period in periods)
        {
            var snapshots = await _dbContext.TrialBalanceSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id)
                .Select(x => new { x.Id, x.FinanceAccountId, x.BalanceAmount }).ToArrayAsync(cancellationToken);
            snapshotCount += snapshots.Length + await _dbContext.FinancialStatementSnapshots.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id, cancellationToken);
            if (snapshots.Length == 0) continue;
            var accountOpenings = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId)
                .ToDictionaryAsync(x => x.Id, x => x.OpeningBalance, cancellationToken);
            var cumulative = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.EntryUtc < period.EndUtc)
                .GroupBy(x => x.FinanceAccountId)
                .Select(x => new { AccountId = x.Key, Amount = x.Sum(line => line.DebitAmount - line.CreditAmount) })
                .ToDictionaryAsync(x => x.AccountId, x => x.Amount, cancellationToken);
            foreach (var snapshot in snapshots)
            {
                var expected = accountOpenings.GetValueOrDefault(snapshot.FinanceAccountId) + cumulative.GetValueOrDefault(snapshot.FinanceAccountId);
                if (Math.Abs(expected - snapshot.BalanceAmount) > 0.0001m)
                    issues.Add(Issue(AccountingOperationsReasonCodes.RestoreSnapshotMismatch,
                        "A restored trial-balance snapshot does not match the posted ledger through the period end.",
                        "trial_balance_snapshot", snapshot.Id));
            }
        }

        var providerReferenceCount = await _dbContext.FinanceExternalReferences.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == command.CompanyId, cancellationToken) +
            await _dbContext.FortnoxExternalReferences.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == command.CompanyId, cancellationToken) +
            await _dbContext.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        var totalDebit = lines.Sum(x => x.DebitAmount);
        var totalCredit = lines.Sum(x => x.CreditAmount);
        var controls = new[]
        {
            Control("functional_currency", lines.Length, totalDebit, totalCredit, lines),
            Control("document_currency", lines.Count(x => !string.IsNullOrWhiteSpace(x.DocumentCurrency)),
                lines.Sum(x => x.DocumentDebitAmount), lines.Sum(x => x.DocumentCreditAmount),
                lines.Where(x => !string.IsNullOrWhiteSpace(x.DocumentCurrency)).ToArray(),
                hasBlockingIssue: issues.Any(x => x.ReasonCode == AccountingOperationsReasonCodes.RestoreDocumentCurrencyUnbalanced)),
            Control("dimensions_and_allocations", dimensionFacts.Length + allocations.Length,
                allocations.Sum(x => x.SourceAmount), allocations.Sum(x => x.AllocatedAmount),
                new { dimensionFacts, allocations },
                hasBlockingIssue: issues.Any(x => x.ReasonCode == AccountingOperationsReasonCodes.RestoreAllocationUnbalanced)),
            Control("currency_revaluation", revaluations.Length + revaluationReconciliations.Length,
                revaluationReconciliations.Sum(x => x.ProposedAdjustment),
                revaluationReconciliations.Sum(x => x.ProposalLineAdjustment),
                new { revaluations, revaluationReconciliations },
                hasBlockingIssue: issues.Any(x => x.ReasonCode is
                    AccountingOperationsReasonCodes.RestoreRevaluationUnreconciled or
                    AccountingOperationsReasonCodes.RestoreRevaluationJournalMissing)),
            Control("accounting_schedules", scheduleOccurrences.Length,
                scheduleOccurrences.Sum(x => x.ReleasedAmount), scheduleOccurrences.Sum(x => x.ReversedAmount),
                scheduleOccurrences, requiresBalance: false,
                hasBlockingIssue: issues.Any(x => x.ReasonCode == AccountingOperationsReasonCodes.RestoreScheduleOccurrenceIncomplete)),
            Control("fixed_assets", assetEvents.Length + depreciationRuns.Length + depreciationItems.Length,
                assetEvents.Sum(x => x.Amount), depreciationItems.Sum(x => x.Amount),
                new { assetEvents, depreciationRuns, depreciationItems }, requiresBalance: false,
                hasBlockingIssue: issues.Any(x => x.ReasonCode is
                    AccountingOperationsReasonCodes.RestoreAssetPostingIncomplete or
                    AccountingOperationsReasonCodes.RestoreAdvancedWorkerFailure)),
            Control("series_and_commerce_boundary", voucherGapEvidence.Length + commerceReceipts.Length,
                voucherGapEvidence.Length, commerceReceipts.Length,
                new { voucherGapEvidence, commerceReceipts }, requiresBalance: false,
                hasBlockingIssue: issues.Any(x => x.ReasonCode == AccountingOperationsReasonCodes.RestoreVoucherDuplicate))
        };
        var checksumInput = JsonSerializer.Serialize(new
        {
            command.CompanyId,
            command.FiscalPeriodId,
            entries,
            lines,
            sourceLinks = sourceLinks.OrderBy(x => x.LedgerEntryId).ThenBy(x => x.SourceType).ThenBy(x => x.SourceId),
            evidence = evidenceLinks.OrderBy(x => x.LedgerEntryId).ThenBy(x => x.DocumentId)
                .Select(x => new { x.LedgerEntryId, x.DocumentId, x.ContentHash, x.StorageKey }),
            auditTargets = auditTargets.OrderBy(x => x),
            snapshotCount,
            providerReferenceCount,
            statutoryExports,
            vatPackages,
            dimensionFacts,
            allocations,
            revaluations,
            revaluationReconciliations,
            scheduleOccurrences,
            assetEvents,
            depreciationRuns,
            depreciationItems,
            voucherGapEvidence,
            commerceReceipts,
            controls
        });
        var verifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = new AccountingRecoveryVerificationDto(command.CompanyId, command.FiscalPeriodId,
            command.VerifyObjectContent, entries.Count(x => x.VoucherSequenceNumber.HasValue), entries.Length,
            lines.Length, sourceLinks.Length, evidenceLinks.Length, auditTargets.Length, snapshotCount,
            providerReferenceCount, totalDebit, totalCredit, Sha256(checksumInput), issues.Count == 0,
            verifiedUtc, issues, controls);

        await _auditWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
            command.ActorUserId, AuditEventActions.AccountingRecoveryVerified, AuditTargetTypes.AccountingRecovery,
            command.FiscalPeriodId?.ToString("D") ?? command.CompanyId.ToString("D"),
            result.IsValid ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            result.IsValid
                ? "Accounting database and evidence integrity verification completed successfully."
                : "Accounting recovery verification found operator-visible integrity issues.",
            ["ledger_entries", "ledger_entry_lines", "source_documents", "audit_events", "report_snapshots", "provider_references", "statutory_archives", "vat_packages", "currency_rates", "dimensions", "schedules", "fixed_assets", "series"],
            new Dictionary<string, string?>
            {
                ["fiscalPeriodId"] = command.FiscalPeriodId?.ToString("D"),
                ["objectContentVerified"] = command.VerifyObjectContent.ToString(),
                ["journalCount"] = result.JournalCount.ToString(),
                ["issueCount"] = issues.Count.ToString(),
                ["evidenceChecksum"] = result.EvidenceChecksum
            }, command.CorrelationId, verifiedUtc), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.RecoveryVerified(command.CompanyId, command.FiscalPeriodId, result.IsValid,
            issues.Count, command.VerifyObjectContent, command.CorrelationId);
        return result;
    }

    private static AccountingRecoveryIssueDto Issue(string code, string explanation, string entityType, Guid entityId) =>
        new(code, explanation, entityType, entityId.ToString("D"), true);

    private static AccountingRecoveryIssueDto Issue(string code, string explanation, string entityType, string entityId) =>
        new(code, explanation, entityType, entityId, true);

    private static AccountingRecoveryControlDto Control(
        string key,
        int recordCount,
        decimal debit,
        decimal credit,
        object evidence,
        bool requiresBalance = true,
        bool hasBlockingIssue = false)
    {
        var difference = debit - credit;
        return new AccountingRecoveryControlDto(
            key,
            !hasBlockingIssue && (!requiresBalance || Math.Abs(difference) <= 0.0001m)
                ? AccountingReadinessStatuses.Ready
                : AccountingReadinessStatuses.Blocked,
            recordCount,
            debit,
            credit,
            difference,
            Sha256(JsonSerializer.Serialize(evidence)));
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            hasher.AppendData(buffer, 0, read);
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task VerifyStoredObjectAsync(string storageKey, string expectedHash, Guid entityId,
        string missingCode, string mismatchCode, string entityType, List<AccountingRecoveryIssueDto> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _documentStorage.OpenReadAsync(storageKey, cancellationToken);
            var actualHash = await ComputeSha256Async(stream, cancellationToken);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(mismatchCode, "A restored statutory archive object does not match its retained SHA-256 checksum.", entityType, entityId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            issues.Add(Issue(missingCode, "A restored statutory archive object cannot be opened from object storage.", entityType, entityId));
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record RecoveryEntryRow(Guid Id, Guid FiscalPeriodId, string EntryNumber, DateTime EntryUtc,
        Guid? VoucherSeriesId, int? VoucherFiscalYear, long? VoucherSequenceNumber, string? SourceType,
        string? SourceId, string? SourceVersion, string? IdempotencyKey, string? PolicyPackKey,
        string? PolicyPackVersion);
    private sealed record RecoveryLineRow(Guid Id, Guid LedgerEntryId, Guid FinanceAccountId,
        decimal DebitAmount, decimal CreditAmount, string Currency, decimal DocumentDebitAmount,
        decimal DocumentCreditAmount, string? DocumentCurrency, string? TaxFactsJson);
    private sealed record RecoveryEvidenceRow(Guid Id, Guid LedgerEntryId, Guid DocumentId,
        string ContentHash, string StorageKey, string OriginalFileName);
    private sealed record RecoveryArchiveRow(Guid Id, Guid FiscalPeriodId, string ExportType, string? StorageKey,
        string? Checksum, string? InputChecksum, string? ManifestJson, long? ContentLength);
    private sealed record RecoveryVatPackageRow(Guid Id, Guid FilingPeriodId, string? StorageKey,
        string? Checksum, long? ContentLength);
    private sealed record RecoveryDimensionRow(Guid Id, Guid LedgerEntryLineId, Guid DimensionTypeId,
        Guid DimensionMemberId, string DimensionTypeCode, string DimensionMemberCode, string HierarchyPath);
    private sealed record RecoveryAllocationRow(Guid Id, Guid TemplateVersionId, string SourceType,
        string SourceId, string SourceVersion, string PayloadHash, decimal SourceAmount,
        decimal AllocatedAmount, string Currency);
    private sealed record RecoveryRevaluationRow(Guid Id, Guid FiscalPeriodId, int RunNumber, string Status,
        string? ProposalChecksum, Guid? LedgerEntryId, Guid? ReversalLedgerEntryId, decimal ProposedAdjustmentTotal);
    private sealed record RecoveryRevaluationReconciliationRow(Guid Id, Guid RunId, string ReconciliationType,
        decimal ProposedAdjustment, decimal ProposalLineAdjustment, decimal Difference, string Currency,
        string Checksum, bool IsReconciled);
    private sealed record RecoveryScheduleRow(Guid Id, Guid ScheduleId, Guid ScheduleVersionId,
        string ScheduleVersionHash, DateOnly PostingDate, decimal ScheduledAmount, decimal ReleasedAmount,
        decimal ReversedAmount, string Currency, string Status, Guid? LedgerEntryId,
        Guid? ReversalLedgerEntryId, string? FailureCode);
    private sealed record RecoveryAssetEventRow(Guid Id, Guid AssetId, string EventType, DateOnly EffectiveDate,
        decimal Amount, decimal DepreciationMovement, decimal ImpairmentMovement, decimal Proceeds,
        decimal GainLoss, string SnapshotHash, string Status, Guid? LedgerEntryId, Guid? OriginalEventId);
    private sealed record RecoveryDepreciationRunRow(Guid Id, Guid FiscalPeriodId, DateOnly PeriodStart,
        DateOnly PeriodEnd, string PopulationHash, string Status, decimal TotalAmount,
        int PostedItemCount, int ExceptionCount);
    private sealed record RecoveryDepreciationItemRow(Guid Id, Guid RunId, Guid AssetId, long AssetVersion,
        string AssetClassHash, decimal Amount, string Status, Guid? LedgerEntryId, string? FailureCode);
    private sealed record RecoveryVoucherGapRow(Guid Id, Guid VoucherSeriesId, int FiscalYear,
        long MissingNumber, string Reason, DateTime RecordedUtc);
    private sealed record RecoveryCommerceRow(Guid Id, Guid EventId, long EventVersion,
        string ContractVersion, string EventType, string SourceSystem, string Status);
}
