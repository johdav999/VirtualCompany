namespace VirtualCompany.Application.Finance;

public enum BillDocumentInputType
{
    Pdf,
    Docx,
    EmailBodyText,
    EmailBodyHtml
}

public enum BillExtractionConfidence
{
    Low,
    Medium,
    High
}

public enum BillFieldConfidence
{
    Low,
    Medium,
    High
}

public enum BillValidationStatus
{
    Pending,
    Valid,
    Flagged,
    Rejected
}

public enum BillValidationSeverity
{
    Warning,
    Rejection
}

public enum BillDuplicateCheckStatus
{
    NotDuplicate,
    Duplicate
}

public sealed record ExtractBillDocumentCommand(
    Guid CompanyId,
    BillDocumentInputType InputType,
    Stream Content,
    string SourceDocumentName,
    string? SourceEmailId = null,
    string? SourceAttachmentId = null,
    string? SourceDocumentId = null);

public sealed record DetectedBillCandidateCommand(
    Guid CompanyId,
    ExtractedDocumentText Document,
    string SourceDocumentName,
    string? SourceEmailId = null,
    string? SourceAttachmentId = null,
    string? SourceDocumentId = null);

public sealed record DocumentExtractionResult(
    Guid CompanyId,
    IReadOnlyList<NormalizedBillCandidateDto> Candidates);

public sealed record NormalizedBillCandidateDto(
    string? SupplierName,
    string? SupplierOrgNumber,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    string? Currency,
    decimal? TotalAmount,
    decimal? VatAmount,
    string? PaymentReference,
    string? Bankgiro,
    string? Plusgiro,
    string? Iban,
    string? Bic,
    BillExtractionConfidence Confidence,
    string? SourceEmailId,
    string? SourceAttachmentId,
    IReadOnlyList<BillFieldEvidenceDto> Evidence,
    IReadOnlyList<BillValidationFindingDto> ValidationFindings,
    BillValidationStatus ValidationStatus,
    BillDuplicateCheckDto DuplicateCheck,
    bool RequiresReview,
    bool IsEligibleForApprovalProposal,
    bool ValidationStatusPersisted);

public sealed record BillFieldEvidenceDto(
    string FieldName,
    string ExtractedValue,
    string SourceDocument,
    string SourceDocumentType,
    string PageOrSectionReference,
    string TextSpanOrLocator,
    string ExtractionMethod,
    BillFieldConfidence Confidence,
    string Snippet);

public sealed record BillValidationFindingDto(
    string Code,
    string Message,
    BillValidationSeverity Severity,
    string? FieldName = null);

public sealed record BillDuplicateCheckDto(
    Guid Id,
    BillDuplicateCheckStatus Status,
    IReadOnlyList<Guid> MatchedBillIds,
    string CriteriaSummary,
    DateTime CheckedUtc);

public sealed record ExtractedDocumentText(
    string SourceDocumentType,
    IReadOnlyList<ExtractedDocumentSection> Sections)
{
    public string FullText => string.Join("\n", Sections.Select(x => x.Text));
}

public sealed record ExtractedDocumentSection(
    string Reference,
    string Text,
    int StartOffset);

public sealed record BillDuplicateCheckRequest(
    Guid CompanyId,
    string? SupplierName,
    string? SupplierOrgNumber,
    string? InvoiceNumber,
    decimal? TotalAmount,
    string? Currency,
    string? SourceEmailId,
    string? SourceAttachmentId);

public sealed record BillDuplicateCheckResult(
    Guid Id,
    bool IsDuplicate,
    IReadOnlyList<Guid> MatchedBillIds,
    string CriteriaSummary,
    DateTime CheckedUtc);

public interface IDocumentExtractionService
{
    Task<DocumentExtractionResult> ExtractAsync(ExtractBillDocumentCommand command, CancellationToken cancellationToken);
}

public interface IBillInformationExtractor
{
    Task<NormalizedBillCandidateDto> ExtractAsync(
        DetectedBillCandidateCommand command,
        CancellationToken cancellationToken);
}

public interface IDocumentTextExtractor
{
    bool Supports(BillDocumentInputType inputType);
    Task<ExtractedDocumentText> ExtractAsync(
        Stream content,
        string sourceDocumentName,
        BillDocumentInputType inputType,
        CancellationToken cancellationToken);
}

public interface IBillDuplicateCheckRepository
{
    Task<BillDuplicateCheckResult> CheckAndPersistAsync(
        BillDuplicateCheckRequest request,
        CancellationToken cancellationToken);
}

public sealed record PersistNormalizedBillExtractionCommand(
    Guid CompanyId,
    NormalizedBillCandidateDto Candidate);

public interface IBillExtractionPersistenceRepository
{
    Task<Guid> PersistAsync(
        PersistNormalizedBillExtractionCommand command,
        CancellationToken cancellationToken);
}

public sealed class UnsupportedBillDocumentException : Exception
{
    public UnsupportedBillDocumentException(string message)
        : base(message)
    {
    }
}
