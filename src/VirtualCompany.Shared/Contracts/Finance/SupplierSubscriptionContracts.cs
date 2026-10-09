

namespace VirtualCompany.Application.Finance;


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


public sealed record SupplierBillSubscriptionContextDto(
    Guid BillId,
    bool HasContext,
    SupplierSubscriptionSummaryDto? Subscription,
    SupplierSubscriptionMatchDto? Match,
    IReadOnlyList<SupplierSubscriptionMatchDto> Suggestions,
    string Status,
    string Message);
