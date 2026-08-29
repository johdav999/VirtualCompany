using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankConsentRevocationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<BankConsentRevocationBackgroundService> _logger;
    public BankConsentRevocationBackgroundService(IServiceScopeFactory scopeFactory,
        ILogger<BankConsentRevocationBackgroundService> logger, TimeProvider? clock = null)
    { _scopeFactory = scopeFactory; _logger = logger; _clock = clock ?? TimeProvider.System; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), _clock, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Bank consent revocation worker iteration failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), _clock, stoppingToken);
        }
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var now = _clock.GetUtcNow().UtcDateTime;
        var task = await db.BankConsentRevocationTasks.IgnoreQueryFilters()
            .Where(x => (x.Status == "pending" && x.NextAttemptUtc <= now) || (x.Status == "running" && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (task is null) return false;
        task.Claim(now); await db.SaveChangesAsync(cancellationToken);
        try
        {
            var connection = await db.BankConnections.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == task.CompanyId && x.Id == task.ConnectionId, cancellationToken);
            var consent = await db.BankConsentVersions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == task.CompanyId && x.Id == task.ConsentVersionId, cancellationToken);
            var credentials = scope.ServiceProvider.GetRequiredService<IProtectedBankCredentialStore>();
            var bundle = await credentials.GetAsync(task.CompanyId, task.ConnectionId, cancellationToken);
            if (bundle is not null)
            {
                var provider = scope.ServiceProvider.GetRequiredService<IBankConnectionProviderRegistry>().GetRequired(connection.ProviderKey);
                await provider.RevokeConsentAsync(task.CompanyId, consent.ProviderConsentId, bundle, cancellationToken);
            }
            await credentials.ClearAsync(task.CompanyId, task.ConnectionId, cancellationToken);
            task.Complete(_clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Remote bank consent revocation completed for company {CompanyId}, connection {ConnectionId}.", task.CompanyId, task.ConnectionId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var safe = exception is BankProviderSafeException providerException
                ? providerException.SafeMessage
                : "Remote bank consent revocation could not be confirmed.";
            task.Retry(safe, _clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Remote bank consent revocation will be retried for company {CompanyId}, connection {ConnectionId}. Attempt={AttemptCount}.", task.CompanyId, task.ConnectionId, task.AttemptCount);
        }
        return true;
    }
}
