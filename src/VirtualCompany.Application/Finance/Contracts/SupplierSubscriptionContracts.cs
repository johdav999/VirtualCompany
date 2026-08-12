namespace VirtualCompany.Application.Finance;

public sealed record GetSupplierSubscriptionsQuery(Guid CompanyId, string? Status = null, string? Search = null);
public sealed record GetSupplierSubscriptionQuery(Guid CompanyId, Guid SubscriptionId);
public sealed record GetSupplierBillSubscriptionContextQuery(Guid CompanyId, Guid BillId);

public sealed record CreateSupplierSubscriptionCommand(
    Guid CompanyId,
    Guid CounterpartyId,
    string Name,
    string Currency,
    decimal ExpectedAmount,
    string Cadence,
    int BillingDay,
    DateTime StartDateUtc,
    DateTime NextExpectedBillDateUtc,
    decimal AmountTolerance,
    int DateToleranceDays,
    DateTime? EndDateUtc,
    string? ContractReference,
    string? Description,
    int NoticePeriodDays,
    bool AutoRenews,
    Guid? ContractDocumentId,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record UpdateSupplierSubscriptionCommand(
    Guid CompanyId,
    Guid SubscriptionId,
    Guid CounterpartyId,
    string Name,
    string Currency,
    decimal ExpectedAmount,
    string Cadence,
    int BillingDay,
    DateTime StartDateUtc,
    DateTime NextExpectedBillDateUtc,
    decimal AmountTolerance,
    int DateToleranceDays,
    DateTime? EndDateUtc,
    string? ContractReference,
    string? Description,
    int NoticePeriodDays,
    bool AutoRenews,
    Guid? ContractDocumentId,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record ChangeSupplierSubscriptionStatusCommand(
    Guid CompanyId,
    Guid SubscriptionId,
    string Action,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record EvaluateSupplierSubscriptionBillCommand(Guid CompanyId, Guid BillId, Guid? ActorUserId, string ActorDisplayName);
public sealed record DecideSupplierSubscriptionMatchCommand(Guid CompanyId, Guid MatchId, bool Confirm, Guid? ActorUserId, string ActorDisplayName);
public sealed record LinkSupplierSubscriptionReceiptEvidenceCommand(Guid CompanyId, Guid SubscriptionId, Guid BillId, string EvidenceSummary, Guid? ActorUserId, string ActorDisplayName);

public sealed record SupplierSubscriptionSummaryDto(
    Guid Id,
    Guid CounterpartyId,
    string SupplierName,
    string Name,
    string Currency,
    decimal ExpectedAmount,
    string Cadence,
    string Status,
    string Health,
    string HealthMessage,
    DateTime NextExpectedBillDateUtc,
    DateTime? EndDateUtc,
    DateTime? LastMatchedBillUtc,
    int MatchCount,
    int ReviewCount);

public sealed record SupplierSubscriptionMatchDto(
    Guid Id,
    Guid SubscriptionId,
    Guid BillId,
    string BillNumber,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime ExpectedBillDateUtc,
    decimal ExpectedAmount,
    decimal ActualAmount,
    decimal AmountVariance,
    string Currency,
    string Status,
    string MatchMethod,
    int ConfidenceScore,
    string EvidenceSummary,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    DateTime CreatedUtc);

public sealed record SupplierSubscriptionSourceEvidenceDto(
    Guid ProposalId,
    string Status,
    string? SourceSubject,
    string? SourceAttachmentName,
    string EvidenceSummary,
    string? DecisionReason,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    DateTime CreatedUtc);
public sealed record SupplierSubscriptionDetailDto(
    Guid Id,
    Guid CounterpartyId,
    string SupplierName,
    string Name,
    string? ContractReference,
    string? Description,
    string Currency,
    decimal ExpectedAmount,
    decimal AmountTolerance,
    string Cadence,
    int BillingDay,
    DateTime StartDateUtc,
    DateTime? EndDateUtc,
    DateTime NextExpectedBillDateUtc,
    int DateToleranceDays,
    int NoticePeriodDays,
    bool AutoRenews,
    string Status,
    string Health,
    string HealthMessage,
    Guid? ContractDocumentId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    SupplierSubscriptionSourceEvidenceDto? SourceEvidence,
    IReadOnlyList<SupplierSubscriptionMatchDto> Matches);

public sealed record SupplierBillSubscriptionContextDto(
    Guid BillId,
    bool HasContext,
    SupplierSubscriptionSummaryDto? Subscription,
    SupplierSubscriptionMatchDto? Match,
    IReadOnlyList<SupplierSubscriptionMatchDto> Suggestions,
    string Status,
    string Message);

public interface ISupplierSubscriptionService
{
    Task<IReadOnlyList<SupplierSubscriptionSummaryDto>> GetAsync(GetSupplierSubscriptionsQuery query, CancellationToken cancellationToken);
    Task<SupplierSubscriptionDetailDto?> GetAsync(GetSupplierSubscriptionQuery query, CancellationToken cancellationToken);
    Task<SupplierBillSubscriptionContextDto> GetBillContextAsync(GetSupplierBillSubscriptionContextQuery query, CancellationToken cancellationToken);
    Task<SupplierSubscriptionDetailDto> CreateAsync(CreateSupplierSubscriptionCommand command, CancellationToken cancellationToken);
    Task<SupplierSubscriptionDetailDto> UpdateAsync(UpdateSupplierSubscriptionCommand command, CancellationToken cancellationToken);
    Task<SupplierSubscriptionDetailDto> ChangeStatusAsync(ChangeSupplierSubscriptionStatusCommand command, CancellationToken cancellationToken);
    Task<SupplierBillSubscriptionContextDto> EvaluateBillAsync(EvaluateSupplierSubscriptionBillCommand command, CancellationToken cancellationToken);
    Task<SupplierBillSubscriptionContextDto> DecideMatchAsync(DecideSupplierSubscriptionMatchCommand command, CancellationToken cancellationToken);
    Task<SupplierBillSubscriptionContextDto> LinkReceiptEvidenceAsync(LinkSupplierSubscriptionReceiptEvidenceCommand command, CancellationToken cancellationToken);
}

public sealed record GetSupplierSubscriptionIntakeProposalsQuery(Guid CompanyId, string? Status = null, string? Search = null);
public sealed record GetSupplierSubscriptionIntakeProposalQuery(Guid CompanyId, Guid ProposalId);

public sealed record SupplierSubscriptionProposalTermsDto(
    Guid? CounterpartyId,
    string? Name,
    string? Currency,
    decimal? ExpectedAmount,
    string? Cadence,
    int? BillingDay,
    DateTime? StartDateUtc,
    DateTime? NextExpectedBillDateUtc,
    decimal? AmountTolerance,
    int? DateToleranceDays,
    DateTime? EndDateUtc,
    string? ContractReference,
    string? Description,
    int? NoticePeriodDays,
    bool? AutoRenews,
    Guid? ContractDocumentId);

public sealed record RecordSupplierSubscriptionIntakeProposalCommand(
    Guid CompanyId,
    Guid SourceEmailMessageSnapshotId,
    Guid? SourceEmailAttachmentSnapshotId,
    Guid? SourceDocumentId,
    string SourceFingerprint,
    string Classification,
    string Status,
    int ConfidenceScore,
    string EvidenceSummary,
    string? SupplierName,
    string? SupplierOrgNumber,
    SupplierSubscriptionProposalTermsDto Terms,
    string? SafeFailureSummary,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record AcceptSupplierSubscriptionIntakeProposalCommand(
    Guid CompanyId,
    Guid ProposalId,
    SupplierSubscriptionProposalTermsDto Terms,
    Guid? ActorUserId,
    string ActorDisplayName,
    string? DecisionReason = null);

public sealed record RejectSupplierSubscriptionIntakeProposalCommand(
    Guid CompanyId,
    Guid ProposalId,
    string Reason,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record RetrySupplierSubscriptionIntakeProposalCommand(
    Guid CompanyId,
    Guid ProposalId,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record SupplierSubscriptionIntakeProposalSummaryDto(
    Guid Id,
    string Status,
    string Classification,
    string SupplierName,
    string AgreementName,
    string? Currency,
    decimal? ExpectedAmount,
    string? Cadence,
    int ConfidenceScore,
    string EvidenceSummary,
    Guid? AcceptedSubscriptionId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record SupplierSubscriptionIntakeProposalDetailDto(
    Guid Id,
    string Status,
    string Classification,
    Guid SourceEmailMessageSnapshotId,
    Guid? SourceEmailAttachmentSnapshotId,
    Guid? SourceDocumentId,
    string SourceFingerprint,
    string? SourceSubject,
    string? SourceAttachmentName,
    string SupplierName,
    string? SupplierOrgNumber,
    SupplierSubscriptionProposalTermsDto Terms,
    int ConfidenceScore,
    string EvidenceSummary,
    string? SafeFailureSummary,
    Guid? AcceptedSubscriptionId,
    Guid? DecidedByUserId,
    string? DecisionReason,
    DateTime? DecidedUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public interface ISupplierSubscriptionIntakeProposalService
{
    Task<IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryDto>> GetAsync(GetSupplierSubscriptionIntakeProposalsQuery query, CancellationToken cancellationToken);
    Task<SupplierSubscriptionIntakeProposalDetailDto?> GetAsync(GetSupplierSubscriptionIntakeProposalQuery query, CancellationToken cancellationToken);
    Task<SupplierSubscriptionIntakeProposalDetailDto> RecordAsync(RecordSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken);
    Task<SupplierSubscriptionDetailDto> AcceptAsync(AcceptSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken);
    Task<SupplierSubscriptionIntakeProposalDetailDto> RejectAsync(RejectSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken);
    Task<SupplierSubscriptionIntakeProposalDetailDto> RetryAsync(RetrySupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken);
}

public sealed record ClassifySupplierSubscriptionSourceCommand(
    Guid CompanyId,
    Guid SourceEmailMessageSnapshotId,
    Guid? ActorUserId,
    string ActorDisplayName);

public sealed record SupplierSubscriptionSourceClassificationResultDto(
    Guid CompanyId,
    Guid SourceEmailMessageSnapshotId,
    int ProposalCount,
    int ReceiptEvidenceCount,
    IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryDto> Proposals);

public interface ISupplierSubscriptionDocumentClassifier
{
    Task<SupplierSubscriptionSourceClassificationResultDto> ClassifyAsync(ClassifySupplierSubscriptionSourceCommand command, CancellationToken cancellationToken);
}
