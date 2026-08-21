using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record SupplierInvoicePaymentExportProviderRequest(
    Guid CompanyId,
    Guid ProposalId,
    Guid BillId,
    string SourceBillNumber,
    Guid SupplierId,
    string SupplierName,
    decimal Amount,
    string Currency,
    DateTime DueUtc,
    string PaymentReference,
    Guid ConnectionId,
    Guid? ActorUserId,
    string ExportMode = "register_payment",
    string? ExistingProviderPaymentNumber = null,
    bool BookkeepExistingProviderPayment = false);

public sealed record SupplierInvoicePaymentExportProviderResult(
    string ProviderKey,
    Guid? ConnectionId,
    string ExportMode,
    string ExportStatus,
    string ResponseSummary,
    JsonObject ProviderMetadata);

public interface ISupplierInvoicePaymentExportProvider
{
    string ProviderKey { get; }

    Task<SupplierInvoicePaymentExportProviderResult> ExportAsync(
        SupplierInvoicePaymentExportProviderRequest request,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceSourceDocumentAttachmentProviderRequest(
    Guid CompanyId,
    Guid AttachmentId,
    Guid BillId,
    Guid DocumentId,
    string SourceBillNumber,
    Guid ConnectionId,
    Guid? ActorUserId,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    Stream Content);

public sealed record SupplierInvoiceSourceDocumentAttachmentProviderResult(
    string ProviderKey,
    Guid? ConnectionId,
    string Status,
    string ResponseSummary,
    JsonObject ProviderMetadata);

public interface ISupplierInvoiceSourceDocumentAttachmentProvider
{
    string ProviderKey { get; }

    Task<SupplierInvoiceSourceDocumentAttachmentProviderResult> AttachAsync(
        SupplierInvoiceSourceDocumentAttachmentProviderRequest request,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceDraftActionProviderRequest(
    Guid CompanyId,
    Guid ActionId,
    Guid BillId,
    string SourceBillNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierNumber,
    decimal Amount,
    string Currency,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    string InvoiceNumber,
    string? PaymentReference,
    decimal? VatAmount,
    string? AccountCode,
    string? CostCenter,
    string? Project,
    Guid ConnectionId,
    Guid? ActorUserId);

public sealed record SupplierInvoiceDraftActionProviderResult(
    string ProviderKey,
    Guid? ConnectionId,
    string Status,
    string ResponseSummary,
    JsonObject ProviderMetadata);

public interface ISupplierInvoiceDraftActionProvider
{
    string ProviderKey { get; }

    Task<SupplierInvoiceDraftActionProviderResult> UpdateDraftAsync(
        SupplierInvoiceDraftActionProviderRequest request,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceDraftActionProviderResult> BookkeepAsync(
        SupplierInvoiceDraftActionProviderRequest request,
        CancellationToken cancellationToken);
}

public sealed record CustomerInvoiceFortnoxActionDto(
    Guid InvoiceId,
    Guid? CreateWriteRequestId,
    Guid? CreateApprovalId,
    string CreateStatus,
    Guid? BookkeepWriteRequestId,
    Guid? BookkeepApprovalId,
    string? BookkeepStatus,
    string Message,
    bool CanRequestCreate,
    bool CanExecuteCreate,
    bool CanRequestBookkeep,
    bool CanExecuteBookkeep,
    string? FortnoxInvoiceNumber,
    DateTime? LastSyncedUtc);

public sealed record RequestCustomerInvoiceFortnoxExportCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox,
    DateOnly? AccountingDate = null,
    string? AuthorityOperation = null);

public sealed record ExecuteCustomerInvoiceFortnoxExportCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? ActorUserId,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record RequestCustomerInvoiceFortnoxBookkeepCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record ExecuteCustomerInvoiceFortnoxBookkeepCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? ActorUserId,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public interface IFinanceCustomerInvoiceFortnoxActionService
{
    Task<CustomerInvoiceFortnoxActionDto> RequestExportAsync(
        RequestCustomerInvoiceFortnoxExportCommand command,
        CancellationToken cancellationToken);

    Task<CustomerInvoiceFortnoxActionDto> ExecuteExportAsync(
        ExecuteCustomerInvoiceFortnoxExportCommand command,
        CancellationToken cancellationToken);

    Task<CustomerInvoiceFortnoxActionDto> RequestBookkeepAsync(
        RequestCustomerInvoiceFortnoxBookkeepCommand command,
        CancellationToken cancellationToken);

    Task<CustomerInvoiceFortnoxActionDto> ExecuteBookkeepAsync(
        ExecuteCustomerInvoiceFortnoxBookkeepCommand command,
        CancellationToken cancellationToken);
}

public sealed record SyncSupplierInvoiceEnrichmentCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public sealed record SupplierInvoiceEnrichmentProviderRequest(
    Guid CompanyId,
    Guid ActionId,
    Guid BillId,
    string SourceBillNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierNumber,
    string? SupplierEmail,
    string? SupplierTaxId,
    string? SupplierPaymentTerms,
    string? SupplierPaymentMethod,
    string? AccountCode,
    string? CostCenter,
    string? Project,
    string? ReviewComment,
    Guid ConnectionId,
    Guid? ActorUserId,
    JsonObject SuggestionPayload);

public sealed record SupplierInvoiceEnrichmentProviderResult(
    string ProviderKey,
    Guid? ConnectionId,
    string Status,
    string ResponseSummary,
    JsonObject ProviderMetadata);

public interface ISupplierInvoiceEnrichmentProvider
{
    string ProviderKey { get; }

    Task<SupplierInvoiceEnrichmentProviderResult> SyncAsync(
        SupplierInvoiceEnrichmentProviderRequest request,
        CancellationToken cancellationToken);
}

public sealed record SupplierInvoiceCorrectionProviderRequest(
    Guid CompanyId,
    Guid ActionId,
    Guid BillId,
    string SourceBillNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierNumber,
    decimal Amount,
    string Currency,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    string InvoiceNumber,
    string? PaymentReference,
    Guid ConnectionId,
    Guid? ActorUserId,
    string? Reason);

public sealed record SupplierInvoiceCorrectionProviderResult(
    string ProviderKey,
    Guid? ConnectionId,
    string Status,
    string ResponseSummary,
    JsonObject ProviderMetadata,
    string? ProviderCreditNoteNumber = null);

public interface ISupplierInvoiceCorrectionProvider
{
    string ProviderKey { get; }

    Task<SupplierInvoiceCorrectionProviderResult> CancelAsync(
        SupplierInvoiceCorrectionProviderRequest request,
        CancellationToken cancellationToken);

    Task<SupplierInvoiceCorrectionProviderResult> CreateCreditNoteAsync(
        SupplierInvoiceCorrectionProviderRequest request,
        CancellationToken cancellationToken);
}

public interface IFinanceToolProvider
{
    Task<FinanceCashBalanceDto> GetCashBalanceAsync(GetFinanceCashBalanceQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(GetFinanceTransactionsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(GetFinanceInvoicesQuery query, CancellationToken cancellationToken);

    Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(GetFinanceMonthlyProfitAndLossQuery query, CancellationToken cancellationToken);

    Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(GetFinanceExpenseBreakdownQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(GetFinanceBillsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(GetFinanceBalancesQuery query, CancellationToken cancellationToken);

    Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(GetFinanceAgentQueryQuery query, CancellationToken cancellationToken);

    Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);

    Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);

    Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(UpdateFinanceTransactionCategoryCommand command, CancellationToken cancellationToken);

    Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(UpdateFinanceInvoiceApprovalStatusCommand command, CancellationToken cancellationToken);

    Task<PaidSupplierBillExpensePostingDto> PostPaidSupplierBillExpenseAsync(PostPaidSupplierBillExpenseCommand command, CancellationToken cancellationToken);
}

