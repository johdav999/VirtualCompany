namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceBillInboxQuery(Guid CompanyId, int Limit = 100);

public sealed record GetFinanceBillInboxDetailQuery(Guid CompanyId, Guid BillId);

public sealed record ApproveFinanceBillCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record RejectFinanceBillCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record RequestFinanceBillClarificationCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record FinanceBillInboxRowDto(
    Guid Id,
    string SupplierName,
    string BillReference,
    decimal? Amount,
    string? Currency,
    DateTime DetectedUtc,
    string Status,
    string ConfidenceLevel,
    int ValidationWarningCount,
    int DuplicateWarningCount);

public sealed record FinanceBillInboxDetailDto(
    Guid Id,
    string SupplierName,
    string? SupplierOrgNumber,
    string BillReference,
    DateTime? BillDateUtc,
    DateTime? DueDateUtc,
    decimal? Amount,
    decimal? VatAmount,
    string? Currency,
    string Status,
    decimal? Confidence,
    string ConfidenceLevel,
    IReadOnlyList<FinanceBillExtractedFieldDto> ExtractedFields,
    IReadOnlyList<FinanceBillWarningDto> ValidationWarnings,
    IReadOnlyList<FinanceBillWarningDto> DuplicateWarnings,
    FinanceBillProposalSummaryDto ProposalSummary,
    IReadOnlyList<FinanceBillReviewActionDto> ActionHistory,
    bool CanApprove,
    string? ApprovalBlockedReason);

public sealed record FinanceBillExtractedFieldDto(
    string FieldName,
    string DisplayName,
    string? RawValue,
    string? NormalizedValue,
    decimal? Confidence,
    IReadOnlyList<FinanceBillEvidenceReferenceDto> EvidenceReferences);

public sealed record FinanceBillEvidenceReferenceDto(
    string SourceDocument,
    string? SourceDocumentType,
    string? PageReference,
    string? SectionReference,
    string? TextSpan,
    string? Locator,
    string? Snippet);

public sealed record FinanceBillWarningDto(
    string Code,
    string Severity,
    string Message,
    bool IsResolved);

public sealed record FinanceBillProposalSummaryDto(
    string Headline,
    string Summary,
    IReadOnlyList<string> RiskFlags,
    string ApprovalAsk,
    string RecommendedAction,
    bool ExplicitlyRequestsApproval,
    bool InitiatesPayment);

public sealed record FinanceBillReviewActionDto(
    Guid Id,
    string Action,
    string ActorDisplayName,
    Guid? ActorUserId,
    DateTime OccurredUtc,
    string PriorStatus,
    string NewStatus,
    string Rationale);

public sealed record FinanceBillReviewActionResultDto(
    Guid BillId,
    string PriorStatus,
    string NewStatus,
    DateTime OccurredUtc);

public interface IFinanceBillInboxService
{
    Task<IReadOnlyList<FinanceBillInboxRowDto>> GetInboxAsync(GetFinanceBillInboxQuery query, CancellationToken cancellationToken);
    Task<FinanceBillInboxDetailDto?> GetDetailAsync(GetFinanceBillInboxDetailQuery query, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> ApproveAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> RejectAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> RequestClarificationAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken);
}