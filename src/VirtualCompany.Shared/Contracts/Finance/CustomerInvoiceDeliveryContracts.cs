

namespace VirtualCompany.Application.Finance;

public sealed record CustomerInvoiceElectronicDeliveryDto(Guid Id, Guid InvoiceId, Guid ArtifactId, string ProviderKey,
    string Profile, string ProfileVersion, string ParticipantScheme, string ParticipantIdentifier, string DocumentType,
    string Status, string Outcome, int SubmissionAttempts, int ReconciliationAttempts, string? ProviderReference,
    string? ProviderState, string? FailureCode, string? FailureSummary, bool AllowEmailFallback, Guid? FallbackEmailDeliveryId,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? SubmittedUtc, DateTime? DeliveredUtc, DateTime? NextReconcileUtc);

public sealed record CustomerInvoiceElectronicProviderCapabilityDto(string ProviderKey, bool Enabled, string Environment,
    string Status, string SafeMessage, IReadOnlyCollection<string> Profiles, IReadOnlyCollection<string> DocumentTypes,
    bool SupportsParticipantValidation, bool SupportsDocumentValidation, bool SupportsAttachments,
    bool SupportsAcknowledgementPolling, bool SupportsWebhooks, bool SupportsCancellation, string ApiVersion);

public sealed record CustomerInvoiceEmailDeliveryDto(Guid Id, Guid InvoiceId, Guid ArtifactId, string Status, int Attempts, string? ProviderReference, string? FailureCode, string? FailureSummary, string RequestSource, string? FallbackReasonCode, string? FallbackProviderKey, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? AcceptedUtc);

public sealed record CustomerInvoiceArtifactDto(Guid Id, Guid InvoiceId, string SnapshotHash, string TemplateVersion, string Locale, string MediaType, string FileName, string Status, string? ContentHash, long? ContentLength, int GenerationAttempts, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? RenderedUtc);

public sealed record CustomerInvoicePreferredDeliveryDto(string PreferredChannel, string SelectedChannel, string Status, string ReasonCode, bool UsedEmailFallback, string? ElectronicProviderKey, string? ElectronicProfile, string? ElectronicDeliveryId, CustomerInvoiceEmailDeliveryDto? EmailDelivery);
