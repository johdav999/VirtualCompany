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
    public async Task<IReadOnlyList<FinanceSeedAnomalyDto>> GetSeedAnomaliesAsync(
        GetFinanceSeedAnomaliesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = NormalizeLimit(query.Limit);
        var anomalyType = string.IsNullOrWhiteSpace(query.AnomalyType)
            ? null
            : query.AnomalyType.Trim();

        if (query.AffectedRecordId == Guid.Empty)
        {
            throw new ArgumentException("Affected record id cannot be empty.", nameof(query));
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var filtered = anomalies
            .Select(MapSeedAnomaly)
            .Where(x =>
                anomalyType is null ||
                string.Equals(x.AnomalyType, anomalyType, StringComparison.OrdinalIgnoreCase));

        if (query.AffectedRecordId is Guid affectedRecordId)
        {
            filtered = filtered.Where(x => x.AffectedRecordIds.Contains(affectedRecordId));
        }

        return filtered
            .Take(limit)
            .ToList();
    }

    public async Task<FinanceSeedAnomalyDto?> GetSeedAnomalyByIdAsync(
        GetFinanceSeedAnomalyByIdQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AnomalyId == Guid.Empty)
        {
            throw new ArgumentException("Anomaly id is required.", nameof(query));
        }

        var anomaly = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.AnomalyId, cancellationToken);

        return anomaly is null
            ? null
            : MapSeedAnomaly(anomaly);
    }

    public async Task<FinanceAnomalyWorkbenchResultDto> GetAnomalyWorkbenchAsync(
        GetFinanceAnomalyWorkbenchQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var normalizedType = NormalizeFilterToken(query.AnomalyType);
        var normalizedStatus = NormalizeFilterToken(query.Status);
        var normalizedSupplier = NormalizeOptionalText(query.Supplier);
        var confidenceMin = NormalizeConfidence(query.ConfidenceMin);
        var confidenceMax = NormalizeConfidence(query.ConfidenceMax);
        var dateFromUtc = NormalizeUtc(query.DateFromUtc);
        var dateToUtc = NormalizeUtc(query.DateToUtc);
        var (page, pageSize) = NormalizePagination(query.Page, query.PageSize);

        if (confidenceMin.HasValue && confidenceMax.HasValue && confidenceMin > confidenceMax)
        {
            (confidenceMin, confidenceMax) = (confidenceMax, confidenceMin);
        }

        var alerts = await LoadFinanceAnomalyAlertsAsync(query.CompanyId, cancellationToken);
        var transactions = await LoadFinanceAnomalyTransactionsAsync(query.CompanyId, alerts, cancellationToken);
        var invoices = await LoadFinanceAnomalyInvoicesAsync(query.CompanyId, transactions.Values, cancellationToken);
        var bills = await LoadFinanceAnomalyBillsAsync(query.CompanyId, transactions.Values, cancellationToken);
        var tasksByCorrelationId = await LoadFinanceAnomalyTasksByCorrelationIdAsync(query.CompanyId, alerts, cancellationToken);

        var filtered = alerts
            .Select(alert => MapFinanceAnomalyWorkbenchItem(alert, transactions, invoices, bills, tasksByCorrelationId))
            .Where(item => item is not null)
            .Cast<FinanceAnomalyWorkbenchItemDto>()
            .Where(item => normalizedType is null || string.Equals(NormalizeFilterToken(item.AnomalyType), normalizedType, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedStatus is null || string.Equals(NormalizeFilterToken(item.Status), normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(item => confidenceMin is null || item.Confidence >= confidenceMin.Value)
            .Where(item => confidenceMax is null || item.Confidence <= confidenceMax.Value)
            .Where(item =>
                normalizedSupplier is null ||
                (!string.IsNullOrWhiteSpace(item.SupplierName) &&
                 item.SupplierName.Contains(normalizedSupplier, StringComparison.OrdinalIgnoreCase)))
            .Where(item => dateFromUtc is null || item.DetectedAtUtc >= dateFromUtc.Value)
            .Where(item => dateToUtc is null || item.DetectedAtUtc < dateToUtc.Value)
            .OrderByDescending(item => item.DetectedAtUtc)
            .ThenBy(item => item.AffectedRecordReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToList();

        var totalCount = filtered.Count;
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new FinanceAnomalyWorkbenchResultDto(totalCount, page, pageSize, items);
    }

    public async Task<FinanceAnomalyDetailDto?> GetAnomalyDetailAsync(
        GetFinanceAnomalyDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AnomalyId == Guid.Empty)
        {
            throw new ArgumentException("Anomaly id is required.", nameof(query));
        }

        var alert = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == query.CompanyId &&
                     x.Id == query.AnomalyId &&
                     x.Type == AlertType.Anomaly &&
                     x.CorrelationId.StartsWith("fin-anom:"),
                cancellationToken);

        if (alert is null)
        {
            return null;
        }

        var transactions = await LoadFinanceAnomalyTransactionsAsync(query.CompanyId, [alert], cancellationToken);
        var invoices = await LoadFinanceAnomalyInvoicesAsync(query.CompanyId, transactions.Values, cancellationToken);
        var bills = await LoadFinanceAnomalyBillsAsync(query.CompanyId, transactions.Values, cancellationToken);
        var tasksByCorrelationId = await LoadFinanceAnomalyTasksByCorrelationIdAsync(query.CompanyId, [alert], cancellationToken);

        var transactionId = ExtractGuid(alert.Evidence, "transactionId");
        var transaction = transactionId.HasValue ? transactions.GetValueOrDefault(transactionId.Value) : null;
        var invoice = transaction?.InvoiceId is Guid invoiceId ? invoices.GetValueOrDefault(invoiceId) : null;
        var bill = transaction?.BillId is Guid billId ? bills.GetValueOrDefault(billId) : null;
        var tasks = tasksByCorrelationId.GetValueOrDefault(alert.CorrelationId) ?? [];
        var latestTask = tasks
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .FirstOrDefault();

        var anomalyType = ExtractString(alert.Metadata, "anomalyType")
            ?? ExtractString(alert.Evidence, "anomalyType")
            ?? "unknown";
        var confidence = ExtractDecimal(alert.Metadata, "confidence")
            ?? ExtractDecimal(alert.Evidence, "confidence")
            ?? 0m;
        var supplierName = NormalizeOptionalText(
            invoice?.CounterpartyName
            ?? bill?.CounterpartyName
            ?? transaction?.CounterpartyName
            ?? ExtractString(alert.Evidence, "counterpartyName"));
        var affectedRecord = transaction is null
            ? null
            // The detail card shows the primary transaction summary while related record links expose drill-down targets.
            : new FinanceAnomalyRelatedRecordDto(
                transaction.Id,
                transaction.ExternalReference,
                transaction.TransactionUtc,
                transaction.Amount,
                transaction.Currency,
                transaction.CounterpartyName);

        return new FinanceAnomalyDetailDto(
            alert.Id,
            anomalyType,
            latestTask?.Status.ToStorageValue() ?? alert.Status.ToStorageValue(),
            confidence,
            supplierName,
            alert.Summary,
            ExtractString(alert.Metadata, "recommendedAction")
                ?? ExtractString(alert.Evidence, "recommendedAction")
                ?? string.Empty,
            alert.LastDetectedUtc ?? alert.CreatedUtc,
            BuildDeduplicationMetadata(alert),
            affectedRecord,
            invoice?.Id,
            invoice?.InvoiceNumber,
            bill?.Id,
            bill?.BillNumber,
            BuildFinanceAnomalyRecordLinks(transaction, invoice, bill),
            tasks
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .Select(x => new FinanceAnomalyFollowUpTaskDto(
                    x.Id,
                    x.Title,
                    x.Status.ToStorageValue(),
                    x.CreatedUtc,
                    x.DueUtc,
                    x.UpdatedUtc))
                .ToList());
    }

}

