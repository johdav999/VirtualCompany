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
                    ["timestampUtc"] = JsonValue.Create(transaction.TransactionUtc)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{transaction.CompanyId:N}:{eventId}",
            causationId: transaction.Id.ToString("N"));
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
                    ["dueDateUtc"] = JsonValue.Create(invoice.DueUtc),
                    ["timestampUtc"] = JsonValue.Create(invoice.CreatedUtc)
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{invoice.CompanyId:N}:{eventId}",
            causationId: invoice.Id.ToString("N"));
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
}
