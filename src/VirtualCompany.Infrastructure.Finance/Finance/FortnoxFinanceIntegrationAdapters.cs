using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxFinanceIntegrationOAuthService(IFortnoxOAuthService fortnoxOAuthService)
    : IFinanceIntegrationOAuthService
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<FinanceIntegrationOAuthResult> BuildAuthorizationUrlAsync(
        StartFinanceIntegrationOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureProvider(command.ProviderKey);
        var result = await fortnoxOAuthService.BuildAuthorizationUrlAsync(
            new StartFortnoxOAuthConnectionCommand(
                command.CompanyId,
                command.UserId,
                command.ReturnUri,
                command.Reconnect),
            cancellationToken);

        return new FinanceIntegrationOAuthResult(ProviderKey, result.AuthorizationUrl, result.ExpiresUtc);
    }

    public async Task<FinanceIntegrationOAuthCompletionResult> HandleCallbackAsync(
        CompleteFinanceIntegrationOAuthConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureProvider(command.ProviderKey);
        var result = await fortnoxOAuthService.HandleCallbackAsync(
            new CompleteFortnoxOAuthConnectionCommand(
                command.CompanyId,
                command.UserId,
                command.State,
                command.Code,
                command.Nonce,
                command.ProviderError),
            cancellationToken);

        return new FinanceIntegrationOAuthCompletionResult(
            ProviderKey,
            result.ConnectionId,
            result.CompanyId,
            result.Status,
            result.ReturnUri);
    }

    public async Task<FinanceIntegrationAccessTokenResult> GetValidAccessTokenAsync(
        RefreshFinanceIntegrationAccessTokenCommand command,
        CancellationToken cancellationToken)
    {
        EnsureProvider(command.ProviderKey);
        var result = await fortnoxOAuthService.GetValidAccessTokenAsync(
            new RefreshFortnoxAccessTokenCommand(command.CompanyId, command.ConnectionId),
            cancellationToken);

        return new FinanceIntegrationAccessTokenResult(
            ProviderKey,
            result.Succeeded,
            result.AccessToken,
            result.ExpiresUtc,
            result.NeedsReconnect,
            result.SafeFailureMessage);
    }

    public async Task<FinanceIntegrationConnectionStatusResult> GetStatusAsync(
        GetFinanceIntegrationConnectionStatusQuery query,
        CancellationToken cancellationToken)
    {
        EnsureProvider(query.ProviderKey);
        var result = await fortnoxOAuthService.GetStatusAsync(
            new GetFortnoxConnectionStatusQuery(query.CompanyId, query.UserId),
            cancellationToken);

        return new FinanceIntegrationConnectionStatusResult(
            ProviderKey,
            result.IsConnected,
            result.ConnectionId,
            result.ConnectionStatus,
            result.ConnectedAtUtc,
            result.AccessTokenExpiresUtc,
            result.LastRefreshAttemptUtc,
            result.LastErrorSummary,
            result.LastSuccessfulSyncUtc);
    }

    public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
        fortnoxOAuthService.MarkNeedsReconnectAsync(companyId, connectionId, safeReason, cancellationToken);

    public async Task<FinanceIntegrationConnectionDisconnectResult> DisconnectAsync(
        DisconnectFinanceIntegrationConnectionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureProvider(command.ProviderKey);
        var result = await fortnoxOAuthService.DisconnectAsync(
            new DisconnectFortnoxConnectionCommand(command.CompanyId, command.UserId),
            cancellationToken);

        return new FinanceIntegrationConnectionDisconnectResult(
            ProviderKey,
            result.CompanyId,
            result.ConnectionId,
            result.Status,
            result.DisconnectedUtc,
            result.Message);
    }

    private static void EnsureProvider(string providerKey)
    {
        if (!string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase))
        {
            throw new FinanceIntegrationProviderNotFoundException(providerKey);
        }
    }
}

public sealed class FortnoxFinanceIntegrationSyncService(IFortnoxSyncService fortnoxSyncService)
    : IFinanceIntegrationSyncService
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<FinanceIntegrationSyncResult> SyncAsync(
        RunFinanceIntegrationSyncCommand command,
        CancellationToken cancellationToken)
    {
        EnsureProvider(command.ProviderKey);
        var result = await fortnoxSyncService.SyncAsync(
            new RunFortnoxSyncCommand(command.CompanyId, command.ConnectionId, command.CorrelationId, command.ActorUserId, command.FullSync),
            cancellationToken);

        return ToGeneric(result);
    }

    public async Task<FinanceIntegrationSyncHistoryResult> GetHistoryAsync(
        GetFinanceIntegrationSyncHistoryQuery query,
        CancellationToken cancellationToken)
    {
        EnsureProvider(query.ProviderKey);
        var result = await fortnoxSyncService.GetHistoryAsync(
            new GetFortnoxSyncHistoryQuery(query.CompanyId, query.Limit),
            cancellationToken);

        return new FinanceIntegrationSyncHistoryResult(
            ProviderKey,
            result.CompanyId,
            result.Items
                .Select(item => new FinanceIntegrationSyncHistoryItem(
                    item.Id,
                    item.ConnectionId,
                    item.StartedUtc,
                    item.CompletedUtc,
                    item.Status,
                    item.Created,
                    item.Updated,
                    item.Skipped,
                    item.Errors,
                    item.Summary,
                    item.ErrorSummary,
                    item.RetryAttempts,
                    item.RetryOutcome,
                    item.Entities?
                        .Select(entity => new FinanceIntegrationEntitySyncResult(
                            entity.EntityType,
                            entity.Created,
                            entity.Updated,
                            entity.Skipped,
                            entity.Errors,
                            entity.ErrorSummary))
                        .ToList()))
                .ToList());
    }

    private static FinanceIntegrationSyncResult ToGeneric(FortnoxSyncResult result) =>
        new(
            FinanceIntegrationProviderKeys.Fortnox,
            result.CompanyId,
            result.ConnectionId,
            result.StartedUtc,
            result.CompletedUtc,
            result.Status,
            result.Created,
            result.Updated,
            result.Skipped,
            result.Errors,
            result.Entities
                .Select(entity => new FinanceIntegrationEntitySyncResult(
                    entity.EntityType,
                    entity.Created,
                    entity.Updated,
                    entity.Skipped,
                    entity.Errors,
                    entity.ErrorSummary))
                .ToList(),
            result.ErrorSummary,
            result.RetryAttempts,
            result.RetryOutcome);

    private static void EnsureProvider(string providerKey)
    {
        if (!string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase))
        {
            throw new FinanceIntegrationProviderNotFoundException(providerKey);
        }
    }
}

public sealed class FortnoxFinanceIntegrationMapper(IFortnoxMappingService fortnoxMappingService)
    : IFinanceIntegrationMapper
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;
    public IFortnoxMappingService Fortnox => fortnoxMappingService;
}
