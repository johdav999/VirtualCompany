namespace VirtualCompany.Application.Finance;

public static class FinanceIntegrationProviderKeys
{
    public const string Fortnox = "fortnox";
}

public interface IFinanceIntegrationProvider
{
    string ProviderKey { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> Capabilities { get; }
    IFinanceIntegrationOAuthService OAuth { get; }
    IFinanceIntegrationSyncService Sync { get; }
    IFinanceIntegrationWriteCommandService WriteCommands { get; }
    IFinanceIntegrationMapper Mapper { get; }
}

public interface IFinanceIntegrationProviderResolver
{
    IFinanceIntegrationProvider GetRequired(string providerKey);
}

public interface IFinanceIntegrationProviderRegistry : IFinanceIntegrationProviderResolver
{
    IReadOnlyCollection<IFinanceIntegrationProvider> Providers { get; }
    IFinanceIntegrationProvider Resolve(string providerKey);
}

public sealed record FinanceIntegrationProviderMetadata(
    string ProviderKey,
    string DisplayName,
    IReadOnlyCollection<string> Capabilities);

public sealed class FinanceIntegrationProviderNotFoundException(string providerKey)
    : InvalidOperationException($"Finance integration provider '{providerKey}' is not registered.")
{
    public string ProviderKey { get; } = providerKey;
}

public sealed record StartFinanceIntegrationOAuthConnectionCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid UserId,
    Uri? ReturnUri = null,
    bool Reconnect = false);

public sealed record CompleteFinanceIntegrationOAuthConnectionCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid UserId,
    string State,
    string Code,
    string? Nonce = null,
    string? ProviderError = null);

public sealed record RefreshFinanceIntegrationAccessTokenCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid? ConnectionId = null);

public sealed record DisconnectFinanceIntegrationConnectionCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid UserId);

public sealed record GetFinanceIntegrationConnectionStatusQuery(
    string ProviderKey,
    Guid CompanyId,
    Guid UserId);

public sealed record RunFinanceIntegrationSyncCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid? ConnectionId = null,
    string? CorrelationId = null,
    Guid? ActorUserId = null,
    bool FullSync = false);

public sealed record GetFinanceIntegrationSyncHistoryQuery(
    string ProviderKey,
    Guid CompanyId,
    int Limit = 25);

public sealed record FinanceIntegrationOAuthResult(
    string ProviderKey,
    Uri AuthorizationUrl,
    DateTime ExpiresUtc);

public sealed record FinanceIntegrationOAuthCompletionResult(
    string ProviderKey,
    Guid ConnectionId,
    Guid CompanyId,
    string Status,
    Uri? ReturnUri);

public sealed record FinanceIntegrationTokenSnapshot(
    string ProviderKey,
    Guid ConnectionId,
    Guid CompanyId,
    string Status,
    string? AccessToken,
    string? RefreshToken,
    DateTime? AccessTokenExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes,
    string? ProviderTenantId);

public sealed record FinanceIntegrationAccessTokenResult(
    string ProviderKey,
    bool Succeeded,
    string? AccessToken,
    DateTime? ExpiresUtc,
    bool NeedsReconnect,
    string? SafeFailureMessage);

public sealed record FinanceIntegrationConnectionStatusResult(
    string ProviderKey,
    bool IsConnected,
    Guid? ConnectionId,
    string? ConnectionStatus,
    DateTime? ConnectedAtUtc,
    DateTime? AccessTokenExpiresUtc,
    DateTime? LastRefreshAttemptUtc,
    string? LastErrorSummary,
    DateTime? LastSuccessfulSyncUtc = null);

public sealed record FinanceIntegrationConnectionDisconnectResult(
    string ProviderKey,
    Guid CompanyId,
    Guid? ConnectionId,
    string Status,
    DateTime DisconnectedUtc,
    string Message);

public sealed record FinanceIntegrationEntitySyncResult(
    string EntityType,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    string? ErrorSummary = null);

public sealed record FinanceIntegrationSyncResult(
    string ProviderKey,
    Guid CompanyId,
    Guid ConnectionId,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string Status,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    IReadOnlyList<FinanceIntegrationEntitySyncResult> Entities,
    string? ErrorSummary = null,
    int RetryAttempts = 0,
    string? RetryOutcome = null);

public sealed record FinanceIntegrationSyncHistoryItem(
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
    IReadOnlyList<FinanceIntegrationEntitySyncResult>? Entities = null);

public sealed record FinanceIntegrationSyncHistoryResult(
    string ProviderKey,
    Guid CompanyId,
    IReadOnlyList<FinanceIntegrationSyncHistoryItem> Items);

public sealed record FinanceIntegrationWriteCommand(
    string ProviderKey,
    Guid CompanyId,
    Guid? ConnectionId,
    Guid? ActorUserId,
    string CommandType,
    string HttpMethod,
    string Path,
    string TargetCompany,
    string PayloadSummary,
    string PayloadHash,
    FinanceIntegrationWritePayload Payload,
    Guid WriteRequestId,
    string? CorrelationId = null,
    Guid? ApprovedApprovalId = null);

public sealed record FinanceIntegrationWritePayload(
    string SanitizedJson,
    string? ProviderPayloadType = null);

public sealed record FinanceIntegrationWriteResult(
    string ProviderKey,
    Guid WriteRequestId,
    Guid? ApprovalId,
    string Status,
    string Message,
    bool CanExecute);

public sealed record FinanceIntegrationOutboundExecutionResult(
    string ProviderKey,
    Guid WriteRequestId,
    Guid? ApprovalId,
    string Status,
    int? ResponseStatusCode,
    string Summary,
    bool Executed);

public interface IFortnoxOutboundActionExecutor
{
    Task<FinanceIntegrationOutboundExecutionResult> ExecuteApprovedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
}

public sealed record FinanceIntegrationWriteApprovalCheck(
    string ProviderKey,
    Guid CompanyId,
    Guid? ConnectionId,
    Guid? ActorUserId,
    Guid? ApprovedApprovalId,
    string CommandType,
    string HttpMethod,
    string Path,
    string TargetCompany,
    string PayloadSummary,
    string PayloadHash,
    FinanceIntegrationWritePayload Payload,
    Guid WriteRequestId);

public static class FinanceIntegrationWriteCommandTypes
{
    public const string Payment = "payment";
    public const string InvoiceExport = "invoice_export";
    public const string SupplierMasterData = "supplier_master_data";
    public const string VoucherCreate = "voucher_create";
    public const string AccountingRecord = "accounting_record";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? AccountingRecord
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class FinanceIntegrationWriteCommandStatuses
{
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string Executing = "executing";
    public const string Executed = "executed";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public static class FinanceIntegrationWriteRetryPolicies
{
    public const string None = "none";
    public const string TransientOnly = "transient_only";
}

public interface IFinanceIntegrationWriteApprovalService
{
    Task EnsureApprovedAsync(FinanceIntegrationWriteApprovalCheck check, CancellationToken cancellationToken);
    Task RecordExecutionSucceededAsync(FinanceIntegrationWriteApprovalCheck check, object? responsePayload, CancellationToken cancellationToken);
    Task RecordExecutionFailedAsync(FinanceIntegrationWriteApprovalCheck check, Exception exception, CancellationToken cancellationToken);
}

public interface IFinanceIntegrationOAuthService
{
    string ProviderKey { get; }
    Task<FinanceIntegrationOAuthResult> BuildAuthorizationUrlAsync(StartFinanceIntegrationOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<FinanceIntegrationOAuthCompletionResult> HandleCallbackAsync(CompleteFinanceIntegrationOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<FinanceIntegrationAccessTokenResult> GetValidAccessTokenAsync(RefreshFinanceIntegrationAccessTokenCommand command, CancellationToken cancellationToken);
    Task<FinanceIntegrationConnectionStatusResult> GetStatusAsync(GetFinanceIntegrationConnectionStatusQuery query, CancellationToken cancellationToken);
    Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken);
    Task<FinanceIntegrationConnectionDisconnectResult> DisconnectAsync(DisconnectFinanceIntegrationConnectionCommand command, CancellationToken cancellationToken);
}

public interface IFinanceIntegrationSyncService
{
    string ProviderKey { get; }
    Task<FinanceIntegrationSyncResult> SyncAsync(RunFinanceIntegrationSyncCommand command, CancellationToken cancellationToken);
    Task<FinanceIntegrationSyncHistoryResult> GetHistoryAsync(GetFinanceIntegrationSyncHistoryQuery query, CancellationToken cancellationToken);
}

public interface IFinanceIntegrationWriteCommandService
{
    string ProviderKey { get; }
    Task<FinanceIntegrationWriteResult> RequestApprovalAsync(FinanceIntegrationWriteCommand command, CancellationToken cancellationToken);
    Task<FinanceIntegrationWriteResult> EnsureApprovedForExecutionAsync(FinanceIntegrationWriteCommand command, CancellationToken cancellationToken);
    Task RecordExecutionSucceededAsync(FinanceIntegrationWriteCommand command, object? responsePayload, CancellationToken cancellationToken);
    Task RecordExecutionFailedAsync(FinanceIntegrationWriteCommand command, Exception exception, CancellationToken cancellationToken);
}

public interface IFinanceIntegrationMapper
{
    string ProviderKey { get; }
}
