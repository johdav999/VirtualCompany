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
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var connection in await LoadConnectionsAsync(stoppingToken))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var companyScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
                        using var companyScope = companyScopeFactory.BeginScope(connection.CompanyId);
                        var orchestrator = scope.ServiceProvider.GetRequiredService<IConnectedMailboxInboxScanOrchestrator>();
                        await orchestrator.ExecuteConnectedMailboxScanAsync(
                            new ConnectedMailboxInboxScanJob(
                                connection.CompanyId,
                                connection.UserId,
                                connection.ConnectionId,
                                MailboxProvider.StandardEmail,
                                connection.Purpose),
                            stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogWarning(
                            exception,
                            "Hosted mailbox polling failed. CompanyId: {CompanyId}. Purpose: {Purpose}. ConnectionId: {ConnectionId}.",
                            connection.CompanyId,
                            connection.Purpose,
                            connection.ConnectionId);
                    }
                }

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
            .Where(connection => connection.Provider == MailboxProvider.StandardEmail &&
                connection.Status == MailboxConnectionStatus.Active &&
                (connection.Purpose == MailboxPurpose.Sales || connection.Purpose == MailboxPurpose.Support))
            .OrderBy(connection => connection.CompanyId)
            .ThenBy(connection => connection.Id)
            .Select(connection => new ConnectionWorkItem(
                connection.CompanyId,
                connection.UserId,
                connection.Id,
                connection.Purpose))
            .ToArrayAsync(cancellationToken);
    }

    private sealed record ConnectionWorkItem(
        Guid CompanyId,
        Guid UserId,
        Guid ConnectionId,
        MailboxPurpose Purpose);
}
