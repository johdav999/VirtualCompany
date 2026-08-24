using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceBillsQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record GetFinanceBillDetailQuery(
    Guid CompanyId,
    Guid BillId,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record FinancePayablePressureInsightDto(
    decimal OverdueAmount,
    decimal DueSoonAmount,
    int OverdueBillCount,
    int DueSoonBillCount,
    decimal? UpcomingBurdenRatioOfCash,
    string RiskLabel,
    string Summary,
    IReadOnlyList<FinancePayablePressureItemDto> Suppliers);

public sealed record FinancePayablePressureItemDto(
    Guid CounterpartyId,
    string SupplierName,
    decimal DueAmount,
    int DueBillCount,
    bool HasOverdueBalance,
    int MaxUrgencyDays,
    string RiskLabel,
    string Summary);

public sealed record FinanceDueSoonBillRecommendationDto(
    int Rank,
    Guid BillId,
    string BillNumber,
    string SupplierName,
    DateTime DueUtc,
    decimal OutstandingAmount,
    string Currency,
    int DaysUntilDue,
    string CashImpact,
    string Severity,
    string RecommendationCode,
    string RecommendationText,
    int UrgencyScore = 0,
    string RecommendationAction = "",
    string RecommendationSeverity = "",
    string CashImpactRationale = "",
    string VendorCriticality = "standard",
    string VendorCriticalityReason = "",
    string CashPressure = "low",
    string CashPressureReason = "",
    int DueDateFactor = 0,
    int AmountFactor = 0,
    int VendorCriticalityFactor = 0,
    int CashPressureFactor = 0,
    string ScoringFactors = "");

public sealed record FinanceOpenPayableItemDto(
    Guid BillId,
    string BillNumber,
    string SupplierName,
    DateTime DueUtc,
    decimal OutstandingAmount,
    string Currency,
    string Status,
    Guid? SupplierId = null);

public sealed record FinanceBillDto(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string BillNumber,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceLinkedDocumentDto? LinkedDocument,
    string Source = FinanceDataSources.Simulation,
    string PostingStatus = FinanceDocumentPostingStatuses.Booked,
    string SettlementStatus = FinanceSettlementStatuses.Unpaid,
    string DueStatus = FinanceDocumentDueStatuses.NotDue,
    string DocumentKind = FinanceDocumentKinds.SupplierInvoice,
    string? ProviderStatus = null,
    string ProcessingStatus = FinanceDocumentProcessingStatuses.None,
    FinanceTransactionPaymentContextDto? PaymentContext = null,
    SupplierInvoicePaymentProposalDto? PaymentProposal = null,
    SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment = null,
    SupplierInvoiceDraftActionDto? DraftAction = null,
    IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions = null,
    SupplierInvoiceEnrichmentActionDto? EnrichmentAction = null);

public sealed record FinanceBillDetailDto(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string BillNumber,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceActionPermissionsDto Permissions,
    FinanceLinkedDocumentAccessDto LinkedDocument,
    IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights,
    string PostingStatus = FinanceDocumentPostingStatuses.Booked,
    string SettlementStatus = FinanceSettlementStatuses.Unpaid,
    string DueStatus = FinanceDocumentDueStatuses.NotDue,
    string DocumentKind = FinanceDocumentKinds.SupplierInvoice,
    string? ProviderStatus = null,
    string ProcessingStatus = FinanceDocumentProcessingStatuses.None,
    FinanceTransactionPaymentContextDto? PaymentContext = null,
    IReadOnlyList<FinanceInvoiceRelatedTransactionDto>? RelatedTransactions = null,
    SupplierInvoicePaymentProposalDto? PaymentProposal = null,
    SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment = null,
    SupplierInvoiceDraftActionDto? DraftAction = null,
    IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions = null,
    SupplierInvoiceEnrichmentActionDto? EnrichmentAction = null,
    PaidSupplierBillExpenseAvailabilityDto? PaidExpensePostingAvailability = null,
    SupplierBillAccountingStateDto? Accounting = null,
    string Source = FinanceDataSources.Manual);

public sealed record PaidSupplierBillExpenseAvailabilityDto(
    bool CanPost,
    string StatusLabel,
    string StatusTone,
    string Message,
    string? AccountCode = null,
    IReadOnlyList<string>? BlockingReasons = null,
    IReadOnlyList<string>? ReasonCodes = null,
    bool RequiresApproval = false);

public sealed record SupplierInvoiceSourceDocumentAttachmentDto(
    Guid Id,
    Guid BillId,
    Guid? DocumentId,
    string Status,
    string? ProviderKey,
    Guid? ConnectionId,
    Guid? RequestedByUserId,
    DateTime? RequestedUtc,
    DateTime? AttachedUtc,
    string? ResponseSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record RequestSupplierInvoiceSourceDocumentAttachmentCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public interface IFinanceSupplierInvoiceSourceDocumentAttachmentService
{
    Task<SupplierInvoiceSourceDocumentAttachmentDto> RequestAttachmentAsync(
        RequestSupplierInvoiceSourceDocumentAttachmentCommand command,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceDraftActionDto(
    Guid Id,
    Guid BillId,
    string Status,
    string? ProviderKey,
    Guid? ConnectionId,
    Guid? RequestedByUserId,
    DateTime? RequestedUtc,
    DateTime? UpdatedInProviderUtc,
    DateTime? BookedUtc,
    string? ResponseSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record UpdateSupplierInvoiceDraftCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record BookkeepSupplierInvoiceDraftCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox,
    bool AllowPaidBill = false);

public sealed record PostPaidSupplierBillExpenseCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string? ProviderKey = null);

public sealed record PaidSupplierBillExpensePostingDto(
    Guid BillId,
    Guid DraftActionId,
    string Status,
    bool Posted,
    string ProviderKey,
    Guid? ConnectionId,
    string Summary,
    DateTime? RequestedUtc,
    DateTime? BookedUtc,
    SupplierInvoiceDraftActionDto DraftAction);

public interface IFinanceSupplierInvoiceDraftActionService
{
    Task<SupplierInvoiceDraftActionDto> UpdateDraftAsync(
        UpdateSupplierInvoiceDraftCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceDraftActionDto> BookkeepAsync(
        BookkeepSupplierInvoiceDraftCommand command,
        CancellationToken cancellationToken);
}

public interface IPaidSupplierBillExpensePostingService
{
    Task<PaidSupplierBillExpensePostingDto> PostAsync(
        PostPaidSupplierBillExpenseCommand command,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceEnrichmentActionDto(
    Guid Id,
    Guid BillId,
    string Status,
    string? ProviderKey,
    Guid? ConnectionId,
    Guid? RequestedByUserId,
    Guid? ApprovedByUserId,
    Guid? TaskId,
    Guid? ApprovalRequestId,
    DateTime? RequestedUtc,
    DateTime? ApprovedUtc,
    DateTime? SyncedUtc,
    string? ResponseSummary,
    JsonObject SuggestionPayload,
    JsonArray ReconciliationWarnings,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record SuggestSupplierInvoiceEnrichmentCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record ReconcileSupplierInvoiceCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName);

public interface IFinanceSupplierInvoiceEnrichmentService
{
    Task<SupplierInvoiceEnrichmentActionDto> SuggestAsync(
        SuggestSupplierInvoiceEnrichmentCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceEnrichmentActionDto> SyncApprovedAsync(
        SyncSupplierInvoiceEnrichmentCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceEnrichmentActionDto> ReconcileAsync(
        ReconcileSupplierInvoiceCommand command,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceCorrectionActionDto(
    Guid Id,
    Guid BillId,
    string ActionType,
    string Status,
    string? ProviderKey,
    Guid? ConnectionId,
    Guid? RequestedByUserId,
    Guid? ApprovedByUserId,
    Guid? TaskId,
    Guid? ApprovalRequestId,
    DateTime? RequestedUtc,
    DateTime? ApprovedUtc,
    DateTime? CompletedUtc,
    Guid? CreditNoteBillId,
    string? ProviderCreditNoteNumber,
    string? ResponseSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record RequestSupplierInvoiceCancellationCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record RequestSupplierInvoiceCreditNoteCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Reason = "Correction",
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public interface IFinanceSupplierInvoiceCorrectionService
{
    Task<SupplierInvoiceCorrectionActionDto> RequestCancellationAsync(
        RequestSupplierInvoiceCancellationCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceCorrectionActionDto> RequestCreditNoteAsync(
        RequestSupplierInvoiceCreditNoteCommand command,
        CancellationToken cancellationToken);
}

