namespace VirtualCompany.Application.Finance;

public static class CustomerInvoiceDeliveryReasonCodes
{
    public const string InvoiceNotFound = "customer_invoice_delivery_invoice_not_found";
    public const string ArtifactNotFound = "customer_invoice_delivery_artifact_not_found";
    public const string ArtifactNotReady = "customer_invoice_delivery_artifact_not_ready";
    public const string DeliveryAddressMissing = "customer_invoice_delivery_address_missing";
    public const string IdempotencyConflict = "customer_invoice_delivery_idempotency_conflict";
    public const string ReconciliationRequired = "customer_invoice_delivery_reconciliation_required";
    public const string PeppolProviderUnavailable = "customer_invoice_delivery_peppol_provider_unavailable";
    public const string PeppolRecipientUnsupported = "customer_invoice_delivery_peppol_recipient_unsupported";
    public const string PeppolValidationFailed = "customer_invoice_delivery_peppol_validation_failed";
    public const string PeppolRejected = "customer_invoice_delivery_peppol_rejected";
    public const string PeppolOutcomePending = "customer_invoice_delivery_peppol_outcome_pending";
    public const string PeppolCredentialsMissing = "customer_invoice_delivery_peppol_credentials_missing";
    public const string PeppolProfileUnsupported = "customer_invoice_delivery_peppol_profile_unsupported";
    public const string PeppolRateLimited = "customer_invoice_delivery_peppol_rate_limited";
    public const string PeppolRetryNotAllowed = "customer_invoice_delivery_peppol_retry_not_allowed";
    public const string PeppolDeliveryNotFound = "customer_invoice_delivery_peppol_delivery_not_found";
    public const string PeppolWebhookInvalid = "customer_invoice_delivery_peppol_webhook_invalid";
    public const string EmailFallbackDisabled = "customer_invoice_delivery_email_fallback_disabled";
}
public static class CustomerInvoiceDeliveryChannels
{
    public const string Peppol = "peppol";
    public const string Email = "email";
    public const string None = "none";
}
public static class CustomerInvoiceElectronicDeliveryOutcomes
{
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Delivered = "delivered";
    public const string Unavailable = "unavailable";
    public const string RecipientUnsupported = "recipient_unsupported";
    public const string ValidationFailed = "validation_failed";
    public const string Rejected = "rejected";
    public const string RetryableFailure = "retryable_failure";
    public const string ReconciliationRequired = "reconciliation_required";
}
public static class CustomerInvoiceEmailRequestSources
{
    public const string Direct = "direct";
    public const string PeppolFallback = "peppol_fallback";
}
public sealed record RequestCustomerInvoiceRenderCommand(Guid CompanyId, Guid InvoiceId, string Locale, string TemplateVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record RequestCustomerInvoiceEmailDeliveryCommand(Guid CompanyId, Guid InvoiceId, Guid ArtifactId, string? RecipientEmail, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RequestCustomerInvoicePreferredDeliveryCommand(Guid CompanyId, Guid InvoiceId, Guid ArtifactId, string? RecipientEmail, bool AllowEmailFallback, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record ResendCustomerInvoiceEmailCommand(Guid CompanyId, Guid DeliveryId, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RequestCustomerInvoiceElectronicDeliveryCommand(Guid CompanyId, Guid InvoiceId, Guid ArtifactId, bool AllowEmailFallback, string? RecipientEmail, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RetryCustomerInvoiceElectronicDeliveryCommand(Guid CompanyId, Guid DeliveryId, string Reason, Guid ActorUserId, string? CorrelationId = null);
public sealed record ReconcileCustomerInvoiceElectronicDeliveryCommand(Guid CompanyId, Guid DeliveryId, string Reason, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetCustomerInvoiceArtifactQuery(Guid CompanyId, Guid ArtifactId);
public sealed record GetCustomerInvoiceDeliveryQuery(Guid CompanyId, Guid DeliveryId);
public sealed record GetCustomerInvoiceElectronicDeliveryQuery(Guid CompanyId, Guid DeliveryId);
public sealed record CustomerInvoiceArtifactDto(Guid Id, Guid InvoiceId, string SnapshotHash, string TemplateVersion, string Locale, string MediaType, string FileName, string Status, string? ContentHash, long? ContentLength, int GenerationAttempts, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? RenderedUtc);
public sealed record CustomerInvoiceEmailDeliveryDto(Guid Id, Guid InvoiceId, Guid ArtifactId, string Status, int Attempts, string? ProviderReference, string? FailureCode, string? FailureSummary, string RequestSource, string? FallbackReasonCode, string? FallbackProviderKey, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? AcceptedUtc);
public sealed record CustomerInvoiceElectronicDeliveryCommand(Guid CompanyId, Guid InvoiceId, Guid ArtifactId, bool AllowEmailFallback, string? RecipientEmail, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record CustomerInvoiceElectronicDeliveryResult(string Outcome, string ReasonCode, string SafeExplanation, bool IsSafeToFallback, string? ProviderKey = null, string? Profile = null, string? DeliveryId = null);
public sealed record CustomerInvoiceElectronicDeliveryDto(Guid Id, Guid InvoiceId, Guid ArtifactId, string ProviderKey,
    string Profile, string ProfileVersion, string ParticipantScheme, string ParticipantIdentifier, string DocumentType,
    string Status, string Outcome, int SubmissionAttempts, int ReconciliationAttempts, string? ProviderReference,
    string? ProviderState, string? FailureCode, string? FailureSummary, bool AllowEmailFallback, Guid? FallbackEmailDeliveryId,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? SubmittedUtc, DateTime? DeliveredUtc, DateTime? NextReconcileUtc);
public sealed record CustomerInvoiceElectronicProviderCapabilityDto(string ProviderKey, bool Enabled, string Environment,
    string Status, string SafeMessage, IReadOnlyCollection<string> Profiles, IReadOnlyCollection<string> DocumentTypes,
    bool SupportsParticipantValidation, bool SupportsDocumentValidation, bool SupportsAttachments,
    bool SupportsAcknowledgementPolling, bool SupportsWebhooks, bool SupportsCancellation, string ApiVersion);
public sealed record CustomerInvoiceElectronicParticipantValidation(string Status, string SafeMessage,
    string ParticipantScheme, string ParticipantIdentifier, IReadOnlyCollection<string> SupportedDocumentTypes,
    bool IsRetryable);
public sealed record CustomerInvoiceElectronicDocumentValidation(bool IsValid, string Profile, string ProfileVersion,
    string DocumentHash, IReadOnlyCollection<string> ReasonCodes, IReadOnlyCollection<string> SafeMessages);
public sealed record CustomerInvoiceElectronicProviderSubmission(string Outcome, string? ProviderReference,
    string? ProviderState, string SafeMessage, bool IsRetryable, bool IsAmbiguous, bool IsSafeToFallback);
public sealed record CustomerInvoiceElectronicProviderStatus(string Outcome, string? ProviderReference,
    string? ProviderState, string SafeMessage, bool IsTerminal, bool IsSafeToFallback);
public sealed record CustomerInvoiceElectronicWebhookCommand(string Signature, string RawBody, DateTime ReceivedUtc);
public sealed record CustomerInvoiceElectronicWebhookResult(bool Accepted, bool Duplicate, string SafeMessage);
public sealed record CustomerInvoicePreferredDeliveryDto(string PreferredChannel, string SelectedChannel, string Status, string ReasonCode, bool UsedEmailFallback, string? ElectronicProviderKey, string? ElectronicProfile, string? ElectronicDeliveryId, CustomerInvoiceEmailDeliveryDto? EmailDelivery);
public interface ICustomerInvoiceElectronicDeliveryProvider
{
    string ProviderKey { get; }
    Task<CustomerInvoiceElectronicDeliveryResult> TryQueueAsync(CustomerInvoiceElectronicDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicProviderCapabilityDto> GetCapabilityAsync(Guid companyId, CancellationToken cancellationToken);
    Task ProcessAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken);
    Task ReconcileAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicWebhookResult> ProcessWebhookAsync(CustomerInvoiceElectronicWebhookCommand command, CancellationToken cancellationToken);
}
public interface ICustomerInvoiceDeliveryService
{
    Task<CustomerInvoiceArtifactDto> RequestRenderAsync(RequestCustomerInvoiceRenderCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceEmailDeliveryDto> RequestEmailAsync(RequestCustomerInvoiceEmailDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoicePreferredDeliveryDto> RequestPreferredDeliveryAsync(RequestCustomerInvoicePreferredDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicDeliveryDto> RequestElectronicAsync(RequestCustomerInvoiceElectronicDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicDeliveryDto> RetryElectronicAsync(RetryCustomerInvoiceElectronicDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicDeliveryDto> ReconcileElectronicAsync(ReconcileCustomerInvoiceElectronicDeliveryCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceEmailDeliveryDto> ResendAsync(ResendCustomerInvoiceEmailCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceArtifactDto> GetArtifactAsync(GetCustomerInvoiceArtifactQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceEmailDeliveryDto> GetDeliveryAsync(GetCustomerInvoiceDeliveryQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicDeliveryDto> GetElectronicDeliveryAsync(GetCustomerInvoiceElectronicDeliveryQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceElectronicProviderCapabilityDto> GetElectronicProviderCapabilityAsync(Guid companyId, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName)> OpenArtifactAsync(Guid companyId, Guid artifactId, CancellationToken cancellationToken);
}
public interface ICustomerInvoiceDeliveryDispatcher
{
    Task RenderAsync(Guid companyId, Guid artifactId, CancellationToken cancellationToken);
    Task DeliverAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken);
    Task DeliverElectronicAsync(Guid companyId, Guid deliveryId, string providerKey, CancellationToken cancellationToken);
    Task ReconcileElectronicAsync(Guid companyId, Guid deliveryId, string providerKey, CancellationToken cancellationToken);
}
public sealed class CustomerInvoiceDeliveryException(string reasonCode, string message, bool conflict = false) : Exception(message) { public string ReasonCode { get; } = reasonCode; public bool Conflict { get; } = conflict; }
