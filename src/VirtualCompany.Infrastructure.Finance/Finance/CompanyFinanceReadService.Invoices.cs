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
    public async Task<FinanceInvoiceDetailDto?> GetInvoiceDetailAsync(
        GetFinanceInvoiceDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.InvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(query));
        }

        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        var row = await ApplySourceFilter(_dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId),
            query.CompanyId,
            sourceFilter,
            "invoice")
            .Select(x => new FinanceInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.InvoiceNumber,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.PostingStatus,
                x.SettlementStatus,
                x.DueStatus,
                x.DocumentKind,
                x.ProviderStatus,
                x.ProcessingStatus,
                x.DocumentId,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var hasFortnoxReference = await HasFortnoxReferenceAsync(query.CompanyId, ["invoice"], row.Id, cancellationToken);
        await EnsureFinanceInitializedForRecordAsync(
            query.CompanyId,
            row.SourceType,
            row.ProviderKey,
            hasFortnoxReference,
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(query.CompanyId, [row.DocumentId], cancellationToken);
        var documentAccess = await BuildDocumentAccessAsync(query.CompanyId, row.DocumentId, linkedDocuments, cancellationToken);
        var invoiceReviewStates = await LoadInvoiceReviewStatesAsync(query.CompanyId, [row.Id], cancellationToken);
        var paymentContext = BuildPaymentContext(
            row.Id,
            null,
            invoiceReviewStates,
            new Dictionary<Guid, TransactionDocumentReviewState>());
        var relatedTransactions = await LoadInvoiceRelatedTransactionsAsync(query.CompanyId, row.Id, cancellationToken);
        return new FinanceInvoiceDetailDto(
            row.Id,
            row.CounterpartyId,
            row.CounterpartyName,
            row.InvoiceNumber,
            row.IssuedUtc,
            row.DueUtc,
            row.Amount,
            row.Currency,
            row.Status,
            null,
            BuildActionPermissions(),
            documentAccess,
            await LoadEntityAgentInsightsAsync(query.CompanyId, "invoice", row.Id, cancellationToken),
            row.PostingStatus,
            row.SettlementStatus,
            row.DueStatus,
            row.DocumentKind,
            row.ProviderStatus,
            row.ProcessingStatus,
            paymentContext,
            relatedTransactions,
            null,
            ResolveFinanceSource(row.SourceType, row.ProviderKey, hasFortnoxReference));
    }

    private async Task<Dictionary<Guid, List<FinanceSeedAnomalyDto>>> LoadTransactionAnomalyLookupAsync(
        Guid companyId,
        IEnumerable<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        var ids = transactionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<Guid, List<FinanceSeedAnomalyDto>>();
        foreach (var anomaly in anomalies.Select(MapSeedAnomaly))
        {
            foreach (var affectedRecordId in anomaly.AffectedRecordIds.Where(ids.Contains))
            {
                if (!lookup.TryGetValue(affectedRecordId, out var items))
                {
                    items = [];
                    lookup[affectedRecordId] = items;
                }

                items.Add(anomaly);
            }
        }

        return lookup;
    }

    public async Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
        GetFinanceInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, query.SourceFilter);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var limit = NormalizeLimit(query.Limit);

        var invoices = _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            invoices = invoices.Where(x => x.IssuedUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            invoices = invoices.Where(x => x.IssuedUtc < endUtc.Value);
        }

        invoices = ApplySourceFilter(invoices, query.CompanyId, query.SourceFilter, "invoice");

        var rows = await invoices
            .OrderByDescending(x => x.IssuedUtc)
            .ThenBy(x => x.InvoiceNumber)
            .Take(limit)
            .Select(x => new FinanceInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.InvoiceNumber,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.PostingStatus,
                x.SettlementStatus,
                x.DueStatus,
                x.DocumentKind,
                x.ProviderStatus,
                x.ProcessingStatus,
                x.DocumentId,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .ToListAsync(cancellationToken);

        var fortnoxReferenceIds = await LoadFortnoxReferenceIdsAsync(
            query.CompanyId,
            ["invoice"],
            rows.Select(x => x.Id),
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);
        var invoiceReviewStates = await LoadInvoiceReviewStatesAsync(
            query.CompanyId,
            rows.Select(x => (Guid?)x.Id),
            cancellationToken);
        var emptyBillReviewStates = new Dictionary<Guid, TransactionDocumentReviewState>();
        var accountingStates = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && rows.Select(row => row.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.Status, x.LedgerEntryId, ApprovalStatus = x.ApprovalRequest == null ? null : (ApprovalRequestStatus?)x.ApprovalRequest.Status })
            .ToDictionaryAsync(x => x.InvoiceId, cancellationToken);

        return rows
            .Select(x =>
            {
                var paymentContext = BuildPaymentContext(x.Id, null, invoiceReviewStates, emptyBillReviewStates);
                accountingStates.TryGetValue(x.Id, out var accounting);
                var accountingStatus = accounting?.Status == CustomerInvoiceAccountingStatuses.AwaitingApproval && accounting.ApprovalStatus == ApprovalRequestStatus.Approved
                    ? CustomerInvoiceAccountingStatuses.ReadyToPost
                    : accounting?.Status ?? CustomerInvoiceAccountingStatuses.NotReady;
                return new FinanceInvoiceDto(
                    x.Id,
                    x.CounterpartyId,
                    x.CounterpartyName,
                    x.InvoiceNumber,
                    x.IssuedUtc,
                    x.DueUtc,
                    x.Amount,
                    x.Currency,
                    x.Status,
                    MapLinkedDocument(x.DocumentId, linkedDocuments),
                    ResolveFinanceSource(x.SourceType, x.ProviderKey, fortnoxReferenceIds.Contains(x.Id)),
                    x.PostingStatus,
                    x.SettlementStatus,
                    x.DueStatus,
                    x.DocumentKind,
                    x.ProviderStatus,
                    x.ProcessingStatus,
                    paymentContext,
                    accountingStatus,
                    AccountingStatusLabel(accountingStatus),
                    accounting?.LedgerEntryId);
            })
            .ToList();
    }

    private static string AccountingStatusLabel(string status) => status switch
    {
        CustomerInvoiceAccountingStatuses.AwaitingApproval => "Waiting for approval",
        CustomerInvoiceAccountingStatuses.ReadyToPost => "Ready to post",
        CustomerInvoiceAccountingStatuses.Posted => "Posted",
        CustomerInvoiceAccountingStatuses.Reversed => "Reversed",
        CustomerInvoiceAccountingStatuses.Blocked => "Needs review",
        _ => "Not ready"
    };

}

