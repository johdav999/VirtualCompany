using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class PaymentBeneficiaryProfile : ICompanyOwnedEntity
{
    private PaymentBeneficiaryProfile() { }

    public PaymentBeneficiaryProfile(Guid id, Guid companyId, string partyType, Guid partyId,
        string displayName, string rail, string destination, string maskedDestination, string currency,
        int version, string verificationEvidenceReference, string verificationEvidenceHash,
        Guid verifiedByUserId, DateTime verifiedUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        PartyType = PaymentBatchEntityValues.Text(partyType, nameof(partyType), 40).ToLowerInvariant();
        PartyId = PaymentBatchEntityValues.Required(partyId, nameof(partyId));
        DisplayName = PaymentBatchEntityValues.Text(displayName, nameof(displayName), 200);
        Rail = PaymentBatchEntityValues.Rail(rail); Destination = PaymentBatchEntityValues.Text(destination, nameof(destination), 200);
        MaskedDestination = PaymentBatchEntityValues.Text(maskedDestination, nameof(maskedDestination), 100);
        Currency = PaymentBatchEntityValues.Currency(currency); Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        VerificationEvidenceReference = PaymentBatchEntityValues.Text(verificationEvidenceReference, nameof(verificationEvidenceReference), 500);
        VerificationEvidenceHash = PaymentBatchEntityValues.Hash(verificationEvidenceHash, nameof(verificationEvidenceHash));
        VerifiedByUserId = PaymentBatchEntityValues.Required(verifiedByUserId, nameof(verifiedByUserId));
        VerifiedUtc = CreatedUtc = PaymentBatchEntityValues.Utc(verifiedUtc, nameof(verifiedUtc));
        Status = PaymentBeneficiaryVerificationStatuses.Verified; IsCurrent = true;
        RowVersion = PaymentBatchEntityValues.ConcurrencyToken();
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public string PartyType { get; private set; } = null!; public Guid PartyId { get; private set; }
    public string DisplayName { get; private set; } = null!; public string Rail { get; private set; } = null!;
    public string Destination { get; private set; } = null!; public string MaskedDestination { get; private set; } = null!;
    public string Currency { get; private set; } = null!; public int Version { get; private set; }
    public string Status { get; private set; } = null!; public bool IsCurrent { get; private set; }
    public string VerificationEvidenceReference { get; private set; } = null!;
    public string VerificationEvidenceHash { get; private set; } = null!;
    public Guid VerifiedByUserId { get; private set; } public DateTime VerifiedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime? SupersededUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Supersede(DateTime utcNow)
    {
        if (!IsCurrent) return;
        IsCurrent = false; Status = PaymentBeneficiaryVerificationStatuses.Superseded;
        SupersededUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        RowVersion = PaymentBatchEntityValues.ConcurrencyToken();
    }
}

public sealed class PaymentBatch : ICompanyOwnedEntity
{
    private PaymentBatch() { }

    public PaymentBatch(Guid id, Guid companyId, string reference, string name, DateOnly plannedExecutionDate,
        string createIdempotencyKey, string createPayloadHash, Guid actorUserId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        Reference = PaymentBatchEntityValues.Text(reference, nameof(reference), 64);
        Name = PaymentBatchEntityValues.Text(name, nameof(name), 200); PlannedExecutionDate = plannedExecutionDate;
        CreateIdempotencyKey = PaymentBatchEntityValues.Text(createIdempotencyKey, nameof(createIdempotencyKey), 200);
        CreatePayloadHash = PaymentBatchEntityValues.Hash(createPayloadHash, nameof(createPayloadHash));
        CreatedByUserId = UpdatedByUserId = PaymentBatchEntityValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
        Status = PaymentBatchStatuses.Draft; Version = 1;
        RowVersion = PaymentBatchEntityValues.ConcurrencyToken();
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public string Reference { get; private set; } = null!; public string Name { get; private set; } = null!;
    public DateOnly PlannedExecutionDate { get; private set; } public string Status { get; private set; } = null!;
    public long Version { get; private set; } public int InstructionSetVersion { get; private set; }
    public Guid? CurrentValidationResultId { get; private set; } public Guid? CurrentExportArtifactId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; } public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; } public Guid? SubmittedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; } public Guid? RejectedByUserId { get; private set; }
    public Guid? CancelledByUserId { get; private set; } public string? DecisionComment { get; private set; }
    public string CreateIdempotencyKey { get; private set; } = null!; public string CreatePayloadHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; } public DateTime? ApprovedUtc { get; private set; }
    public DateTime? RejectedUtc { get; private set; } public DateTime? CancelledUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<PaymentBatchObligationLink> Obligations { get; } = new List<PaymentBatchObligationLink>();

    public void MarkContentChanged(long expectedVersion, Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion); EnsureEditable();
        Status = PaymentBatchStatuses.Draft; CurrentValidationResultId = null; CurrentExportArtifactId = null;
        ApprovalRequestId = null; SubmittedByUserId = null; SubmittedUtc = null;
        Touch(actor, utcNow);
    }

    public void InvalidateEvidence(long expectedVersion, Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (Status is PaymentBatchStatuses.Rejected or PaymentBatchStatuses.Cancelled)
            throw new InvalidOperationException("Final rejected or cancelled payment evidence cannot be reopened.");
        Status = PaymentBatchStatuses.Draft; CurrentValidationResultId = null; CurrentExportArtifactId = null;
        ApprovalRequestId = null; SubmittedByUserId = null; SubmittedUtc = null;
        Touch(actor, utcNow);
    }

    public int BeginInstructionSet(long expectedVersion, Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion); EnsureEditable(); InstructionSetVersion++;
        Status = PaymentBatchStatuses.Draft; CurrentValidationResultId = null; CurrentExportArtifactId = null;
        ApprovalRequestId = null; SubmittedByUserId = null; SubmittedUtc = null;
        Touch(actor, utcNow); return InstructionSetVersion;
    }

    public void MarkValidated(long expectedVersion, Guid validationResultId, Guid exportArtifactId,
        Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion); EnsureEditable();
        CurrentValidationResultId = PaymentBatchEntityValues.Required(validationResultId, nameof(validationResultId));
        CurrentExportArtifactId = PaymentBatchEntityValues.Required(exportArtifactId, nameof(exportArtifactId));
        Status = PaymentBatchStatuses.Validated; Touch(actor, utcNow);
    }

    public void MarkValidationFailed(long expectedVersion, Guid validationResultId, Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion); EnsureEditable();
        CurrentValidationResultId = PaymentBatchEntityValues.Required(validationResultId, nameof(validationResultId));
        CurrentExportArtifactId = null; Status = PaymentBatchStatuses.Draft; Touch(actor, utcNow);
    }

    public void Submit(long expectedVersion, Guid approvalRequestId, Guid actor, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (Status != PaymentBatchStatuses.Validated || !CurrentValidationResultId.HasValue || !CurrentExportArtifactId.HasValue)
            throw new InvalidOperationException("Only a current validated batch can be submitted for approval.");
        ApprovalRequestId = PaymentBatchEntityValues.Required(approvalRequestId, nameof(approvalRequestId));
        SubmittedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor));
        SubmittedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); Status = PaymentBatchStatuses.AwaitingApproval;
        Touch(actor, utcNow);
    }

    public void Approve(long expectedVersion, Guid actor, string? comment, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (Status != PaymentBatchStatuses.AwaitingApproval) throw new InvalidOperationException("The payment batch is not awaiting approval.");
        ApprovedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); ApprovedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        DecisionComment = PaymentBatchEntityValues.Optional(comment, 2000); Status = PaymentBatchStatuses.Approved; Touch(actor, utcNow);
    }

    public void Reject(long expectedVersion, Guid actor, string comment, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (Status != PaymentBatchStatuses.AwaitingApproval) throw new InvalidOperationException("The payment batch is not awaiting approval.");
        RejectedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); RejectedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        DecisionComment = PaymentBatchEntityValues.Text(comment, nameof(comment), 2000); Status = PaymentBatchStatuses.Rejected; Touch(actor, utcNow);
    }

    public void Cancel(long expectedVersion, Guid actor, string reason, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (Status == PaymentBatchStatuses.Cancelled) return;
        CancelledByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); CancelledUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        DecisionComment = PaymentBatchEntityValues.Text(reason, nameof(reason), 2000); Status = PaymentBatchStatuses.Cancelled; Touch(actor, utcNow);
    }

    private void EnsureEditable()
    {
        if (Status is PaymentBatchStatuses.Approved or PaymentBatchStatuses.Rejected or PaymentBatchStatuses.Cancelled)
            throw new InvalidOperationException("Finalized payment batch instructions are immutable.");
    }
    private void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new InvalidOperationException("The payment batch changed after it was opened."); }
    private void Touch(Guid actor, DateTime utcNow)
    { UpdatedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); Version++; RowVersion = PaymentBatchEntityValues.ConcurrencyToken(); }
}

public sealed class PaymentBatchObligationLink : ICompanyOwnedEntity
{
    private PaymentBatchObligationLink() { }
    public PaymentBatchObligationLink(Guid id, Guid companyId, Guid batchId, string obligationType, Guid sourceId,
        string sourceReference, string sourceVersion, string sourceHash, decimal amount, string currency,
        DateOnly dueDate, string paymentReference, Guid actorUserId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        ObligationType = PaymentBatchObligationTypes.Normalize(obligationType);
        if (!PaymentBatchObligationTypes.IsSupported(ObligationType)) throw new ArgumentOutOfRangeException(nameof(obligationType));
        SourceId = PaymentBatchEntityValues.Required(sourceId, nameof(sourceId));
        SourceReference = PaymentBatchEntityValues.Text(sourceReference, nameof(sourceReference), 200);
        SourceVersion = PaymentBatchEntityValues.Text(sourceVersion, nameof(sourceVersion), 128);
        SourceHash = PaymentBatchEntityValues.Hash(sourceHash, nameof(sourceHash)); Amount = PaymentBatchEntityValues.Positive(amount, nameof(amount));
        Currency = PaymentBatchEntityValues.Currency(currency); DueDate = dueDate;
        PaymentReference = PaymentBatchEntityValues.Text(paymentReference, nameof(paymentReference), 200);
        AddedByUserId = PaymentBatchEntityValues.Required(actorUserId, nameof(actorUserId)); CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public string ObligationType { get; private set; } = null!; public Guid SourceId { get; private set; }
    public string SourceReference { get; private set; } = null!; public string SourceVersion { get; private set; } = null!;
    public string SourceHash { get; private set; } = null!; public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!; public DateOnly DueDate { get; private set; }
    public string PaymentReference { get; private set; } = null!; public Guid AddedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public Guid? RemovedByUserId { get; private set; }
    public DateTime? RemovedUtc { get; private set; } public bool IsActive => !RemovedUtc.HasValue;
    public PaymentBatch Batch { get; private set; } = null!; public PaymentBeneficiarySnapshot BeneficiarySnapshot { get; private set; } = null!;
    public void Remove(Guid actor, DateTime utcNow)
    { if (RemovedUtc.HasValue) return; RemovedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); RemovedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
}

public sealed class PaymentBeneficiarySnapshot : ICompanyOwnedEntity
{
    private PaymentBeneficiarySnapshot() { }
    public PaymentBeneficiarySnapshot(Guid id, Guid companyId, Guid obligationLinkId, Guid? profileId,
        int profileVersion, string displayName, string rail, string destination, string maskedDestination,
        string verificationEvidenceReference, string verificationEvidenceHash, DateTime verifiedUtc, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ObligationLinkId = PaymentBatchEntityValues.Required(obligationLinkId, nameof(obligationLinkId));
        ProfileId = profileId == Guid.Empty ? null : profileId; ProfileVersion = profileVersion > 0 ? profileVersion : throw new ArgumentOutOfRangeException(nameof(profileVersion));
        DisplayName = PaymentBatchEntityValues.Text(displayName, nameof(displayName), 200); Rail = PaymentBatchEntityValues.Rail(rail);
        Destination = PaymentBatchEntityValues.Text(destination, nameof(destination), 200); MaskedDestination = PaymentBatchEntityValues.Text(maskedDestination, nameof(maskedDestination), 100);
        VerificationEvidenceReference = PaymentBatchEntityValues.Text(verificationEvidenceReference, nameof(verificationEvidenceReference), 500);
        VerificationEvidenceHash = PaymentBatchEntityValues.Hash(verificationEvidenceHash, nameof(verificationEvidenceHash));
        VerifiedUtc = PaymentBatchEntityValues.Utc(verifiedUtc, nameof(verifiedUtc)); CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ObligationLinkId { get; private set; }
    public Guid? ProfileId { get; private set; } public int ProfileVersion { get; private set; }
    public string DisplayName { get; private set; } = null!; public string Rail { get; private set; } = null!;
    public string Destination { get; private set; } = null!; public string MaskedDestination { get; private set; } = null!;
    public string VerificationEvidenceReference { get; private set; } = null!; public string VerificationEvidenceHash { get; private set; } = null!;
    public DateTime VerifiedUtc { get; private set; } public DateTime CreatedUtc { get; private set; }
    public PaymentBatchObligationLink ObligationLink { get; private set; } = null!;
}

public sealed class PaymentInstruction : ICompanyOwnedEntity
{
    private PaymentInstruction() { }
    public PaymentInstruction(Guid id, Guid companyId, Guid batchId, Guid obligationLinkId, int instructionSetVersion,
        int sequence, DateOnly executionDate, decimal amount, string currency, string paymentReference,
        string beneficiaryName, string rail, string destination, string maskedDestination,
        string sourceVersion, string sourceHash, string contentHash, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId)); ObligationLinkId = PaymentBatchEntityValues.Required(obligationLinkId, nameof(obligationLinkId));
        InstructionSetVersion = instructionSetVersion > 0 ? instructionSetVersion : throw new ArgumentOutOfRangeException(nameof(instructionSetVersion));
        Sequence = sequence > 0 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence)); ExecutionDate = executionDate;
        Amount = PaymentBatchEntityValues.Positive(amount, nameof(amount)); Currency = PaymentBatchEntityValues.Currency(currency);
        PaymentReference = PaymentBatchEntityValues.Text(paymentReference, nameof(paymentReference), 200);
        BeneficiaryName = PaymentBatchEntityValues.Text(beneficiaryName, nameof(beneficiaryName), 200); Rail = PaymentBatchEntityValues.Rail(rail);
        Destination = PaymentBatchEntityValues.Text(destination, nameof(destination), 200); MaskedDestination = PaymentBatchEntityValues.Text(maskedDestination, nameof(maskedDestination), 100);
        SourceVersion = PaymentBatchEntityValues.Text(sourceVersion, nameof(sourceVersion), 128); SourceHash = PaymentBatchEntityValues.Hash(sourceHash, nameof(sourceHash));
        ContentHash = PaymentBatchEntityValues.Hash(contentHash, nameof(contentHash)); CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
        Status = PaymentInstructionStatuses.Draft; IsCurrent = true;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public Guid ObligationLinkId { get; private set; } public int InstructionSetVersion { get; private set; } public int Sequence { get; private set; }
    public DateOnly ExecutionDate { get; private set; } public decimal Amount { get; private set; } public string Currency { get; private set; } = null!;
    public string PaymentReference { get; private set; } = null!; public string BeneficiaryName { get; private set; } = null!;
    public string Rail { get; private set; } = null!; public string Destination { get; private set; } = null!;
    public string MaskedDestination { get; private set; } = null!; public string SourceVersion { get; private set; } = null!;
    public string SourceHash { get; private set; } = null!; public string ContentHash { get; private set; } = null!;
    public string Status { get; private set; } = null!; public bool IsCurrent { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; } public DateTime? SupersededUtc { get; private set; }
    public void Approve(DateTime utcNow) { if (!IsCurrent) throw new InvalidOperationException("Only current instructions can be approved."); Status = PaymentInstructionStatuses.Approved; ApprovedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
    public void Supersede(DateTime utcNow) { if (!IsCurrent) return; IsCurrent = false; Status = PaymentInstructionStatuses.Superseded; SupersededUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
}

public sealed class PaymentBatchValidationResult : ICompanyOwnedEntity
{
    private PaymentBatchValidationResult() { }
    public PaymentBatchValidationResult(Guid id, Guid companyId, Guid batchId, long evaluatedBatchVersion,
        int instructionSetVersion, bool isValid, string sourceSetHash, string totalsJson, string cashAvailabilityJson,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId)); BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        EvaluatedBatchVersion = evaluatedBatchVersion; InstructionSetVersion = instructionSetVersion;
        IsValid = isValid; SourceSetHash = PaymentBatchEntityValues.Hash(sourceSetHash, nameof(sourceSetHash));
        TotalsJson = PaymentBatchEntityValues.Text(totalsJson, nameof(totalsJson), 8000); CashAvailabilityJson = PaymentBatchEntityValues.Text(cashAvailabilityJson, nameof(cashAvailabilityJson), 8000);
        ValidatedByUserId = PaymentBatchEntityValues.Required(actorUserId, nameof(actorUserId)); CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public long EvaluatedBatchVersion { get; private set; } public int InstructionSetVersion { get; private set; }
    public bool IsValid { get; private set; } public string SourceSetHash { get; private set; } = null!;
    public string TotalsJson { get; private set; } = null!; public string CashAvailabilityJson { get; private set; } = null!;
    public Guid ValidatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public ICollection<PaymentBatchValidationIssue> Issues { get; } = new List<PaymentBatchValidationIssue>();
}

public sealed class PaymentBatchValidationIssue : ICompanyOwnedEntity
{
    private PaymentBatchValidationIssue() { }
    public PaymentBatchValidationIssue(Guid id, Guid companyId, Guid validationResultId, Guid? obligationLinkId,
        string severity, string reasonCode, string explanation, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId)); ValidationResultId = PaymentBatchEntityValues.Required(validationResultId, nameof(validationResultId));
        ObligationLinkId = obligationLinkId == Guid.Empty ? null : obligationLinkId; Severity = PaymentBatchEntityValues.Text(severity, nameof(severity), 20);
        ReasonCode = PaymentBatchEntityValues.Text(reasonCode, nameof(reasonCode), 100); Explanation = PaymentBatchEntityValues.Text(explanation, nameof(explanation), 1000);
        CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ValidationResultId { get; private set; }
    public Guid? ObligationLinkId { get; private set; } public string Severity { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!; public string Explanation { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public PaymentBatchValidationResult ValidationResult { get; private set; } = null!;
}

public sealed class PaymentBatchExportArtifact : ICompanyOwnedEntity
{
    private PaymentBatchExportArtifact() { }
    public PaymentBatchExportArtifact(Guid id, Guid companyId, Guid batchId, int instructionSetVersion,
        string format, string mimeType, string content, string contentHash, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId)); BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        InstructionSetVersion = instructionSetVersion > 0 ? instructionSetVersion : throw new ArgumentOutOfRangeException(nameof(instructionSetVersion));
        Format = PaymentBatchEntityValues.Text(format, nameof(format), 100); MimeType = PaymentBatchEntityValues.Text(mimeType, nameof(mimeType), 100);
        Content = PaymentBatchEntityValues.Text(content, nameof(content), 200_000); ContentHash = PaymentBatchEntityValues.Hash(contentHash, nameof(contentHash));
        CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc)); IsCurrent = true;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public int InstructionSetVersion { get; private set; } public string Format { get; private set; } = null!; public string MimeType { get; private set; } = null!;
    public string Content { get; private set; } = null!; public string ContentHash { get; private set; } = null!; public bool IsCurrent { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime? SupersededUtc { get; private set; }
    public void Supersede(DateTime utcNow) { if (!IsCurrent) return; IsCurrent = false; SupersededUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
}

public sealed class PaymentBatchApprovalBinding : ICompanyOwnedEntity
{
    private PaymentBatchApprovalBinding() { }
    public PaymentBatchApprovalBinding(Guid id, Guid companyId, Guid batchId, Guid approvalRequestId,
        int instructionSetVersion, string sourceSetHash, Guid requestedByUserId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId)); BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        ApprovalRequestId = PaymentBatchEntityValues.Required(approvalRequestId, nameof(approvalRequestId)); InstructionSetVersion = instructionSetVersion;
        SourceSetHash = PaymentBatchEntityValues.Hash(sourceSetHash, nameof(sourceSetHash)); RequestedByUserId = PaymentBatchEntityValues.Required(requestedByUserId, nameof(requestedByUserId));
        CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc)); Status = PaymentBatchApprovalBindingStatuses.Pending;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public Guid ApprovalRequestId { get; private set; } public int InstructionSetVersion { get; private set; }
    public string SourceSetHash { get; private set; } = null!; public string Status { get; private set; } = null!;
    public Guid RequestedByUserId { get; private set; } public Guid? DecidedByUserId { get; private set; }
    public string? DecisionComment { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime? DecidedUtc { get; private set; }
    public ApprovalRequest ApprovalRequest { get; private set; } = null!;
    public void MarkApproved(Guid actor, string? comment, DateTime utcNow) => Decide(PaymentBatchApprovalBindingStatuses.Approved, actor, comment, utcNow);
    public void MarkRejected(Guid actor, string? comment, DateTime utcNow) => Decide(PaymentBatchApprovalBindingStatuses.Rejected, actor, comment, utcNow);
    public void MarkCancelled(Guid actor, string? comment, DateTime utcNow) => Decide(PaymentBatchApprovalBindingStatuses.Cancelled, actor, comment, utcNow);
    public void MarkStale(Guid actor, string? comment, DateTime utcNow) => Decide(PaymentBatchApprovalBindingStatuses.Stale, actor, comment, utcNow);
    private void Decide(string status, Guid actor, string? comment, DateTime utcNow)
    { Status = status; DecidedByUserId = PaymentBatchEntityValues.Required(actor, nameof(actor)); DecisionComment = PaymentBatchEntityValues.Optional(comment, 2000); DecidedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
}

public sealed class PaymentBatchOperation : ICompanyOwnedEntity
{
    private PaymentBatchOperation() { }
    public PaymentBatchOperation(Guid id, Guid companyId, Guid batchId, string operationType,
        string idempotencyKey, string requestHash, long resultBatchVersion, string resultStatus,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId)); BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        OperationType = PaymentBatchEntityValues.Text(operationType, nameof(operationType), 40); IdempotencyKey = PaymentBatchEntityValues.Text(idempotencyKey, nameof(idempotencyKey), 200);
        RequestHash = PaymentBatchEntityValues.Hash(requestHash, nameof(requestHash)); ResultBatchVersion = resultBatchVersion;
        ResultStatus = PaymentBatchEntityValues.Text(resultStatus, nameof(resultStatus), 32); ActorUserId = PaymentBatchEntityValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid BatchId { get; private set; }
    public string OperationType { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!; public long ResultBatchVersion { get; private set; }
    public string ResultStatus { get; private set; } = null!; public Guid ActorUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
}

internal static class PaymentBatchEntityValues
{
    public static Guid Id(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;
    public static byte[] ConcurrencyToken() => Guid.NewGuid().ToByteArray();
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Text(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    public static string Currency(string? value) { var result = Text(value, nameof(value), 3).ToUpperInvariant(); return result.Length == 3 && result.All(char.IsLetter) ? result : throw new ArgumentOutOfRangeException(nameof(value)); }
    public static string Rail(string? value) { var result = PaymentRails.Normalize(value); return PaymentRails.IsSupported(result) ? result : throw new ArgumentOutOfRangeException(nameof(value)); }
    public static string Hash(string? value, string name) { var result = Text(value, name, 64).ToLowerInvariant(); return result.Length == 64 && result.All(Uri.IsHexDigit) ? result : throw new ArgumentOutOfRangeException(name); }
    public static decimal Positive(decimal value, string name) { var result = decimal.Round(value, 2, MidpointRounding.AwayFromZero); return result > 0 ? result : throw new ArgumentOutOfRangeException(name); }
    public static DateTime Utc(DateTime value, string name) => EntityTimestampNormalizer.NormalizeUtc(value, name);
}
