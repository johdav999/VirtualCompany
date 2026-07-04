using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class CashRiskFinancialCheck : IFinancialCheck
{
    private readonly Func<FinancialCheckContext, CancellationToken, Task<FinanceCashPositionDto>> _loadCashPosition;

    public CashRiskFinancialCheck(Func<FinancialCheckContext, CancellationToken, Task<FinanceCashPositionDto>> loadCashPosition)
    {
        _loadCashPosition = loadCashPosition;
    }

    public FinancialCheckDefinition Definition => FinancialCheckDefinitions.CashRisk;
    public string CheckCode => Definition.Code;

    public async Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)
    {
        var position = await _loadCashPosition(context, cancellationToken);
        if (!position.AlertState.IsLowCash)
        {
            return [];
        }

        var primaryEntity = new FinanceInsightEntityReferenceDto("company", context.CompanyId.ToString("N"), "Company cash position", true);
        return
        [
            new FinancialCheckResult(
                Definition,
                $"{CheckCode}:{context.CompanyId:N}",
                primaryEntity.EntityType,
                primaryEntity.EntityId,
                ParseSeverity(position.AlertState.RiskLevel),
                position.Rationale,
                position.WorkflowOutput.RecommendedAction,
                position.WorkflowOutput.Confidence,
                primaryEntity,
                [primaryEntity],
                ObservedAtUtc: context.AsOfUtc)
        ];
    }

    private static FinancialCheckSeverity ParseSeverity(string riskLevel) =>
        riskLevel.Trim().ToLowerInvariant() switch
        {
            "critical" => FinancialCheckSeverity.Critical,
            "high" => FinancialCheckSeverity.High,
            "medium" => FinancialCheckSeverity.Medium,
            _ => FinancialCheckSeverity.Low
        };
}

internal sealed class TransactionAnomalyFinancialCheck : IFinancialCheck
{
    private readonly VirtualCompanyDbContext _dbContext;

    public TransactionAnomalyFinancialCheck(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FinancialCheckDefinition Definition => FinancialCheckDefinitions.TransactionAnomaly;
    public string CheckCode => Definition.Code;

    public async Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)
    {
        var alerts = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == context.CompanyId &&
                x.Type == AlertType.Anomaly &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged))
            .OrderByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);

        return alerts
            .Select(Map)
            .ToArray();
    }

    private FinancialCheckResult Map(Alert alert)
    {
        string? transactionId = ReadFirstString(alert.Evidence, "sourceEntityId", "transactionId", "financeTransactionId");
        if (string.IsNullOrWhiteSpace(transactionId) &&
            alert.Metadata.TryGetValue("dedupeKey", out var dedupeNode) &&
            dedupeNode is JsonValue dedupeValue &&
            dedupeValue.TryGetValue<string>(out var dedupeText) &&
            !string.IsNullOrWhiteSpace(dedupeText))
        {
            transactionId = dedupeText.Trim();
        }

        transactionId ??= alert.Id.ToString("N");
        var displayName = ReadFirstString(alert.Evidence, "externalReference", "referenceNumber")
            ?? alert.Title;
        var primaryEntity = string.IsNullOrWhiteSpace(transactionId)
            ? null
            : new FinanceInsightEntityReferenceDto("finance_transaction", transactionId, displayName, true);
        var affectedEntities = primaryEntity is null
            ? Array.Empty<FinanceInsightEntityReferenceDto>()
            : [primaryEntity];
        var recommendation = ReadFirstString(alert.Metadata, "recommendedAction")
            ?? ReadFirstString(alert.Evidence, "recommendedAction")
            ?? "Review the transaction, supporting evidence, and counterparty before closing the anomaly.";
        var confidence = ReadDecimal(alert.Metadata, "confidence") ?? ReadDecimal(alert.Evidence, "confidence") ?? 0.75m;

        return new FinancialCheckResult(
            Definition,
            $"{CheckCode}:{alert.Fingerprint}",
            primaryEntity?.EntityType ?? Definition.EntityScope,
            primaryEntity?.EntityId ?? alert.Id.ToString("N"),
            MapSeverity(alert.Severity),
            alert.Summary,
            recommendation,
            Math.Clamp(confidence, 0m, 1m),
            primaryEntity,
            affectedEntities,
            ObservedAtUtc: alert.UpdatedUtc);
    }

    private static FinancialCheckSeverity MapSeverity(AlertSeverity severity) =>
        severity switch
        {
            AlertSeverity.Critical => FinancialCheckSeverity.Critical,
            AlertSeverity.High => FinancialCheckSeverity.High,
            AlertSeverity.Medium => FinancialCheckSeverity.Medium,
            _ => FinancialCheckSeverity.Low
        };

    private static string? ReadFirstString(IReadOnlyDictionary<string, JsonNode?> nodes, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (nodes.TryGetValue(key, out var node) && node is not null)
            {
                var value = node.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonNode?> nodes, string key) =>
        nodes.TryGetValue(key, out var node) && node is not null
            ? node.GetValue<decimal>()
            : null;
}

internal sealed class OverdueReceivablesFinancialCheck : IFinancialCheck
{
    private readonly VirtualCompanyDbContext _dbContext;

    public OverdueReceivablesFinancialCheck(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FinancialCheckDefinition Definition => FinancialCheckDefinitions.OverdueReceivables;
    public string CheckCode => Definition.Code;

    public async Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)
    {
        var invoices = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => new InvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? "Unknown counterparty" : x.Counterparty.Name,
                x.InvoiceNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var incomingAllocations = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == context.CompanyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Incoming &&
                x.Payment.PaymentDate <= context.AsOfUtc)
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(x => new AllocationRow(x.Key, x.Sum(y => y.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

        return invoices
            .Where(x => IsOpenReceivable(x.Status, x.SettlementStatus) && x.DueUtc < context.AsOfUtc)
            .Select(x => new
            {
                Invoice = x,
                Outstanding = Math.Max(0m, x.Amount - incomingAllocations.GetValueOrDefault(x.Id))
            })
            .Where(x => x.Outstanding > 0m)
            .GroupBy(x => new { x.Invoice.CounterpartyId, x.Invoice.CounterpartyName })
            .Select(group =>
            {
                var invoiceItems = group.OrderByDescending(x => x.Outstanding).ToArray();
                var totalOutstanding = invoiceItems.Sum(x => x.Outstanding);
                var maxDaysOverdue = invoiceItems.Max(x => Math.Max(1, (context.AsOfUtc.Date - x.Invoice.DueUtc.Date).Days));
                var primaryEntity = new FinanceInsightEntityReferenceDto(
                    "counterparty",
                    group.Key.CounterpartyId.ToString("N"),
                    group.Key.CounterpartyName,
                    true);
                var affectedEntities = new List<FinanceInsightEntityReferenceDto> { primaryEntity };
                affectedEntities.AddRange(invoiceItems.Select(x =>
                    new FinanceInsightEntityReferenceDto("invoice", x.Invoice.Id.ToString("N"), x.Invoice.InvoiceNumber)));

                return new FinancialCheckResult(
                    Definition,
                    $"{CheckCode}:{group.Key.CounterpartyId:N}",
                    primaryEntity.EntityType,
                    primaryEntity.EntityId,
                    ResolveReceivablesSeverity(totalOutstanding, maxDaysOverdue),
                    $"{group.Key.CounterpartyName} has {totalOutstanding:0.00} {invoiceItems[0].Invoice.Currency} overdue across {invoiceItems.Length} receivable(s).",
                    "Escalate collection outreach, confirm payment commitments, and update the expected cash-in plan.",
                    ResolveConfidence(invoiceItems.Length, maxDaysOverdue),
                    primaryEntity,
                    affectedEntities,
                    ObservedAtUtc: context.AsOfUtc);
            })
            .ToArray();
    }

    private static FinancialCheckSeverity ResolveReceivablesSeverity(decimal totalOutstanding, int maxDaysOverdue) =>
        maxDaysOverdue >= 90 || totalOutstanding >= 25000m
            ? FinancialCheckSeverity.Critical
            : maxDaysOverdue >= 45 || totalOutstanding >= 10000m
                ? FinancialCheckSeverity.High
                : FinancialCheckSeverity.Medium;

    private static decimal ResolveConfidence(int itemCount, int maxDaysOverdue) =>
        Math.Clamp(0.72m + Math.Min(0.18m, itemCount * 0.02m) + Math.Min(0.08m, maxDaysOverdue / 365m), 0m, 0.98m);

    private static bool IsOpenReceivable(string status, string settlementStatus)
    {
        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant() ?? string.Empty;
        var normalizedSettlement = NormalizeOptionalText(settlementStatus)?.ToLowerInvariant() ?? string.Empty;
        return normalizedSettlement != FinanceSettlementStatuses.Paid &&
               normalizedStatus is not ("paid" or "rejected" or "void");
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record InvoiceRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string InvoiceNumber,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
}

internal sealed class PayablesFinancialCheck : IFinancialCheck
{
    private readonly VirtualCompanyDbContext _dbContext;

    public PayablesFinancialCheck(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FinancialCheckDefinition Definition => FinancialCheckDefinitions.PayablesPressure;
    public string CheckCode => Definition.Code;

    public async Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)
    {
        var bills = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => new BillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? "Unknown counterparty" : x.Counterparty.Name,
                x.BillNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var outgoingAllocations = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == context.CompanyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= context.AsOfUtc)
            .GroupBy(x => x.BillId!.Value)
            .Select(x => new AllocationRow(x.Key, x.Sum(y => y.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

        var payableWindowEndUtc = context.AsOfUtc.Date.AddDays(context.PayableWindowDays + 1);

        return bills
            .Where(x => IsOpenPayable(x.Status, x.SettlementStatus) && x.DueUtc < payableWindowEndUtc)
            .Select(x => new
            {
                Bill = x,
                Outstanding = Math.Max(0m, x.Amount - outgoingAllocations.GetValueOrDefault(x.Id))
            })
            .Where(x => x.Outstanding > 0m)
            .GroupBy(x => new { x.Bill.CounterpartyId, x.Bill.CounterpartyName })
            .Select(group =>
            {
                var billItems = group.OrderByDescending(x => x.Outstanding).ToArray();
                var totalOutstanding = billItems.Sum(x => x.Outstanding);
                var overdueCount = billItems.Count(x => x.Bill.DueUtc < context.AsOfUtc);
                var primaryEntity = new FinanceInsightEntityReferenceDto(
                    "counterparty",
                    group.Key.CounterpartyId.ToString("N"),
                    group.Key.CounterpartyName,
                    true);
                var affectedEntities = new List<FinanceInsightEntityReferenceDto> { primaryEntity };
                affectedEntities.AddRange(billItems.Select(x =>
                    new FinanceInsightEntityReferenceDto("bill", x.Bill.Id.ToString("N"), x.Bill.BillNumber)));

                return new FinancialCheckResult(
                    Definition,
                    $"{CheckCode}:{group.Key.CounterpartyId:N}",
                    primaryEntity.EntityType,
                    primaryEntity.EntityId,
                    ResolvePayablesSeverity(totalOutstanding, overdueCount),
                    $"{group.Key.CounterpartyName} has {totalOutstanding:0.00} {billItems[0].Bill.Currency} due or overdue within the next {context.PayableWindowDays} days.",
                    "Review near-term cash coverage, sequence the payment plan, and escalate overdue supplier commitments.",
                    Math.Clamp(0.74m + Math.Min(0.16m, billItems.Length * 0.02m) + (overdueCount > 0 ? 0.08m : 0m), 0m, 0.98m),
                    primaryEntity,
                    affectedEntities,
                    ObservedAtUtc: context.AsOfUtc);
            })
            .ToArray();
    }

    private static FinancialCheckSeverity ResolvePayablesSeverity(decimal totalOutstanding, int overdueCount) =>
        overdueCount >= 3 || totalOutstanding >= 25000m
            ? FinancialCheckSeverity.High
            : overdueCount > 0 || totalOutstanding >= 10000m
                ? FinancialCheckSeverity.Medium
                : FinancialCheckSeverity.Low;

    private static bool IsOpenPayable(string status, string settlementStatus)
    {
        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant() ?? string.Empty;
        var normalizedSettlement = NormalizeOptionalText(settlementStatus)?.ToLowerInvariant() ?? string.Empty;
        return normalizedSettlement != FinanceSettlementStatuses.Paid &&
               normalizedStatus is not ("paid" or "void" or "cancelled");
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BillRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string BillNumber,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
}

internal sealed class SupplierBillDueMonitoringFinancialCheck : IFinancialCheck
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SupplierBillDueMonitoringFinancialCheck(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FinancialCheckDefinition Definition => FinancialCheckDefinitions.SupplierBillDueMonitoring;
    public string CheckCode => Definition.Code;

    public async Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken)
    {
        var bills = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId)
            .Select(x => new BillDueRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? "Unknown supplier" : x.Counterparty.Name,
                x.BillNumber,
                x.DueUtc,
                x.Amount,
                x.PaidAmount,
                x.Currency,
                x.Status,
                x.PostingStatus,
                x.SettlementStatus,
                x.DueStatus,
                x.DocumentKind))
            .ToListAsync(cancellationToken);

        var outgoingAllocations = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == context.CompanyId &&
                x.BillId.HasValue &&
                x.Payment.Status == PaymentStatuses.Completed &&
                x.Payment.PaymentType == PaymentTypes.Outgoing &&
                x.Payment.PaymentDate <= context.AsOfUtc)
            .GroupBy(x => x.BillId!.Value)
            .Select(x => new AllocationRow(x.Key, x.Sum(y => y.AllocatedAmount)))
            .ToDictionaryAsync(x => x.DocumentId, x => x.Amount, cancellationToken);

        return bills
            .Where(IsDueAttentionBill)
            .Select(x => new
            {
                Bill = x,
                Outstanding = CalculateOutstanding(x, outgoingAllocations.GetValueOrDefault(x.Id))
            })
            .Where(x => x.Outstanding > 0m)
            .OrderBy(x => DueStatusRank(x.Bill.DueStatus))
            .ThenBy(x => x.Bill.DueUtc)
            .ThenBy(x => x.Bill.BillNumber, StringComparer.OrdinalIgnoreCase)
            .Select(x => MapResult(context, x.Bill, x.Outstanding))
            .ToArray();
    }

    private static FinancialCheckResult MapResult(FinancialCheckContext context, BillDueRow bill, decimal outstanding)
    {
        var normalizedDue = Normalize(bill.DueStatus);
        var overdue = normalizedDue == FinanceDocumentDueStatuses.Overdue;
        var dueDate = bill.DueUtc.Date;
        var days = overdue
            ? Math.Max(1, (context.AsOfUtc.Date - dueDate).Days)
            : Math.Max(0, (dueDate - context.AsOfUtc.Date).Days);
        var primaryEntity = new FinanceInsightEntityReferenceDto(
            "bill",
            bill.Id.ToString("N"),
            bill.BillNumber,
            true);
        var affectedEntities = new[]
        {
            primaryEntity,
            new FinanceInsightEntityReferenceDto("counterparty", bill.CounterpartyId.ToString("N"), bill.CounterpartyName)
        };
        var duePhrase = overdue
            ? $"{days} day{(days == 1 ? string.Empty : "s")} overdue"
            : days == 0
                ? "due today"
                : $"due in {days} day{(days == 1 ? string.Empty : "s")}";

        return new FinancialCheckResult(
            FinancialCheckDefinitions.SupplierBillDueMonitoring,
            $"supplier_bill_due:{bill.Id:N}",
            "bill",
            bill.Id.ToString("N"),
            overdue ? FinancialCheckSeverity.Medium : FinancialCheckSeverity.Low,
            $"Supplier bill {bill.BillNumber} from {bill.CounterpartyName} is unpaid and {duePhrase}.",
            overdue
                ? "Prioritize this supplier bill and confirm whether payment has been scheduled or completed."
                : "Schedule the supplier payment or confirm it is already in the payment run.",
            overdue ? 0.9m : 0.82m,
            primaryEntity,
            affectedEntities,
            ObservedAtUtc: context.AsOfUtc,
            MetadataJson: new JsonObject
            {
                ["dueStatus"] = JsonValue.Create(normalizedDue),
                ["dueUtc"] = JsonValue.Create(bill.DueUtc),
                ["outstandingAmount"] = JsonValue.Create(outstanding),
                ["currency"] = JsonValue.Create(bill.Currency),
                ["settlementStatus"] = JsonValue.Create(Normalize(bill.SettlementStatus))
            }.ToJsonString());
    }

    private static bool IsDueAttentionBill(BillDueRow bill)
    {
        var posting = Normalize(bill.PostingStatus);
        var settlement = Normalize(bill.SettlementStatus);
        var due = Normalize(bill.DueStatus);
        var kind = Normalize(bill.DocumentKind);
        var status = Normalize(bill.Status);

        return due is FinanceDocumentDueStatuses.DueSoon or FinanceDocumentDueStatuses.Overdue &&
               settlement is not FinanceSettlementStatuses.Paid and not FinanceSettlementStatuses.Credited &&
               posting != FinanceDocumentPostingStatuses.Cancelled &&
               kind != FinanceDocumentKinds.SupplierCreditNote &&
               status is not ("paid" or "void" or "cancelled" or "canceled");
    }

    private static decimal CalculateOutstanding(BillDueRow bill, decimal allocatedAmount)
    {
        var paidAmount = Math.Max(Math.Abs(bill.PaidAmount), Math.Abs(allocatedAmount));
        return Math.Max(0m, decimal.Round(Math.Abs(bill.Amount) - paidAmount, 2, MidpointRounding.AwayFromZero));
    }

    private static int DueStatusRank(string dueStatus) =>
        Normalize(dueStatus) switch
        {
            FinanceDocumentDueStatuses.Overdue => 0,
            FinanceDocumentDueStatuses.DueSoon => 1,
            _ => 2
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('-', '_').ToLowerInvariant();

    private sealed record BillDueRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string BillNumber,
        DateTime DueUtc,
        decimal Amount,
        decimal PaidAmount,
        string Currency,
        string Status,
        string PostingStatus,
        string SettlementStatus,
        string DueStatus,
        string DocumentKind);

    private sealed record AllocationRow(Guid DocumentId, decimal Amount);
}
