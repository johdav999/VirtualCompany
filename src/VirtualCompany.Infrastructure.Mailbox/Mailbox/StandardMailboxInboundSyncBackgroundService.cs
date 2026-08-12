using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class StandardMailboxInboundSyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StandardMailboxInboundSyncBackgroundService> _logger;

    public StandardMailboxInboundSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<StandardMailboxInboundSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation(
                "Mailbox polling worker starting. InitialDelaySeconds: {InitialDelaySeconds}. PollIntervalSeconds: {PollIntervalSeconds}.",
                10,
                PollInterval.TotalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var cycleStartedUtc = DateTime.UtcNow;
                var dispatched = 0;
                var completedDispatches = 0;
                var failed = 0;
                var skipped = 0;

                try
                {
                    var connections = await LoadConnectionsAsync(stoppingToken);
                    _logger.LogInformation(
                        "Mailbox polling cycle loaded {ConnectionCount} configured connection(s). Active: {ActiveCount}. Inactive: {InactiveCount}.",
                        connections.Length,
                        connections.Count(connection => connection.Status == MailboxConnectionStatus.Active),
                        connections.Count(connection => connection.Status != MailboxConnectionStatus.Active));

                    foreach (var connection in connections)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        if (connection.Status != MailboxConnectionStatus.Active)
                        {
                            skipped++;
                            _logger.LogInformation(
                                "Mailbox polling skipped an inactive connection. CompanyId: {CompanyId}. Provider: {Provider}. Purpose: {Purpose}. ConnectionId: {ConnectionId}. Status: {Status}. ErrorCode: {ErrorCode}. ActionRequired: {ActionRequired}.",
                                connection.CompanyId,
                                connection.Provider,
                                connection.Purpose,
                                connection.ConnectionId,
                                connection.Status,
                                connection.LastErrorCode ?? "(none)",
                                GetRequiredAction(connection.Status));
                            continue;
                        }

                        dispatched++;
                        try
                        {
                            _logger.LogInformation(
                                "Mailbox polling dispatching connection scan. CompanyId: {CompanyId}. Provider: {Provider}. Purpose: {Purpose}. ConnectionId: {ConnectionId}.",
                                connection.CompanyId,
                                connection.Provider,
                                connection.Purpose,
                                connection.ConnectionId);

                            using var scope = _scopeFactory.CreateScope();
                            var companyScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
                            using var companyScope = companyScopeFactory.BeginScope(connection.CompanyId);
                            var orchestrator = scope.ServiceProvider.GetRequiredService<IConnectedMailboxInboxScanOrchestrator>();
                            await orchestrator.ExecuteConnectedMailboxScanAsync(
                                new ConnectedMailboxInboxScanJob(
                                    connection.CompanyId,
                                    connection.UserId,
                                    connection.ConnectionId,
                                    connection.Provider,
                                    connection.Purpose),
                                stoppingToken);
                            completedDispatches++;
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            failed++;
                            _logger.LogWarning(
                                exception,
                                "Mailbox polling failed. CompanyId: {CompanyId}. Provider: {Provider}. Purpose: {Purpose}. ConnectionId: {ConnectionId}.",
                                connection.CompanyId,
                                connection.Provider,
                                connection.Purpose,
                                connection.ConnectionId);
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogError(
                        exception,
                        "Mailbox polling cycle could not load or dispatch connections. The worker will retry after the polling interval.");
                }

                _logger.LogInformation(
                    "Mailbox polling cycle completed. DurationMilliseconds: {DurationMilliseconds}. Dispatched: {Dispatched}. CompletedDispatches: {CompletedDispatches}. FailedDispatches: {FailedDispatches}. SkippedInactive: {SkippedInactive}.",
                    (DateTime.UtcNow - cycleStartedUtc).TotalMilliseconds,
                    dispatched,
                    completedDispatches,
                    failed,
                    skipped);

                await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<ConnectionWorkItem[]> LoadConnectionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        return await dbContext.MailboxConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(connection => connection.IsPrimaryInbound &&
                (connection.Purpose == MailboxPurpose.Finance ||
                connection.Purpose == MailboxPurpose.Sales ||
                connection.Purpose == MailboxPurpose.Support))
            .OrderBy(connection => connection.CompanyId)
            .ThenBy(connection => connection.Id)
            .Select(connection => new ConnectionWorkItem(
                connection.CompanyId,
                connection.UserId,
                connection.Id,
                connection.Provider,
                connection.Purpose,
                connection.Status,
                connection.LastErrorCode))
            .ToArrayAsync(cancellationToken);
    }

    internal static bool IsSupportedPurpose(MailboxProvider provider, MailboxPurpose purpose) =>
        Enum.IsDefined(provider) &&
        purpose is MailboxPurpose.Finance or MailboxPurpose.Sales or MailboxPurpose.Support;

    private static string GetRequiredAction(MailboxConnectionStatus status) =>
        status switch
        {
            MailboxConnectionStatus.TokenExpired => "ReconnectMailbox",
            MailboxConnectionStatus.Failed => "ReviewConnectionError",
            MailboxConnectionStatus.Pending => "CompleteConnection",
            _ => "None"
        };

    private sealed record ConnectionWorkItem(
        Guid CompanyId,
        Guid UserId,
        Guid ConnectionId,
        MailboxProvider Provider,
        MailboxPurpose Purpose,
        MailboxConnectionStatus Status,
        string? LastErrorCode);
}
