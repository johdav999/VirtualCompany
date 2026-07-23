using System.Text.Json.Nodes;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Events;

namespace VirtualCompany.Infrastructure.Finance;

internal static class FinanceDomainEvents
{
    public static void EnqueueTransactionCreated(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        FinanceTransaction transaction,
        string? correlationId = null)
    {
        if (outboxEnqueuer is null)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinanceTransactionCreated;
        var eventId = $"{eventType}:{transaction.Id:N}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);
        var occurredAtUtc = NormalizeUtc(transaction.CreatedUtc);

        outboxEnqueuer.Enqueue(
            transaction.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                transaction.CompanyId,
                effectiveCorrelationId,
                "finance_transaction",
                transaction.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(transaction.CompanyId),
                    ["recordId"] = JsonValue.Create(transaction.Id),
                    ["amount"] = JsonValue.Create(transaction.Amount),
                    ["category"] = JsonValue.Create(transaction.TransactionType),
                    ["sourceEntityVersion"] = JsonValue.Create(BuildSourceEntityVersion(transaction.CreatedUtc)),
                    ["timestampUtc"] = JsonValue.Create(transaction.TransactionUtc)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{transaction.CompanyId:N}:{eventId}",
            causationId: transaction.Id.ToString("N"));
    }

    public static void EnqueueBillCreated(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        FinanceBill bill,
        string supplierReference,
        string? correlationId = null)
    {
        if (outboxEnqueuer is null)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinanceBillCreated;
        var eventId = $"{eventType}:{bill.Id:N}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);
        var occurredAtUtc = NormalizeUtc(bill.CreatedUtc);

        outboxEnqueuer.Enqueue(
            bill.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                bill.CompanyId,
                effectiveCorrelationId,
                "finance_bill",
                bill.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(bill.CompanyId),
                    ["billId"] = JsonValue.Create(bill.Id),
                    ["billNumber"] = JsonValue.Create(bill.BillNumber),
                    ["supplierReference"] = JsonValue.Create(supplierReference),
                    ["amount"] = JsonValue.Create(bill.Amount),
                    ["dueDateUtc"] = JsonValue.Create(bill.DueUtc),
                    ["sourceEntityVersion"] = JsonValue.Create(BuildSourceEntityVersion(bill.UpdatedUtc)),
                    ["receivedUtc"] = JsonValue.Create(bill.ReceivedUtc)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{bill.CompanyId:N}:{eventId}",
            causationId: bill.Id.ToString("N"));
    }

    public static void EnqueuePaymentCreated(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        Payment payment,
        string? correlationId = null)
    {
        if (outboxEnqueuer is null)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinancePaymentCreated;
        var eventId = $"{eventType}:{payment.Id:N}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);
        var occurredAtUtc = NormalizeUtc(payment.CreatedUtc);

        outboxEnqueuer.Enqueue(
            payment.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                payment.CompanyId,
                effectiveCorrelationId,
                "payment",
                payment.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(payment.CompanyId),
                    ["paymentId"] = JsonValue.Create(payment.Id),
                    ["paymentType"] = JsonValue.Create(payment.PaymentType),
                    ["amount"] = JsonValue.Create(payment.Amount),
                    ["currency"] = JsonValue.Create(payment.Currency),
                    ["paymentDate"] = JsonValue.Create(payment.PaymentDate),
                    ["status"] = JsonValue.Create(payment.Status),
                    ["sourceEntityVersion"] = JsonValue.Create(BuildSourceEntityVersion(payment.UpdatedUtc)),
                    ["counterpartyReference"] = JsonValue.Create(payment.CounterpartyReference)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{payment.CompanyId:N}:{eventId}",
            causationId: payment.Id.ToString("N"));
    }

    public static void EnqueueInvoiceCreated(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        FinanceInvoice invoice,
        string supplierOrCustomerReference,
        string? correlationId = null)
    {
        if (outboxEnqueuer is null)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated;
        var eventId = $"{eventType}:{invoice.Id:N}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);
        var occurredAtUtc = NormalizeUtc(invoice.CreatedUtc);

        outboxEnqueuer.Enqueue(
            invoice.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                invoice.CompanyId,
                effectiveCorrelationId,
                "finance_invoice",
                invoice.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(invoice.CompanyId),
                    ["invoiceId"] = JsonValue.Create(invoice.Id),
                    ["supplierOrCustomerReference"] = JsonValue.Create(supplierOrCustomerReference),
                    ["amount"] = JsonValue.Create(invoice.Amount),
                    ["sourceEntityVersion"] = JsonValue.Create(BuildSourceEntityVersion(invoice.UpdatedUtc)),
                    ["dueDateUtc"] = JsonValue.Create(invoice.DueUtc),
                    ["timestampUtc"] = JsonValue.Create(invoice.CreatedUtc)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{invoice.CompanyId:N}:{eventId}",
            causationId: invoice.Id.ToString("N"));
    }

    public static void EnqueueSimulationDayAdvanced(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        Guid companyId,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        int hoursAdvanced,
        string? correlationId = null)
    {
        if (outboxEnqueuer is null || companyId == Guid.Empty)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinanceSimulationDayAdvanced;
        var normalizedWindowStartUtc = NormalizeUtc(windowStartUtc);
        var normalizedWindowEndUtc = NormalizeUtc(windowEndUtc);
        var eventId = $"{eventType}:{companyId:N}:{normalizedWindowEndUtc:yyyyMMddHHmm}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);

        outboxEnqueuer.Enqueue(
            companyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                normalizedWindowEndUtc,
                companyId,
                effectiveCorrelationId,
                "company_simulation",
                companyId.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(companyId),
                    ["sourceEntityVersion"] = JsonValue.Create(BuildSourceEntityVersion(normalizedWindowEndUtc)),
                    ["windowStartUtc"] = JsonValue.Create(normalizedWindowStartUtc),
                    ["windowEndUtc"] = JsonValue.Create(normalizedWindowEndUtc),
                    ["hoursAdvanced"] = JsonValue.Create(hoursAdvanced)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            causationId: companyId.ToString("N"));
    }

    public static void EnqueueThresholdBreached(
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        Guid companyId,
        string breachType,
        string sourceEntityType,
        Guid affectedRecordId,
        DateTime occurredAtUtc,
        IReadOnlyDictionary<string, JsonNode?> evaluationDetails,
        string? correlationId = null,
        string? idempotencyScope = null)
    {
        if (outboxEnqueuer is null || companyId == Guid.Empty || affectedRecordId == Guid.Empty)
        {
            return;
        }

        var eventType = SupportedPlatformEventTypeRegistry.FinanceThresholdBreached;
        var eventId = string.IsNullOrWhiteSpace(idempotencyScope)
            ? $"{eventType}:{affectedRecordId:N}:{breachType.Trim().ToLowerInvariant()}"
            : $"{eventType}:{affectedRecordId:N}:{breachType.Trim().ToLowerInvariant()}:{idempotencyScope.Trim()}";
        var effectiveCorrelationId = ResolveCorrelationId(correlationId, eventId);

        outboxEnqueuer.Enqueue(
            companyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                NormalizeUtc(occurredAtUtc),
                companyId,
                effectiveCorrelationId,
                string.IsNullOrWhiteSpace(sourceEntityType) ? "finance_record" : sourceEntityType.Trim(),
                affectedRecordId.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["breachType"] = JsonValue.Create(breachType),
                    ["affectedRecordId"] = JsonValue.Create(affectedRecordId),
                    ["evaluationDetails"] = new JsonObject(evaluationDetails.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase))
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            causationId: affectedRecordId.ToString("N"));
    }

    private static string ResolveCorrelationId(string? correlationId, string eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId : correlationId.Trim();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string BuildSourceEntityVersion(DateTime value) =>
        NormalizeUtc(value).ToString("O");
}
