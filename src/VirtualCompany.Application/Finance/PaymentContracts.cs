namespace VirtualCompany.Application.Finance;

public sealed record GetFinancePaymentsQuery(
    Guid CompanyId,
    string? PaymentType = null,
    int Limit = 100,
    string SourceFilter = FinanceDataSources.All);

public sealed record GetFinancePaymentDetailQuery(
    Guid CompanyId,
    Guid PaymentId);

public sealed record GetFinancePaymentAllocationsByPaymentQuery(
    Guid CompanyId,
    Guid PaymentId);

public sealed record GetFinanceInvoiceAllocationsQuery(Guid CompanyId, Guid InvoiceId);

public sealed record GetFinanceBillAllocationsQuery(
    Guid CompanyId,
    Guid BillId);

public sealed record GetFinancePaymentAllocationTraceQuery(
    Guid CompanyId,
    Guid AllocationId);

public sealed record CreateFinancePaymentDto(
    string PaymentType,
    decimal Amount,
    string Currency,
    DateTime PaymentDate,
    string Method,
    string Status,
    string CounterpartyReference);

public sealed record CreateFinancePaymentCommand(
    Guid CompanyId,
    CreateFinancePaymentDto Payment);

public sealed record CreateFinancePaymentAllocationDto(
    Guid PaymentId,
    Guid? InvoiceId,
    Guid? BillId,
    decimal AllocatedAmount,
    string Currency);

public sealed record UpdateFinancePaymentAllocationDto(
    Guid PaymentId,
    Guid? InvoiceId,
    Guid? BillId,
    decimal AllocatedAmount,
    string Currency);

public sealed record CreateFinancePaymentAllocationCommand(
    Guid CompanyId,
    CreateFinancePaymentAllocationDto Allocation);

public sealed record UpdateFinancePaymentAllocationCommand(
    Guid CompanyId,
    Guid AllocationId,
    UpdateFinancePaymentAllocationDto Allocation);

public sealed record DeleteFinancePaymentAllocationCommand(Guid CompanyId, Guid AllocationId);
public sealed record BackfillFinancePaymentAllocationsCommand(Guid CompanyId, bool SynthesizeMissingPayments = true);

public sealed record FinancePaymentDto(
    Guid Id,
    Guid CompanyId,
    string PaymentType,
    decimal Amount,
    string Currency,
    DateTime PaymentDate,
    string Method,
    string Status,
    string CounterpartyReference,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights,
    string Source = FinanceDataSources.Simulation);

public sealed record FinancePaymentAllocationDto(
    Guid Id,
    Guid CompanyId,
    Guid PaymentId,
    Guid? InvoiceId,
    Guid? BillId,
    decimal AllocatedAmount,
    string Currency,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    Guid? SourceSimulationEventRecordId,
    Guid? PaymentSourceSimulationEventRecordId,
    Guid? TargetSourceSimulationEventRecordId);

public sealed record FinanceSimulationEventReferenceDto(
    Guid Id,
    string EventType,
    string SourceEntityType,
    Guid? SourceEntityId,
    string? SourceReference,
    Guid? ParentEventId,
    DateTime SimulationDateUtc,
    decimal? CashBefore,
    decimal? CashDelta,
    decimal? CashAfter);

public sealed record FinanceAllocationTargetDocumentDto(
    string TargetDocumentType,
    Guid Id,
    string Reference,
    decimal Amount,
    string Currency,
    string Status,
    Guid? SourceSimulationEventRecordId);

public sealed record FinancePaymentAllocationTraceDto(
    Guid AllocationId,
    Guid CompanyId,
    FinancePaymentDto Payment,
    FinanceAllocationTargetDocumentDto TargetDocument,
    FinanceSimulationEventReferenceDto? PaymentSourceEvent,
    FinanceSimulationEventReferenceDto? TargetSourceEvent,
    FinanceSimulationEventReferenceDto? OriginatingSourceEvent);

public sealed record FinancePaymentAllocationBackfillResultDto(
    Guid CompanyId,
    int CreatedAllocationCount,
    int CreatedPaymentCount,
    int RecalculatedInvoiceCount,
    int RecalculatedBillCount);

public interface IFinancePaymentReadService
{
    Task<IReadOnlyList<FinancePaymentDto>> GetPaymentsAsync(GetFinancePaymentsQuery query, CancellationToken cancellationToken);
    Task<FinancePaymentDto?> GetPaymentDetailAsync(GetFinancePaymentDetailQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByPaymentAsync(GetFinancePaymentAllocationsByPaymentQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByInvoiceAsync(GetFinanceInvoiceAllocationsQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByBillAsync(GetFinanceBillAllocationsQuery query, CancellationToken cancellationToken);
    Task<FinancePaymentAllocationTraceDto?> GetAllocationTraceAsync(GetFinancePaymentAllocationTraceQuery query, CancellationToken cancellationToken);
}

public interface IFinancePaymentCommandService
{
    Task<FinancePaymentDto> CreatePaymentAsync(CreateFinancePaymentCommand command, CancellationToken cancellationToken);
    Task<FinancePaymentAllocationDto> CreateAllocationAsync(CreateFinancePaymentAllocationCommand command, CancellationToken cancellationToken);
    Task<FinancePaymentAllocationDto> UpdateAllocationAsync(UpdateFinancePaymentAllocationCommand command, CancellationToken cancellationToken);
    Task DeleteAllocationAsync(DeleteFinancePaymentAllocationCommand command, CancellationToken cancellationToken);
    Task<FinancePaymentAllocationBackfillResultDto> BackfillAllocationsAsync(BackfillFinancePaymentAllocationsCommand command, CancellationToken cancellationToken);
}