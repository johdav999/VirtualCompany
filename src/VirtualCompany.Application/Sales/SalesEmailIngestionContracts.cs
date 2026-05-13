using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Application.Sales;

public sealed record ProcessSalesEmailMessageCommand(
    Guid CompanyId,
    Guid UserId,
    Guid MailboxConnectionId,
    string ProviderMessageId);

public sealed record ProcessSalesEmailThreadCommand(
    Guid CompanyId,
    Guid UserId,
    Guid MailboxConnectionId,
    string ProviderThreadId);

public sealed record SalesEmailIngestionResult(
    string Status,
    bool AlreadyProcessed,
    Guid? LeadId,
    Guid? ContactId,
    Guid? ActivityId,
    Guid? CustomerCompanyId,
    Guid MailboxConnectionId,
    string Provider,
    string? ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string? IgnoreReason,
    string? Rationale,
    SalesEmailSignalResult? Signal);

public sealed record SalesEmailIntentExtractionRequest(
    Guid CompanyId,
    string Provider,
    Guid MailboxConnectionId,
    IReadOnlyList<MailboxInboundMessage> Messages);

public sealed record SalesEmailIntentExtractionResult(
    string Classification,
    SalesEmailSignalResult? Signal,
    string? IgnoreReason,
    string Rationale,
    bool UsedFallback = false);

public interface ISalesEmailIntentExtractionService
{
    Task<SalesEmailIntentExtractionResult?> ExtractAsync(
        SalesEmailIntentExtractionRequest request,
        CancellationToken cancellationToken);
}

public static class SalesEmailIntentClassifications
{
    public const string SalesLead = "sales_lead";
    public const string Ignore = "ignore";
    public const string Uncertain = "uncertain";
}

public static class SalesEmailIntents
{
    public const string DemoRequest = "demo request";
    public const string PricingRequest = "pricing request";
    public const string QuoteRequest = "quote request";
    public const string ExpansionInterest = "expansion interest";
    public const string BuyingInterest = "buying interest";
}

public static class SalesEmailUrgencies
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

public sealed record SalesEmailSignalResult(
    string SenderEmail,
    string? ContactName,
    string? CompanyName,
    string Intent,
    string? ProductOrServiceInterest,
    string Urgency,
    decimal Confidence);

public interface ISalesEmailIngestionService
{
    Task<SalesEmailIngestionResult> ProcessMessageAsync(
        ProcessSalesEmailMessageCommand command,
        CancellationToken cancellationToken);

    Task<SalesEmailIngestionResult> ProcessThreadAsync(
        ProcessSalesEmailThreadCommand command,
        CancellationToken cancellationToken);
}

public static class SalesEmailIngestionStatuses
{
    public const string Processed = "processed";
    public const string Ignored = "ignored";
    public const string AlreadyProcessed = "already_processed";
}

public static class SalesEmailIgnoreReasons
{
    public const string Newsletter = "newsletter";
    public const string Receipt = "receipt";
    public const string Invoice = "invoice";
    public const string SupportTicket = "support_ticket_without_upsell_intent";
    public const string NonSalesOperational = "non_sales_operational";
    public const string InsufficientSignal = "insufficient_signal";
    public const string NoSalesIntent = InsufficientSignal;
}

public static class SalesEmailLinkKinds
{
    public const string Message = "message";
    public const string Thread = "thread";
}

public static class SalesEmailDomainEvents
{
    public const string EmailReceived = "sales.email.received";
    public const string LeadDetected = "sales.lead.detected";
}