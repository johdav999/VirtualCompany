namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceArtifactResponse> RequestCustomerInvoiceRenderAsync(Guid companyId, Guid invoiceId, InvoiceRenderApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceRenderApiRequest, CustomerInvoiceArtifactResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/render", request, ct); }
    public Task<CustomerInvoiceArtifactResponse?> GetCustomerInvoiceArtifactAsync(Guid companyId, Guid artifactId, CancellationToken ct = default) => GetAsync<CustomerInvoiceArtifactResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoices/artifacts/{artifactId:D}", true, ct);
    public Task<CustomerInvoiceEmailDeliveryResponse> RequestCustomerInvoiceEmailAsync(Guid companyId, Guid invoiceId, InvoiceEmailDeliveryApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceEmailDeliveryApiRequest, CustomerInvoiceEmailDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/email-deliveries", request, ct); }
    public Task<CustomerInvoicePreferredDeliveryResponse> RequestCustomerInvoicePreferredDeliveryAsync(Guid companyId, Guid invoiceId, InvoicePreferredDeliveryApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoicePreferredDeliveryApiRequest, CustomerInvoicePreferredDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/preferred-delivery", request, ct); }
    public Task<CustomerInvoiceEmailDeliveryResponse?> GetCustomerInvoiceEmailDeliveryAsync(Guid companyId, Guid deliveryId, CancellationToken ct = default) => GetAsync<CustomerInvoiceEmailDeliveryResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoices/email-deliveries/{deliveryId:D}", true, ct);
    public Task<CustomerInvoiceEmailDeliveryResponse> ResendCustomerInvoiceEmailAsync(Guid companyId, Guid deliveryId, InvoiceResendApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceResendApiRequest, CustomerInvoiceEmailDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/email-deliveries/{deliveryId:D}/resend", request, ct); }
    public Task<CustomerInvoiceElectronicProviderCapabilityResponse?> GetCustomerInvoiceElectronicProviderAsync(Guid companyId, CancellationToken ct = default) => GetAsync<CustomerInvoiceElectronicProviderCapabilityResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-provider", true, ct);
    public Task<CustomerInvoiceElectronicDeliveryResponse> RequestCustomerInvoiceElectronicAsync(Guid companyId, Guid invoiceId, InvoiceElectronicDeliveryApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceElectronicDeliveryApiRequest, CustomerInvoiceElectronicDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/electronic-deliveries", request, ct); }
    public Task<CustomerInvoiceElectronicDeliveryResponse?> GetCustomerInvoiceElectronicDeliveryAsync(Guid companyId, Guid deliveryId, CancellationToken ct = default) => GetAsync<CustomerInvoiceElectronicDeliveryResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-deliveries/{deliveryId:D}", true, ct);
    public Task<CustomerInvoiceElectronicDeliveryResponse> RetryCustomerInvoiceElectronicAsync(Guid companyId, Guid deliveryId, InvoiceElectronicOperatorApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceElectronicOperatorApiRequest, CustomerInvoiceElectronicDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-deliveries/{deliveryId:D}/retry", request, ct); }
    public Task<CustomerInvoiceElectronicDeliveryResponse> ReconcileCustomerInvoiceElectronicAsync(Guid companyId, Guid deliveryId, InvoiceElectronicOperatorApiRequest request, CancellationToken ct = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<InvoiceElectronicOperatorApiRequest, CustomerInvoiceElectronicDeliveryResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoices/electronic-deliveries/{deliveryId:D}/reconcile", request, ct); }
}
public sealed record InvoiceRenderApiRequest(string Locale = "en-US", string TemplateVersion = "native-invoice-pdf-2026.1");
public sealed record InvoiceEmailDeliveryApiRequest(Guid ArtifactId, string? RecipientEmail, string Reason, string IdempotencyKey);
public sealed record InvoicePreferredDeliveryApiRequest(Guid ArtifactId, string? RecipientEmail, bool AllowEmailFallback, string Reason, string IdempotencyKey);
public sealed record InvoiceResendApiRequest(string Reason, string IdempotencyKey);
public sealed record InvoiceElectronicDeliveryApiRequest(Guid ArtifactId, bool AllowEmailFallback, string? RecipientEmail,
    string Reason, string IdempotencyKey);
public sealed record InvoiceElectronicOperatorApiRequest(string Reason);
public sealed record CustomerInvoiceArtifactResponse(Guid Id, Guid InvoiceId, string SnapshotHash, string TemplateVersion, string Locale, string MediaType, string FileName, string Status, string? ContentHash, long? ContentLength, int GenerationAttempts, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? RenderedUtc);
public sealed record CustomerInvoiceEmailDeliveryResponse(Guid Id, Guid InvoiceId, Guid ArtifactId, string Status, int Attempts, string? ProviderReference, string? FailureCode, string? FailureSummary, string RequestSource, string? FallbackReasonCode, string? FallbackProviderKey, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? AcceptedUtc);
public sealed record CustomerInvoicePreferredDeliveryResponse(string PreferredChannel, string SelectedChannel, string Status, string ReasonCode, bool UsedEmailFallback, string? ElectronicProviderKey, string? ElectronicProfile, string? ElectronicDeliveryId, CustomerInvoiceEmailDeliveryResponse? EmailDelivery);
public sealed record CustomerInvoiceElectronicDeliveryResponse(Guid Id, Guid InvoiceId, Guid ArtifactId,
    string ProviderKey, string Profile, string ProfileVersion, string ParticipantScheme, string ParticipantIdentifier,
    string DocumentType, string Status, string Outcome, int SubmissionAttempts, int ReconciliationAttempts,
    string? ProviderReference, string? ProviderState, string? FailureCode, string? FailureSummary,
    bool AllowEmailFallback, Guid? FallbackEmailDeliveryId, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? SubmittedUtc, DateTime? DeliveredUtc, DateTime? NextReconcileUtc);
public sealed record CustomerInvoiceElectronicProviderCapabilityResponse(string ProviderKey, bool Enabled,
    string Environment, string Status, string SafeMessage, IReadOnlyCollection<string> Profiles,
    IReadOnlyCollection<string> DocumentTypes, bool SupportsParticipantValidation, bool SupportsDocumentValidation,
    bool SupportsAttachments, bool SupportsAcknowledgementPolling, bool SupportsWebhooks,
    bool SupportsCancellation, string ApiVersion);
