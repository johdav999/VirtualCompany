using System.Globalization;
using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;


public sealed partial class CompanyFinanceReadService
{
    public async Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
        GetFinanceTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, query.SourceFilter);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var category = NormalizeOptionalText(query.Category);
        var flaggedState = NormalizeFlaggedState(query.FlaggedState);
        var limit = NormalizeLimit(query.Limit);

        var transactions = _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            transactions = transactions.Where(x => x.TransactionUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            transactions = transactions.Where(x => x.TransactionUtc < endUtc.Value);
        }

        transactions = ApplySourceFilter(transactions, query.CompanyId, query.SourceFilter, "voucher", "payment", "transaction");

        var rows = await transactions
            .OrderByDescending(x => x.TransactionUtc)
            .ThenBy(x => x.ExternalReference)
            .Take(MaxLimit)
            .Select(x => new FinanceTransactionRow(
                x.Id,
                x.AccountId,
                x.Account.Name,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.DocumentId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .ToListAsync(cancellationToken);

        var fortnoxReferenceIds = await LoadFortnoxReferenceIdsAsync(
            query.CompanyId,
            ["voucher", "payment", "transaction"],
            rows.Select(x => x.Id),
            cancellationToken);

        var anomalyLookup = await LoadTransactionAnomalyLookupAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);
        var voucherFallbacks = await LoadFortnoxVoucherAmountFallbacksAsync(
            query.CompanyId,
            rows,
            fortnoxReferenceIds,
            cancellationToken);
        var invoiceReviewStates = await LoadInvoiceReviewStatesAsync(
            query.CompanyId,
            rows.Select(x => x.InvoiceId).Concat(voucherFallbacks.Values.Select(x => x.InvoiceId)),
            cancellationToken);
        var billReviewStates = await LoadBillReviewStatesAsync(
            query.CompanyId,
            rows.Select(x => x.BillId).Concat(voucherFallbacks.Values.Select(x => x.BillId)),
            cancellationToken);
        var paymentSyncBlocked = await IsFortnoxPaymentSyncBlockedAsync(query.CompanyId, cancellationToken);

        return rows
            .Where(x => category is null || string.Equals(x.TransactionType, category, StringComparison.OrdinalIgnoreCase))
            .Where(x =>
            {
                voucherFallbacks.TryGetValue(x.Id, out var fallback);
                var requiresDocumentReview = RequiresLinkedDocumentReview(x, fallback, invoiceReviewStates, billReviewStates);
                var requiresPaymentSyncReview = RequiresFortnoxPaymentSyncReview(paymentSyncBlocked, fortnoxReferenceIds.Contains(x.Id), x, fallback);
                return MatchesFlaggedState(flaggedState, anomalyLookup.ContainsKey(x.Id) || requiresDocumentReview || requiresPaymentSyncReview);
            })
            .Take(limit)
            .Select(x =>
            {
                voucherFallbacks.TryGetValue(x.Id, out var fallback);
                var requiresDocumentReview = RequiresLinkedDocumentReview(x, fallback, invoiceReviewStates, billReviewStates);
                var requiresPaymentSyncReview = RequiresFortnoxPaymentSyncReview(paymentSyncBlocked, fortnoxReferenceIds.Contains(x.Id), x, fallback);
                var requiresReview = requiresDocumentReview || requiresPaymentSyncReview;

                return new FinanceTransactionDto(
                    x.Id,
                    x.AccountId,
                    x.AccountName,
                    x.CounterpartyId ?? fallback?.CounterpartyId,
                    x.CounterpartyName ?? fallback?.CounterpartyName,
                    x.InvoiceId ?? fallback?.InvoiceId,
                    x.BillId ?? fallback?.BillId,
                    x.TransactionUtc,
                    x.TransactionType,
                    ResolveTransactionAmount(x, fallback),
                    string.IsNullOrWhiteSpace(x.Currency) ? fallback?.Currency ?? x.Currency : x.Currency,
                    x.Description,
                    x.ExternalReference,
                    MapLinkedDocument(x.DocumentId, linkedDocuments),
                    anomalyLookup.ContainsKey(x.Id) || requiresReview,
                    ResolveTransactionAnomalyState(anomalyLookup.GetValueOrDefault(x.Id), requiresReview),
                    ResolveFinanceSource(x.SourceType, x.ProviderKey, fortnoxReferenceIds.Contains(x.Id)));
            })
            .ToList();
    }

    public async Task<FinanceTransactionDetailDto?> GetTransactionDetailAsync(
        GetFinanceTransactionDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction id is required.", nameof(query));
        }

        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        var row = await ApplySourceFilter(_dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.TransactionId),
            query.CompanyId,
            sourceFilter,
            "voucher", "payment", "transaction")
            .Select(x => new FinanceTransactionRow(
                x.Id,
                x.AccountId,
                x.Account.Name,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.DocumentId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var hasFortnoxReference = await HasFortnoxReferenceAsync(
            query.CompanyId,
            ["voucher", "payment", "transaction"],
            row.Id,
            cancellationToken);
        await EnsureFinanceInitializedForRecordAsync(
            query.CompanyId,
            row.SourceType,
            row.ProviderKey,
            hasFortnoxReference,
            cancellationToken);

        var anomalyLookup = await LoadTransactionAnomalyLookupAsync(query.CompanyId, [row.Id], cancellationToken);
        var anomalies = anomalyLookup.GetValueOrDefault(row.Id) ?? [];
        var linkedDocuments = await LoadLinkedDocumentsAsync(query.CompanyId, [row.DocumentId], cancellationToken);
        var documentAccess = await BuildDocumentAccessAsync(query.CompanyId, row.DocumentId, linkedDocuments, cancellationToken);
        var voucherFallbacks = await LoadFortnoxVoucherAmountFallbacksAsync(
            query.CompanyId,
            [row],
            hasFortnoxReference ? new HashSet<Guid> { row.Id } : [],
            cancellationToken);
        voucherFallbacks.TryGetValue(row.Id, out var fallback);
        var invoiceId = row.InvoiceId ?? fallback?.InvoiceId;
        var billId = row.BillId ?? fallback?.BillId;
        var invoiceReviewStates = await LoadInvoiceReviewStatesAsync(query.CompanyId, [invoiceId], cancellationToken);
        var billReviewStates = await LoadBillReviewStatesAsync(query.CompanyId, [billId], cancellationToken);
        var requiresDocumentReview = RequiresLinkedDocumentReview(row, fallback, invoiceReviewStates, billReviewStates);
        var paymentContext = BuildPaymentContext(invoiceId, billId, invoiceReviewStates, billReviewStates);
        var paymentSyncBlocked = await IsFortnoxPaymentSyncBlockedAsync(query.CompanyId, cancellationToken);
        var requiresPaymentSyncReview = RequiresFortnoxPaymentSyncReview(paymentSyncBlocked, hasFortnoxReference, row, fallback);
        var requiresReview = requiresDocumentReview || requiresPaymentSyncReview;
        var flags = anomalies
            .Select(x => NormalizeCategory(x.AnomalyType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requiresDocumentReview && !flags.Contains("partially_paid", StringComparer.OrdinalIgnoreCase))
        {
            flags.Add("partially_paid");
        }
        if (requiresPaymentSyncReview && !flags.Contains("payment_sync_blocked", StringComparer.OrdinalIgnoreCase))
        {
            flags.Add("payment_sync_blocked");
        }

        return new FinanceTransactionDetailDto(
            row.Id,
            row.AccountId,
            row.AccountName,
            row.CounterpartyId ?? fallback?.CounterpartyId,
            row.CounterpartyName ?? fallback?.CounterpartyName,
            invoiceId,
            billId,
            row.TransactionUtc,
            row.TransactionType,
            ResolveTransactionAmount(row, fallback),
            string.IsNullOrWhiteSpace(row.Currency) ? fallback?.Currency ?? row.Currency : row.Currency,
            row.Description,
            row.ExternalReference,
            anomalies.Any() || requiresReview,
            ResolveTransactionAnomalyState(anomalies, requiresReview),
            flags,
            BuildActionPermissions(),
            documentAccess,
            paymentContext,
            ResolveFinanceSource(row.SourceType, row.ProviderKey, hasFortnoxReference));
    }

}

