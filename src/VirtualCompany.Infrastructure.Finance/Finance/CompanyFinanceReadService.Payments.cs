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
    public async Task<IReadOnlyList<FinancePaymentDto>> GetPaymentsAsync(
        GetFinancePaymentsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, query.SourceFilter);
        var normalizedType = string.IsNullOrWhiteSpace(query.PaymentType)
            ? null
            : PaymentTypes.Normalize(query.PaymentType);
        if (normalizedType is not null && !PaymentTypes.IsSupported(normalizedType))
        {
            throw new ArgumentException($"Unsupported payment type '{query.PaymentType}'.", nameof(query));
        }

        var limit = NormalizeLimit(query.Limit);
        var payments = _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (normalizedType is not null)
        {
            payments = payments.Where(x => x.PaymentType == normalizedType);
        }

        payments = ApplySourceFilter(payments, query.CompanyId, query.SourceFilter, "payment");

        var rows = await payments
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(limit)
            .Select(x => new FinancePaymentSourceRow(
                x.Id,
                x.CompanyId,
                x.PaymentType,
                x.Amount,
                x.Currency,
                x.PaymentDate,
                x.Method,
                x.Status,
                x.CounterpartyReference,
                x.CreatedUtc,
                x.UpdatedUtc,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .ToListAsync(cancellationToken);

        var fortnoxReferenceIds = await LoadFortnoxReferenceIdsAsync(
            query.CompanyId,
            ["payment"],
            rows.Select(x => x.Id),
            cancellationToken);

        return rows
            .Select(row => new FinancePaymentDto(
                row.Id,
                row.CompanyId,
                row.PaymentType,
                row.Amount,
                row.Currency,
                row.PaymentDate,
                row.Method,
                row.Status,
                row.CounterpartyReference,
                row.CreatedUtc,
                row.UpdatedUtc,
                Array.Empty<NormalizedFinanceInsightDto>(),
                ResolveFinanceSource(row.SourceType, row.ProviderKey, fortnoxReferenceIds.Contains(row.Id))))
            .ToList();
    }

    public async Task<FinancePaymentDto?> GetPaymentDetailAsync(
        GetFinancePaymentDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(query));
        }

        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        var row = await ApplySourceFilter(_dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.PaymentId),
            query.CompanyId,
            sourceFilter,
            "payment")
            .Select(x => new FinancePaymentSourceRow(
                x.Id,
                x.CompanyId,
                x.PaymentType,
                x.Amount,
                x.Currency,
                x.PaymentDate,
                x.Method,
                x.Status,
                x.CounterpartyReference,
                x.CreatedUtc,
                x.UpdatedUtc,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var hasFortnoxReference = await HasFortnoxReferenceAsync(query.CompanyId, ["payment"], row.Id, cancellationToken);
        var agentInsights = await LoadEntityAgentInsightsAsync(query.CompanyId, "payment", row.Id, cancellationToken);
        return new FinancePaymentDto(
            row.Id,
            row.CompanyId,
            row.PaymentType,
            row.Amount,
            row.Currency,
            row.PaymentDate,
            row.Method,
            row.Status,
            row.CounterpartyReference,
            row.CreatedUtc,
            row.UpdatedUtc,
            agentInsights,
            ResolveFinanceSource(row.SourceType, row.ProviderKey, hasFortnoxReference));
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByPaymentAsync(
        GetFinancePaymentAllocationsByPaymentQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        if (query.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(query));
        }

        var exists = await ApplySourceFilter(_dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.PaymentId),
            query.CompanyId,
            sourceFilter,
            "payment")
            .AnyAsync(cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance payment was not found.");
        }

        return await _sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PaymentId == query.PaymentId), query.CompanyId, sourceFilter)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByInvoiceAsync(
        GetFinanceInvoiceAllocationsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        if (query.InvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(query));
        }

        var exists = await ApplySourceFilter(_dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId),
            query.CompanyId,
            sourceFilter,
            "invoice")
            .AnyAsync(cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance invoice was not found.");
        }

        return await _sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId == query.InvoiceId), query.CompanyId, sourceFilter)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByBillAsync(
        GetFinanceBillAllocationsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var sourceFilter = FinanceDataSources.NormalizeOperationalRead(query.SourceFilter);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken, sourceFilter);
        if (query.BillId == Guid.Empty)
        {
            throw new ArgumentException("Bill id is required.", nameof(query));
        }

        var exists = await ApplySourceFilter(_dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.BillId),
            query.CompanyId,
            sourceFilter,
            "supplier_invoice", "bill")
            .AnyAsync(cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance bill was not found.");
        }

        return await _sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BillId == query.BillId), query.CompanyId, sourceFilter)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<FinancePaymentAllocationTraceDto?> GetAllocationTraceAsync(
        GetFinancePaymentAllocationTraceQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AllocationId == Guid.Empty)
        {
            throw new ArgumentException("Allocation id is required.", nameof(query));
        }

        var allocation = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSingleQuery()
            .Include(x => x.SourceSimulationEventRecord)
            .Include(x => x.PaymentSourceSimulationEventRecord)
            .Include(x => x.TargetSourceSimulationEventRecord)
            .Include(x => x.Payment)
                .ThenInclude(x => x.SourceSimulationEventRecord)
            .Include(x => x.Invoice)
                .ThenInclude(x => x!.SourceSimulationEventRecord)
            .Include(x => x.Bill)
                .ThenInclude(x => x!.SourceSimulationEventRecord)
            .SingleOrDefaultAsync(
                x => x.CompanyId == query.CompanyId && x.Id == query.AllocationId,
                cancellationToken);

        if (allocation is null)
        {
            return null;
        }

        var targetDocument = allocation.Invoice is not null
            ? new FinanceAllocationTargetDocumentDto(
                "invoice",
                allocation.Invoice.Id,
                allocation.Invoice.InvoiceNumber,
                allocation.Invoice.Amount,
                allocation.Invoice.Currency,
                allocation.Invoice.Status,
                allocation.Invoice.SourceSimulationEventRecordId)
            : new FinanceAllocationTargetDocumentDto(
                "bill",
                allocation.Bill!.Id,
                allocation.Bill.BillNumber,
                allocation.Bill.Amount,
                allocation.Bill.Currency,
                allocation.Bill.Status,
                allocation.Bill.SourceSimulationEventRecordId);

        return new FinancePaymentAllocationTraceDto(
            allocation.Id,
            allocation.CompanyId,
            MapPayment(allocation.Payment),
            targetDocument,
            MapSimulationEventReference(allocation.PaymentSourceSimulationEventRecord ?? allocation.Payment.SourceSimulationEventRecord),
            MapSimulationEventReference(allocation.TargetSourceSimulationEventRecord ?? allocation.Invoice?.SourceSimulationEventRecord ?? allocation.Bill?.SourceSimulationEventRecord),
            MapSimulationEventReference(allocation.SourceSimulationEventRecord ?? allocation.PaymentSourceSimulationEventRecord ?? allocation.Payment.SourceSimulationEventRecord));
    }

}

