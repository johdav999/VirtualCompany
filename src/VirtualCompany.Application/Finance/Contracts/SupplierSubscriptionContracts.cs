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
}
