namespace VirtualCompany.Application.Finance;

public sealed record RunFortnoxSyncCommand(
    Guid CompanyId,
    Guid? ConnectionId = null,
    string? CorrelationId = null);

public sealed record GetFortnoxSyncHistoryQuery(
    Guid CompanyId,
    int Limit = 25);

public sealed record FortnoxEntitySyncResult(
    string EntityType,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
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
    string? ErrorSummary = null);

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
    string? ErrorSummary);

public sealed record FortnoxSyncHistoryResult(
    Guid CompanyId,
    IReadOnlyList<FortnoxSyncHistoryItem> Items);

public interface IFortnoxSyncService
{
    Task<FortnoxSyncResult> SyncAsync(RunFortnoxSyncCommand command, CancellationToken cancellationToken);
    Task<FortnoxSyncHistoryResult> GetHistoryAsync(GetFortnoxSyncHistoryQuery query, CancellationToken cancellationToken);
}

public interface IFortnoxMappingService
{
    FortnoxCounterpartySyncModel MapCustomer(FortnoxCustomer customer);
    FortnoxCounterpartySyncModel MapSupplier(FortnoxSupplier supplier);
    FortnoxAccountSyncModel MapAccount(FortnoxAccount account);
    FortnoxArticleSyncModel MapArticle(FortnoxArticle article);
    FortnoxProjectSyncModel MapProject(FortnoxProject project);
    FortnoxInvoiceSyncModel MapInvoice(FortnoxInvoice invoice);
    FortnoxSupplierInvoiceSyncModel MapSupplierInvoice(FortnoxSupplierInvoice invoice);
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
    decimal PaidAmount,
    DateTime? ExternalUpdatedUtc);

public sealed record FortnoxVoucherSyncModel(
    string ExternalId,
    string ExternalNumber,
    DateTime TransactionUtc,
    string Description,
    decimal Amount,
    DateTime? ExternalUpdatedUtc);
