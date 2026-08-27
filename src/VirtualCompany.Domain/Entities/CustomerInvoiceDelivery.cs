namespace VirtualCompany.Domain.Entities;

public static class CustomerInvoiceRenderStatuses
{
    public const string Pending = "pending";
    public const string Rendering = "rendering";
    public const string Rendered = "rendered";
    public const string Failed = "failed";
}

public static class CustomerInvoiceDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string Accepted = "accepted";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
}

public static class CustomerInvoiceElectronicDeliveryStatuses
{
    public const string Pending = "pending";
    public const string VerifyingParticipant = "verifying_participant";
    public const string ValidatingDocument = "validating_document";
    public const string Submitting = "submitting";
    public const string Accepted = "accepted_for_processing";
    public const string Delivered = "delivered";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
}

public sealed class CustomerInvoiceRenderedArtifact : ICompanyOwnedEntity
{
    private CustomerInvoiceRenderedArtifact() { }
    public CustomerInvoiceRenderedArtifact(Guid id, Guid companyId, Guid invoiceId, Guid issuedDocumentId,
        string snapshotHash, string templateVersion, string locale, string fileName, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || invoiceId == Guid.Empty || issuedDocumentId == Guid.Empty) throw new ArgumentException("Company, invoice, and issued document are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; InvoiceId = invoiceId; IssuedDocumentId = issuedDocumentId;
        SnapshotHash = Required(snapshotHash, 64); TemplateVersion = Required(templateVersion, 64); Locale = Required(locale, 16);
        FileName = Required(fileName, 255); MediaType = "application/pdf"; Status = CustomerInvoiceRenderStatuses.Pending; CreatedUtc = UpdatedUtc = nowUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InvoiceId { get; private set; }
    public Guid IssuedDocumentId { get; private set; } public string SnapshotHash { get; private set; } = null!;
    public string TemplateVersion { get; private set; } = null!; public string Locale { get; private set; } = null!;
    public string MediaType { get; private set; } = null!; public string FileName { get; private set; } = null!;
    public string Status { get; private set; } = null!; public string? ContentHash { get; private set; } public long? ContentLength { get; private set; }
    public string? ObjectKey { get; private set; } public int GenerationAttempts { get; private set; } public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? RenderedUtc { get; private set; }
    public void Start(DateTime nowUtc) { Status = CustomerInvoiceRenderStatuses.Rendering; GenerationAttempts++; FailureCode = FailureSummary = null; UpdatedUtc = nowUtc; }
    public void Complete(string objectKey, string contentHash, long contentLength, DateTime nowUtc) { ObjectKey = Required(objectKey, 1024); ContentHash = Required(contentHash, 64); ContentLength = contentLength; Status = CustomerInvoiceRenderStatuses.Rendered; RenderedUtc = UpdatedUtc = nowUtc; FailureCode = FailureSummary = null; }
    public void Fail(string code, string summary, DateTime nowUtc) { Status = CustomerInvoiceRenderStatuses.Failed; FailureCode = Required(code, 100); FailureSummary = Required(summary, 1000); UpdatedUtc = nowUtc; }
    private static string Required(string value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A delivery value is invalid.") : x; }
}

public sealed class CustomerInvoiceEmailDelivery : ICompanyOwnedEntity
{
    private CustomerInvoiceEmailDelivery() { }
    public CustomerInvoiceEmailDelivery(Guid id, Guid companyId, Guid invoiceId, Guid artifactId, string artifactHash,
        string recipientEmail, string recipientSnapshotHash, string subject, string reason, string idempotencyKey,
        Guid requestedByUserId, DateTime nowUtc, string requestSource = "direct",
        string? fallbackReasonCode = null, string? fallbackProviderKey = null)
    {
        if (companyId == Guid.Empty || invoiceId == Guid.Empty || artifactId == Guid.Empty || requestedByUserId == Guid.Empty) throw new ArgumentException("Delivery identity is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; InvoiceId = invoiceId; ArtifactId = artifactId; RequestedByUserId = requestedByUserId;
        ArtifactHash = Required(artifactHash, 64); RecipientEmail = Required(recipientEmail, 320).ToLowerInvariant(); RecipientSnapshotHash = Required(recipientSnapshotHash, 64);
        Subject = Required(subject, 300); Reason = Required(reason, 500); IdempotencyKey = Required(idempotencyKey, 200);
        RequestSource = Required(requestSource, 32); FallbackReasonCode = Optional(fallbackReasonCode, 100);
        FallbackProviderKey = Optional(fallbackProviderKey, 64);
        if (RequestSource == "peppol_fallback" && FallbackReasonCode is null)
            throw new ArgumentException("A Peppol fallback delivery requires a typed fallback reason.");
        Status = CustomerInvoiceDeliveryStatuses.Pending; CreatedUtc = UpdatedUtc = nowUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid InvoiceId { get; private set; } public Guid ArtifactId { get; private set; }
    public string ArtifactHash { get; private set; } = null!; public string RecipientEmail { get; private set; } = null!; public string RecipientSnapshotHash { get; private set; } = null!;
    public string Subject { get; private set; } = null!; public string Reason { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public string RequestSource { get; private set; } = null!; public string? FallbackReasonCode { get; private set; } public string? FallbackProviderKey { get; private set; }
    public string Status { get; private set; } = null!; public int Attempts { get; private set; } public string? ProviderReference { get; private set; }
    public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; } public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? AcceptedUtc { get; private set; }
    public void Start(DateTime nowUtc) { Status = CustomerInvoiceDeliveryStatuses.Sending; Attempts++; FailureCode = FailureSummary = null; UpdatedUtc = nowUtc; }
    public void Accepted(string? providerReference, DateTime nowUtc) { Status = CustomerInvoiceDeliveryStatuses.Accepted; ProviderReference = Trim(providerReference, 256); AcceptedUtc = UpdatedUtc = nowUtc; }
    public void Reconcile(string code, string summary, DateTime nowUtc) { Status = CustomerInvoiceDeliveryStatuses.ReconciliationRequired; FailureCode = Required(code, 100); FailureSummary = Required(summary, 1000); UpdatedUtc = nowUtc; }
    public void Fail(string code, string summary, DateTime nowUtc) { Status = CustomerInvoiceDeliveryStatuses.Failed; FailureCode = Required(code, 100); FailureSummary = Required(summary, 1000); UpdatedUtc = nowUtc; }
    private static string Required(string value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A delivery value is invalid.") : x; }
    private static string? Optional(string? value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) ? null : x.Length > max ? throw new ArgumentException("A delivery value is invalid.") : x; }
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}

public sealed class CustomerInvoiceElectronicDelivery : ICompanyOwnedEntity
{
    private CustomerInvoiceElectronicDelivery() { }

    public CustomerInvoiceElectronicDelivery(Guid id, Guid companyId, Guid invoiceId, Guid issuedDocumentId,
        Guid artifactId, string snapshotHash, string artifactHash, string providerKey, string profile,
        string profileVersion, string participantScheme, string participantIdentifier, string documentType,
        string documentNumber, string idempotencyKey, bool allowEmailFallback, string? fallbackRecipientEmail,
        string requestReason, Guid requestedByUserId, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || invoiceId == Guid.Empty || issuedDocumentId == Guid.Empty ||
            artifactId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new ArgumentException("Electronic delivery identity is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        InvoiceId = invoiceId;
        IssuedDocumentId = issuedDocumentId;
        ArtifactId = artifactId;
        SnapshotHash = Required(snapshotHash, 64);
        ArtifactHash = Required(artifactHash, 64);
        ProviderKey = Required(providerKey, 64).ToLowerInvariant();
        Profile = Required(profile, 128);
        ProfileVersion = Required(profileVersion, 64);
        ParticipantScheme = Required(participantScheme, 16);
        ParticipantIdentifier = Required(participantIdentifier, 128);
        DocumentType = Required(documentType, 32).ToLowerInvariant();
        DocumentNumber = Required(documentNumber, 100);
        IdempotencyKey = Required(idempotencyKey, 200);
        AllowEmailFallback = allowEmailFallback;
        FallbackRecipientEmail = Optional(fallbackRecipientEmail, 320)?.ToLowerInvariant();
        RequestReason = Required(requestReason, 500);
        RequestedByUserId = requestedByUserId;
        Status = CustomerInvoiceElectronicDeliveryStatuses.Pending;
        Outcome = "queued";
        CreatedUtc = UpdatedUtc = NormalizeUtc(nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid IssuedDocumentId { get; private set; }
    public Guid ArtifactId { get; private set; }
    public string SnapshotHash { get; private set; } = null!;
    public string ArtifactHash { get; private set; } = null!;
    public string ProviderKey { get; private set; } = null!;
    public string Profile { get; private set; } = null!;
    public string ProfileVersion { get; private set; } = null!;
    public string ParticipantScheme { get; private set; } = null!;
    public string ParticipantIdentifier { get; private set; } = null!;
    public string DocumentType { get; private set; } = null!;
    public string DocumentNumber { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public int SubmissionAttempts { get; private set; }
    public int ReconciliationAttempts { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? ProviderState { get; private set; }
    public string? DocumentHash { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public bool AllowEmailFallback { get; private set; }
    public string? FallbackRecipientEmail { get; private set; }
    public Guid? FallbackEmailDeliveryId { get; private set; }
    public string RequestReason { get; private set; } = null!;
    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public DateTime? DeliveredUtc { get; private set; }
    public DateTime? NextReconcileUtc { get; private set; }

    public bool ExternalSubmissionMayExist => ProviderReference is not null ||
        Status is CustomerInvoiceElectronicDeliveryStatuses.Submitting or CustomerInvoiceElectronicDeliveryStatuses.Accepted or
            CustomerInvoiceElectronicDeliveryStatuses.Delivered or CustomerInvoiceElectronicDeliveryStatuses.ReconciliationRequired;

    public void StartParticipantVerification(DateTime nowUtc)
    {
        if (Status == CustomerInvoiceElectronicDeliveryStatuses.Delivered) return;
        Status = CustomerInvoiceElectronicDeliveryStatuses.VerifyingParticipant;
        SubmissionAttempts++;
        ClearFailure();
        Touch(nowUtc);
    }

    public void StartDocumentValidation(DateTime nowUtc)
    {
        Status = CustomerInvoiceElectronicDeliveryStatuses.ValidatingDocument;
        ClearFailure();
        Touch(nowUtc);
    }

    public void RecordDocumentHash(string documentHash, DateTime nowUtc)
    {
        DocumentHash = Required(documentHash, 64);
        Touch(nowUtc);
    }

    public void StartSubmission(DateTime nowUtc)
    {
        if (ExternalSubmissionMayExist)
            throw new InvalidOperationException("An electronic delivery with a possible external submission cannot be submitted again.");
        Status = CustomerInvoiceElectronicDeliveryStatuses.Submitting;
        ClearFailure();
        Touch(nowUtc);
    }

    public void Accepted(string providerReference, string? providerState, DateTime nowUtc, DateTime nextReconcileUtc)
    {
        ProviderReference = Required(providerReference, 256);
        ProviderState = Optional(providerState, 64);
        Status = CustomerInvoiceElectronicDeliveryStatuses.Accepted;
        Outcome = "accepted";
        SubmittedUtc ??= NormalizeUtc(nowUtc);
        NextReconcileUtc = NormalizeUtc(nextReconcileUtc);
        ClearFailure();
        Touch(nowUtc);
    }

    public void Delivered(string? providerState, DateTime nowUtc)
    {
        ProviderState = Optional(providerState, 64) ?? ProviderState;
        Status = CustomerInvoiceElectronicDeliveryStatuses.Delivered;
        Outcome = "delivered";
        DeliveredUtc = NormalizeUtc(nowUtc);
        NextReconcileUtc = null;
        ClearFailure();
        Touch(nowUtc);
    }

    public void Reject(string code, string summary, string? providerState, DateTime nowUtc)
    {
        ProviderState = Optional(providerState, 64) ?? ProviderState;
        Status = CustomerInvoiceElectronicDeliveryStatuses.Rejected;
        Outcome = "rejected";
        FailureCode = Required(code, 100);
        FailureSummary = Required(summary, 1000);
        NextReconcileUtc = null;
        Touch(nowUtc);
    }

    public void Fail(string code, string summary, bool retryable, DateTime nowUtc)
    {
        Status = CustomerInvoiceElectronicDeliveryStatuses.Failed;
        Outcome = retryable ? "retryable_failure" : "validation_failed";
        FailureCode = Required(code, 100);
        FailureSummary = Required(summary, 1000);
        NextReconcileUtc = null;
        Touch(nowUtc);
    }

    public void RequireReconciliation(string code, string summary, DateTime nowUtc, DateTime nextReconcileUtc)
    {
        Status = CustomerInvoiceElectronicDeliveryStatuses.ReconciliationRequired;
        Outcome = "reconciliation_required";
        FailureCode = Required(code, 100);
        FailureSummary = Required(summary, 1000);
        NextReconcileUtc = NormalizeUtc(nextReconcileUtc);
        Touch(nowUtc);
    }

    public void StartReconciliation(DateTime nowUtc)
    {
        ReconciliationAttempts++;
        NextReconcileUtc = null;
        Touch(nowUtc);
    }

    public void ScheduleReconciliation(string? providerState, DateTime nowUtc, DateTime nextReconcileUtc)
    {
        ProviderState = Optional(providerState, 64) ?? ProviderState;
        Status = CustomerInvoiceElectronicDeliveryStatuses.Accepted;
        Outcome = "accepted";
        NextReconcileUtc = NormalizeUtc(nextReconcileUtc);
        ClearFailure();
        Touch(nowUtc);
    }

    public void RequestRetry(DateTime nowUtc)
    {
        if (ExternalSubmissionMayExist || Status != CustomerInvoiceElectronicDeliveryStatuses.Failed)
            throw new InvalidOperationException("This electronic delivery cannot be retried until its external outcome is proven safe.");
        Status = CustomerInvoiceElectronicDeliveryStatuses.Pending;
        Outcome = "queued";
        ClearFailure();
        Touch(nowUtc);
    }

    public void RecordFallback(Guid emailDeliveryId, DateTime nowUtc)
    {
        if (emailDeliveryId == Guid.Empty) throw new ArgumentException("Email delivery id is required.", nameof(emailDeliveryId));
        FallbackEmailDeliveryId = emailDeliveryId;
        Touch(nowUtc);
    }

    private void ClearFailure() { FailureCode = null; FailureSummary = null; }
    private void Touch(DateTime nowUtc) => UpdatedUtc = NormalizeUtc(nowUtc);
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
    private static string Required(string? value, int max)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > max
            ? throw new ArgumentException("An electronic delivery value is invalid.")
            : normalized;
    }
    private static string? Optional(string? value, int max)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.Length > max
            ? throw new ArgumentException("An electronic delivery value is invalid.") : normalized;
    }
}

public sealed class CustomerInvoiceElectronicDeliveryEvent : ICompanyOwnedEntity
{
    private CustomerInvoiceElectronicDeliveryEvent() { }
    public CustomerInvoiceElectronicDeliveryEvent(Guid id, Guid companyId, Guid deliveryId, string providerKey,
        string eventKey, string source, string outcome, string? providerState, string safeSummary,
        string evidenceHash, DateTime occurredUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company id is required.") : companyId;
        DeliveryId = deliveryId == Guid.Empty ? throw new ArgumentException("Delivery id is required.") : deliveryId;
        ProviderKey = Required(providerKey, 64).ToLowerInvariant();
        EventKey = Required(eventKey, 256);
        Source = Required(source, 32).ToLowerInvariant();
        Outcome = Required(outcome, 64).ToLowerInvariant();
        ProviderState = Optional(providerState, 64);
        SafeSummary = Required(safeSummary, 1000);
        EvidenceHash = Required(evidenceHash, 64);
        OccurredUtc = occurredUtc.Kind == DateTimeKind.Utc ? occurredUtc : occurredUtc.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string EventKey { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? ProviderState { get; private set; }
    public string SafeSummary { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    private static string Required(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException("An electronic delivery event value is invalid.") : value.Trim();
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, max);
}
