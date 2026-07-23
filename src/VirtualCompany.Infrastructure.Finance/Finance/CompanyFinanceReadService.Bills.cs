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
    public async Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, query.SourceFilter);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var limit = NormalizeLimit(query.Limit);

        var bills = _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            bills = bills.Where(x => x.ReceivedUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            bills = bills.Where(x => x.ReceivedUtc < endUtc.Value);
        }

        bills = ApplySourceFilter(bills, query.CompanyId, query.SourceFilter, "supplier_invoice", "bill");

        var rows = await bills
            .OrderByDescending(x => x.ReceivedUtc)
            .ThenBy(x => x.BillNumber)
            .Take(limit)
            .Select(x => new FinanceBillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.Counterparty == null ? null : x.Counterparty.DefaultAccountMapping,
                x.BillNumber,
                x.ReceivedUtc,
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
            ["supplier_invoice", "bill"],
            rows.Select(x => x.Id),
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);
        var billReviewStates = await LoadBillReviewStatesAsync(
            query.CompanyId,
            rows.Select(x => (Guid?)x.Id),
            cancellationToken);
        var paymentProposals = await LoadSupplierPaymentProposalsAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);
        var sourceDocumentAttachments = await LoadSupplierInvoiceSourceDocumentAttachmentsAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);
        var draftActions = await LoadSupplierInvoiceDraftActionsAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);
        var correctionActions = await LoadSupplierInvoiceCorrectionActionsAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);
        var enrichmentActions = await LoadSupplierInvoiceEnrichmentActionsAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);
        var emptyInvoiceReviewStates = new Dictionary<Guid, TransactionDocumentReviewState>();

        return rows
            .Select(x =>
            {
                var paymentContext = BuildPaymentContext(null, x.Id, emptyInvoiceReviewStates, billReviewStates);
                return new FinanceBillDto(
                    x.Id,
                    x.CounterpartyId,
                    x.CounterpartyName,
                    x.BillNumber,
                    x.ReceivedUtc,
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
                    paymentProposals.GetValueOrDefault(x.Id),
                    sourceDocumentAttachments.GetValueOrDefault(x.Id),
                    draftActions.GetValueOrDefault(x.Id),
                    correctionActions.GetValueOrDefault(x.Id),
                    enrichmentActions.GetValueOrDefault(x.Id));
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, SupplierInvoicePaymentProposalDto>> LoadSupplierPaymentProposalsAsync(
        Guid companyId,
        IEnumerable<Guid> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var proposals = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.BillId))
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        return proposals
            .GroupBy(x => x.BillId)
            .ToDictionary(group => group.Key, group => SupplierInvoicePaymentProposalService.MapProposal(group.First()));
    }

    private async Task<Dictionary<Guid, SupplierInvoiceSourceDocumentAttachmentDto>> LoadSupplierInvoiceSourceDocumentAttachmentsAsync(
        Guid companyId,
        IEnumerable<Guid> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var attachments = await ReadOptionalSupplierInvoiceTableAsync(
            "supplier_invoice_source_document_attachments",
            () => _dbContext.SupplierInvoiceSourceDocumentAttachments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.BillId))
                .ToListAsync(cancellationToken),
            cancellationToken);

        return attachments.ToDictionary(x => x.BillId, SupplierInvoiceSourceDocumentAttachmentService.MapAttachment);
    }

    private async Task<Dictionary<Guid, SupplierInvoiceDraftActionDto>> LoadSupplierInvoiceDraftActionsAsync(
        Guid companyId,
        IEnumerable<Guid> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var actions = await ReadOptionalSupplierInvoiceTableAsync(
            "supplier_invoice_draft_actions",
            () => _dbContext.SupplierInvoiceDraftActions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.BillId))
                .ToListAsync(cancellationToken),
            cancellationToken);

        return actions.ToDictionary(x => x.BillId, SupplierInvoiceDraftActionService.MapAction);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<SupplierInvoiceCorrectionActionDto>>> LoadSupplierInvoiceCorrectionActionsAsync(
        Guid companyId,
        IEnumerable<Guid> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var actions = await ReadOptionalSupplierInvoiceTableAsync(
            "supplier_invoice_correction_actions",
            () => _dbContext.SupplierInvoiceCorrectionActions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.BillId))
                .OrderByDescending(x => x.UpdatedUtc)
                .ToListAsync(cancellationToken),
            cancellationToken);

        return actions
            .GroupBy(x => x.BillId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SupplierInvoiceCorrectionActionDto>)group
                    .Select(SupplierInvoiceCorrectionService.MapAction)
                    .ToArray());
    }

    private async Task<Dictionary<Guid, SupplierInvoiceEnrichmentActionDto>> LoadSupplierInvoiceEnrichmentActionsAsync(
        Guid companyId,
        IEnumerable<Guid> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var actions = await ReadOptionalSupplierInvoiceTableAsync(
            "supplier_invoice_enrichment_actions",
            () => _dbContext.SupplierInvoiceEnrichmentActions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.BillId))
                .ToListAsync(cancellationToken),
            cancellationToken);

        return actions.ToDictionary(x => x.BillId, SupplierInvoiceEnrichmentService.MapAction);
    }

    private async Task<List<T>> ReadOptionalSupplierInvoiceTableAsync<T>(
        string tableName,
        Func<Task<List<T>>> read,
        CancellationToken cancellationToken)
    {
        if (!await OptionalSupplierInvoiceTableExistsAsync(tableName, cancellationToken))
        {
            return [];
        }

        try
        {
            return await read();
        }
        catch (SqlException ex) when (IsMissingSqlObject(ex))
        {
            _optionalSupplierInvoiceTableAvailability[tableName] = false;
            return [];
        }
    }

    private async Task<bool> OptionalSupplierInvoiceTableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        if (_optionalSupplierInvoiceTableAvailability.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.tables AS t
                    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                    WHERE s.name = N'dbo' AND t.name = @tableName
                ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var exists = result is bool value
                ? value
                : result is not null && Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
            _optionalSupplierInvoiceTableAvailability[tableName] = exists;
            return exists;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsMissingSqlObject(SqlException exception)
    {
        foreach (SqlError error in exception.Errors)
        {
            if (error.Number == 208)
            {
                return true;
            }
        }

        return false;
    }

}

