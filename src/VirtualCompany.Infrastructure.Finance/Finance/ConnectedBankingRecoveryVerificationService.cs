using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ConnectedBankingRecoveryVerificationService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage documentStorage,
    TimeProvider timeProvider,
    ICompanyContextAccessor? companyContext = null) : IConnectedBankingRecoveryVerificationService
{
    public async Task<ConnectedBankingRecoveryVerificationDto> VerifyAsync(
        VerifyConnectedBankingRecoveryCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(command));

        var issues = new List<ConnectedBankingRecoveryIssueDto>();
        await AddDuplicateIssuesAsync(command.CompanyId, issues, cancellationToken);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var counts = new RecoveryCounts();
        await AppendDatabaseEvidenceAsync(command.CompanyId, hash, counts, cancellationToken);

        var imports = await db.BankStatementImportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId)
            .OrderBy(x => x.StorageKey).ThenBy(x => x.Id)
            .Select(x => new StatementObjectRow(x.Id, x.StorageKey, x.Checksum, x.ContentLength))
            .ToArrayAsync(cancellationToken);
        counts.StatementImports = imports.Length;
        foreach (var item in imports)
        {
            Append(hash, "statement_import", item.Id, item.StorageKey, item.Checksum, item.ContentLength);
            if (command.VerifyObjectContent)
                await VerifyStatementObjectAsync(item, issues, cancellationToken);
        }

        var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new ConnectedBankingRecoveryVerificationDto(
            command.CompanyId,
            command.VerifyObjectContent,
            counts.Connections,
            counts.FeedSourceObjects,
            counts.FeedTransactions,
            counts.StatementImports,
            counts.PaymentExecutions,
            counts.Acknowledgements,
            counts.WebhookReceipts,
            counts.Settlements,
            counts.ReconciliationResults,
            checksum,
            issues.Count == 0,
            timeProvider.GetUtcNow().UtcDateTime,
            issues);
    }

    private async Task AppendDatabaseEvidenceAsync(Guid companyId, IncrementalHash hash,
        RecoveryCounts counts, CancellationToken cancellationToken)
    {
        await foreach (var row in db.BankConnections.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.ProviderKey).ThenBy(x => x.InstitutionId).ThenBy(x => x.Id)
                           .Select(x => new { x.Id, x.ProviderKey, x.InstitutionId, x.Status, x.Version })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "connection", row.Id, row.ProviderKey, row.InstitutionId, row.Status, row.Version);
            counts.Connections++;
        }

        await foreach (var row in db.BankFeedRawSourceObjects.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.CheckpointId).ThenBy(x => x.SourceIdentity).ThenBy(x => x.Id)
                           .Select(x => new { x.Id, x.CheckpointId, x.SourceIdentity, x.SourceKind, x.Checksum })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "feed_source", row.Id, row.CheckpointId, row.SourceIdentity, row.SourceKind, row.Checksum);
            counts.FeedSourceObjects++;
        }

        await foreach (var row in db.BankFeedSourceTransactions.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.CheckpointId).ThenBy(x => x.StableIdentity).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.CheckpointId, x.StableIdentity, x.Status, x.ContentHash,
                               x.BankTransactionId, x.Version
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "feed_transaction", row.Id, row.CheckpointId, row.StableIdentity, row.Status,
                row.ContentHash, row.BankTransactionId, row.Version);
            counts.FeedTransactions++;
        }

        await foreach (var row in db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.BankAccountId).ThenBy(x => x.ImportSource)
                           .ThenBy(x => x.RowIdentity).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.BankAccountId, x.BookingDate, x.ValueDate, x.Amount, x.Currency,
                               x.Status, x.ReconciledAmount, x.ExternalReference, x.ImportSource,
                               x.RowIdentity, x.RowContentHash, x.SourceVersion
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "bank_transaction", row.Id, row.BankAccountId, row.BookingDate, row.ValueDate,
                row.Amount, row.Currency, row.Status, row.ReconciledAmount, row.ExternalReference,
                row.ImportSource, row.RowIdentity, row.RowContentHash, row.SourceVersion);
        }

        await foreach (var row in db.BankStatementImportJobRows.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.JobId).ThenBy(x => x.RowNumber).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.JobId, x.RowNumber, x.RowIdentity, x.RowHash, x.Outcome,
                               x.ImportedBankTransactionId
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "statement_row", row.Id, row.JobId, row.RowNumber, row.RowIdentity,
                row.RowHash, row.Outcome, row.ImportedBankTransactionId);
        }

        await foreach (var row in db.PaymentInstructions.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.BatchId).ThenBy(x => x.InstructionSetVersion)
                           .ThenBy(x => x.Sequence).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.BatchId, x.ObligationLinkId, x.InstructionSetVersion, x.Sequence,
                               x.ExecutionDate, x.Amount, x.Currency, x.PaymentReference, x.Rail,
                               x.Destination, x.SourceVersion, x.SourceHash, x.ContentHash, x.Status, x.IsCurrent
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "payment_instruction", row.Id, row.BatchId, row.ObligationLinkId,
                row.InstructionSetVersion, row.Sequence, row.ExecutionDate, row.Amount, row.Currency,
                row.PaymentReference, row.Rail, row.Destination, row.SourceVersion, row.SourceHash,
                row.ContentHash, row.Status, row.IsCurrent);
        }

        await foreach (var row in db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.BusinessIdempotencyKey).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.BatchId, x.BusinessIdempotencyKey, x.RequestHash, x.ProviderKey,
                               x.ProviderPaymentId, x.Status, x.Version
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "payment_execution", row.Id, row.BatchId, row.BusinessIdempotencyKey,
                row.RequestHash, row.ProviderKey, row.ProviderPaymentId, row.Status, row.Version);
            counts.PaymentExecutions++;
        }

        await foreach (var row in db.PaymentProviderAcknowledgements.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.ExecutionId).ThenBy(x => x.EventIdentity).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.ExecutionId, x.EventIdentity, x.Source, x.ProviderStatus,
                               x.NormalizedStatus, x.EvidenceHash
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "acknowledgement", row.Id, row.ExecutionId, row.EventIdentity, row.Source,
                row.ProviderStatus, row.NormalizedStatus, row.EvidenceHash);
            counts.Acknowledgements++;
        }

        await foreach (var row in db.PaymentProviderWebhookReceipts.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.ProviderKey).ThenBy(x => x.WebhookId).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.ExecutionId, x.ProviderKey, x.WebhookId, x.ProviderPaymentId,
                               x.ProviderStatus, x.PayloadHash
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "webhook", row.Id, row.ExecutionId, row.ProviderKey, row.WebhookId,
                row.ProviderPaymentId, row.ProviderStatus, row.PayloadHash);
            counts.WebhookReceipts++;
        }

        await foreach (var row in db.PaymentBatchSettlements.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.ExecutionId).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.ExecutionId, x.BankTransactionId, x.BankReference, x.Amount,
                               x.Currency, x.PaymentCount, x.AllocationCount, x.LedgerEntryIdsJson
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "settlement", row.Id, row.ExecutionId, row.BankTransactionId, row.BankReference,
                row.Amount, row.Currency, row.PaymentCount, row.AllocationCount, row.LedgerEntryIdsJson);
            counts.Settlements++;
        }

        await foreach (var row in db.Payments.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.PaymentDate).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.PaymentType, x.Amount, x.Currency, x.PaymentDate, x.Method,
                               x.Status, x.CounterpartyReference
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "payment", row.Id, row.PaymentType, row.Amount, row.Currency, row.PaymentDate,
                row.Method, row.Status, row.CounterpartyReference);
        }

        await foreach (var row in db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.PaymentId).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.PaymentId, x.InvoiceId, x.BillId, x.AllocatedAmount, x.Currency,
                               x.IdempotencyKey
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "payment_allocation", row.Id, row.PaymentId, row.InvoiceId, row.BillId,
                row.AllocatedAmount, row.Currency, row.IdempotencyKey);
        }

        await foreach (var row in db.BankTransactionCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.BankTransactionId).ThenBy(x => x.LedgerEntryId).ThenBy(x => x.Id)
                           .Select(x => new { x.Id, x.BankTransactionId, x.LedgerEntryId, x.IdempotencyKey })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "bank_ledger_link", row.Id, row.BankTransactionId, row.LedgerEntryId,
                row.IdempotencyKey);
        }

        await foreach (var row in db.PaymentCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.PaymentId).ThenBy(x => x.LedgerEntryId).ThenBy(x => x.Id)
                           .Select(x => new { x.Id, x.PaymentId, x.LedgerEntryId, x.SourceType, x.SourceId })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "payment_ledger_link", row.Id, row.PaymentId, row.LedgerEntryId,
                row.SourceType, row.SourceId);
        }

        await foreach (var row in db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId &&
                               (db.BankTransactionCashLedgerLinks.IgnoreQueryFilters().Any(link =>
                                    link.CompanyId == companyId && link.LedgerEntryId == x.Id) ||
                                db.PaymentCashLedgerLinks.IgnoreQueryFilters().Any(link =>
                                    link.CompanyId == companyId && link.LedgerEntryId == x.Id)))
                           .OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.FiscalPeriodId, x.EntryNumber, x.EntryUtc, x.Status, x.SourceType,
                               x.SourceId, x.PostingDate, x.BaseCurrency, x.SourceVersion, x.IdempotencyKey
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "linked_ledger_entry", row.Id, row.FiscalPeriodId, row.EntryNumber, row.EntryUtc,
                row.Status, row.SourceType, row.SourceId, row.PostingDate, row.BaseCurrency,
                row.SourceVersion, row.IdempotencyKey);
        }

        await foreach (var row in db.AdvancedReconciliationResults.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.GroupId).ThenBy(x => x.CreatedUtc).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.GroupId, x.ParentResultId, x.Outcome, x.GroupVersion, x.RuleVersion,
                               x.ExpectedBankTotal, x.AllocatedAmount, x.FeeAmount, x.RoundingAmount,
                               x.ResidualAmount, x.EvidenceJson
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "reconciliation_result", row.Id, row.GroupId, row.ParentResultId, row.Outcome,
                row.GroupVersion, row.RuleVersion, row.ExpectedBankTotal, row.AllocatedAmount,
                row.FeeAmount, row.RoundingAmount, row.ResidualAmount, row.EvidenceJson);
            counts.ReconciliationResults++;
        }

        await foreach (var row in db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
                           .Where(x => x.CompanyId == companyId)
                           .OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Id, x.ActorType, x.ActorId, x.Action, x.TargetType, x.TargetId,
                               x.Outcome, x.CorrelationId, x.OccurredUtc
                           })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            Append(hash, "audit", row.Id, row.ActorType, row.ActorId, row.Action, row.TargetType,
                row.TargetId, row.Outcome, row.CorrelationId, row.OccurredUtc);
        }
    }

    private async Task AddDuplicateIssuesAsync(Guid companyId,
        ICollection<ConnectedBankingRecoveryIssueDto> issues, CancellationToken cancellationToken)
    {
        var duplicateFeed = await db.BankFeedSourceTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.CheckpointId, x.StableIdentity })
            .Where(group => group.Count() > 1).CountAsync(cancellationToken);
        if (duplicateFeed > 0)
            issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.DuplicateFeedIdentity,
                $"{duplicateFeed} duplicate feed transaction identity group(s) were found.",
                "bank_feed_source_transaction", companyId));

        var duplicateBankRows = await db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RowIdentity != null && x.ImportSource != null)
            .GroupBy(x => new { x.BankAccountId, x.ImportSource, x.RowIdentity })
            .Where(group => group.Count() > 1).CountAsync(cancellationToken);
        if (duplicateBankRows > 0)
            issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.DuplicateBankRowIdentity,
                $"{duplicateBankRows} duplicate normalized bank-row identity group(s) were found.",
                "bank_transaction", companyId));

        var duplicatePayment = await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.BusinessIdempotencyKey)
            .Where(group => group.Count() > 1).CountAsync(cancellationToken);
        if (duplicatePayment > 0)
            issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.DuplicatePaymentIdentity,
                $"{duplicatePayment} duplicate payment business identity group(s) were found.",
                "payment_batch_execution", companyId));

        var duplicateWebhooks = await db.PaymentProviderWebhookReceipts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.ProviderKey, x.WebhookId })
            .Where(group => group.Count() > 1).CountAsync(cancellationToken);
        if (duplicateWebhooks > 0)
            issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.DuplicateWebhookIdentity,
                $"{duplicateWebhooks} duplicate provider webhook identity group(s) were found.",
                "payment_provider_webhook_receipt", companyId));
    }

    private async Task VerifyStatementObjectAsync(StatementObjectRow item,
        ICollection<ConnectedBankingRecoveryIssueDto> issues, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await documentStorage.OpenReadAsync(item.StorageKey, cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long length = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                length += read;
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, item.Checksum, StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.StatementObjectHashMismatch,
                    "The restored statement object does not match its retained SHA-256 checksum.",
                    "bank_statement_import_job", item.Id));
            if (length != item.ContentLength)
                issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.StatementObjectLengthMismatch,
                    "The restored statement object length does not match its retained length.",
                    "bank_statement_import_job", item.Id));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            issues.Add(Issue(ConnectedBankingRecoveryReasonCodes.StatementObjectMissing,
                "The restored statement object cannot be opened from object storage.",
                "bank_statement_import_job", item.Id));
        }
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (companyContext is { IsResolved: true, CompanyId: Guid scopedCompanyId } && scopedCompanyId != companyId)
            throw new UnauthorizedAccessException("The requested company is outside the resolved tenant context.");
    }

    private static void Append(IncrementalHash hash, params object?[] fields)
    {
        var line = string.Join("|", fields.Select(Format)) + "\n";
        hash.AppendData(Encoding.UTF8.GetBytes(line));
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        Guid id => id.ToString("D"),
        DateTime timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static ConnectedBankingRecoveryIssueDto Issue(string reasonCode, string explanation,
        string entityType, Guid entityId) =>
        new(reasonCode, explanation, entityType, entityId.ToString("D"), true);

    private sealed record StatementObjectRow(Guid Id, string StorageKey, string Checksum, long ContentLength);

    private sealed class RecoveryCounts
    {
        public int Connections { get; set; }
        public int FeedSourceObjects { get; set; }
        public int FeedTransactions { get; set; }
        public int StatementImports { get; set; }
        public int PaymentExecutions { get; set; }
        public int Acknowledgements { get; set; }
        public int WebhookReceipts { get; set; }
        public int Settlements { get; set; }
        public int ReconciliationResults { get; set; }
    }
}
