using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public sealed record RunFortnoxSyncCommand(
    Guid CompanyId,
    Guid? ConnectionId = null,
    string? CorrelationId = null,
    Guid? ActorUserId = null,
    bool FullSync = false);

public sealed record GetFortnoxSyncHistoryQuery(
    Guid CompanyId,
    int Limit = 25);

public sealed record RepairFortnoxSupplierInvoicesCommand(
    Guid CompanyId,
    Guid ConnectionId,
    IReadOnlyList<FortnoxSupplierInvoiceSyncModel> Invoices,
    string? CorrelationId = null,
    Guid? ActorUserId = null);

public sealed record FortnoxSupplierInvoiceRepairItem(
    string ExternalId,
    string Outcome,
    Guid? BillId = null,
    string? Reason = null);

public sealed record FortnoxSupplierInvoiceRepairResult(
    Guid CompanyId,
    Guid ConnectionId,
    int Repaired,
    int Skipped,
    int Rejected,
    IReadOnlyList<FortnoxSupplierInvoiceRepairItem> Items);

public sealed record FortnoxEntitySyncResult(
    string EntityType,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    int RetryAttempts = 0,
    string? RetryOutcome = null,
    string? ErrorSummary = null);

public sealed record FortnoxSyncResult(
    Guid CompanyId,
    Guid ConnectionId,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string Status,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    IReadOnlyList<FortnoxEntitySyncResult> Entities,
    string? ErrorSummary = null,
    int RetryAttempts = 0,
    string? RetryOutcome = null);

public sealed record FortnoxSyncHistoryItem(
    Guid Id,
    Guid? ConnectionId,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string Status,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    string Summary,
    string? ErrorSummary,
    int RetryAttempts = 0,
    string? RetryOutcome = null,
    IReadOnlyList<FortnoxEntitySyncResult>? Entities = null);

public sealed record FortnoxSyncHistoryResult(
    Guid CompanyId,
    IReadOnlyList<FortnoxSyncHistoryItem> Items);

public interface IFortnoxSyncService
{
    Task<FortnoxSyncResult> SyncAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken);
    Task<FortnoxSyncHistoryResult> GetHistoryAsync(GetFortnoxSyncHistoryQuery query, CancellationToken cancellationToken);
    Task<FortnoxSupplierInvoiceRepairResult> RepairDanglingSupplierInvoicesAsync(
        RepairFortnoxSupplierInvoicesCommand command,
        CancellationToken cancellationToken);
}

public interface IFortnoxMappingService
{
    FortnoxCounterpartySyncModel MapCustomer(FortnoxCustomer customer);
    FortnoxCounterpartySyncModel MapSupplier(FortnoxSupplier supplier);
    FortnoxAccountSyncModel MapAccount(FortnoxAccount account);
    FortnoxArticleSyncModel MapArticle(FortnoxArticle article);
    FortnoxProjectSyncModel MapProject(FortnoxProject project);
    FortnoxInvoiceSyncModel MapInvoice(FortnoxInvoice invoice);
    FortnoxInvoicePaymentSyncModel MapInvoicePayment(FortnoxInvoicePayment payment);
    FortnoxSupplierInvoiceSyncModel MapSupplierInvoice(FortnoxSupplierInvoice invoice);
    FortnoxSupplierInvoicePaymentSyncModel MapSupplierInvoicePayment(FortnoxSupplierInvoicePayment payment);
    FortnoxVoucherSyncModel MapVoucher(FortnoxVoucher voucher);
}

public sealed record FortnoxCounterpartySyncModel(
    string ExternalId,
    string ExternalNumber,
    string Name,
    string CounterpartyType,
    string? Email,
    string? TaxId,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxAccountSyncModel(
    string ExternalId,
    string ExternalNumber,
    string Code,
    string Name,
    string AccountType,
    decimal? BalanceSnapshotAmount,
    DateTime? BalanceSnapshotUtc,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxArticleSyncModel(
    string ExternalId,
    string ExternalNumber,
    string Name,
    decimal SalesPrice,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxProjectSyncModel(
    string ExternalId,
    string ExternalNumber,
    string Name,
    string Status,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxInvoiceSyncModel(
    string ExternalId,
    string ExternalNumber,
    string CustomerNumber,
    string CustomerName,
    DateTime IssuedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    string SettlementStatus,
    string PostingStatus,
    string DueStatus,
    string DocumentKind,
    string? ProviderStatus,
    string ProcessingStatus,
    decimal PaidAmount,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxSupplierInvoiceSyncModel(
    string ExternalId,
    string ExternalNumber,
    string SupplierNumber,
    string SupplierName,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    string SettlementStatus,
    string PostingStatus,
    string DueStatus,
    string DocumentKind,
    string? ProviderStatus,
    string ProcessingStatus,
    decimal PaidAmount,
    DateTime? ExternalUpdatedUtc,
    JsonObject? ProviderMetadata = null);

public sealed record FortnoxInvoicePaymentSyncModel(
    string ExternalId,
    string ExternalNumber,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    DateTime PaymentUtc,
    string Status,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxSupplierInvoicePaymentSyncModel(
    string ExternalId,
    string ExternalNumber,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    DateTime PaymentUtc,
    string Status,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxVoucherSyncModel(
    string ExternalId,
    string ExternalNumber,
    string? ReferenceNumber,
    DateTime TransactionUtc,
    string Description,
    decimal Amount,
    DateTime? ExternalUpdatedUtc);
