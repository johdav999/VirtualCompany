using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceIntegrationStartupSyncOptions
{
    public const string SectionName = "FinanceIntegrations:StartupSync";

    public bool Enabled { get; set; } = true;
    public int StartupDelaySeconds { get; set; } = 5;
    public int SyncTimeoutSeconds { get; set; } = 300;
    public int LockTtlSeconds { get; set; } = 600;
    public bool FullSync { get; set; }
    public string[] ProviderKeys { get; set; } = [FinanceIntegrationProviderKeys.Fortnox];
}

public sealed class FinanceIntegrationStartupSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IOptions<FinanceIntegrationStartupSyncOptions> _options;
    private readonly ILogger<FinanceIntegrationStartupSyncBackgroundService> _logger;

    public FinanceIntegrationStartupSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockProvider lockProvider,
        IOptions<FinanceIntegrationStartupSyncOptions> options,
        ILogger<FinanceIntegrationStartupSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockProvider = lockProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            _logger.LogInformation("Finance integration startup sync is disabled.");
            return;
        }

        try
        {
            if (options.StartupDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.StartupDelaySeconds), stoppingToken);
            }

            await using var lockHandle = await _lockProvider.TryAcquireAsync(
                "finance-integrations:startup-sync",
                TimeSpan.FromSeconds(options.LockTtlSeconds),
                stoppingToken);

            if (lockHandle is null)
            {
                _logger.LogInformation("Finance integration startup sync skipped because another instance holds the startup sync lock.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var providerResolver = scope.ServiceProvider.GetRequiredService<IFinanceIntegrationProviderResolver>();
            var companyExecutionScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
            var targets = await ResolveTargetsAsync(dbContext, options, stoppingToken);

            if (targets.Count == 0)
            {
                _logger.LogInformation("Finance integration startup sync found no connected integration(s).");
                return;
            }

            var synced = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var target in targets)
            {
                using var companyScope = companyExecutionScopeFactory.BeginScope(target.CompanyId);
                using var targetTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                targetTimeout.CancelAfter(TimeSpan.FromSeconds(options.SyncTimeoutSeconds));
                var targetToken = targetTimeout.Token;
                var correlationId = $"finance-integration-startup-sync:{target.ProviderKey}:{target.CompanyId:N}";

                try
                {
                    if (!IsProviderEnabled(scope.ServiceProvider, target.ProviderKey))
                    {
                        skipped++;
                        _logger.LogInformation(
                            "Finance integration startup sync skipped provider {ProviderKey} for company {CompanyId}, connection {ConnectionId} because the provider is disabled.",
                            target.ProviderKey,
                            target.CompanyId,
                            target.ConnectionId);
                        continue;
                    }

                    var provider = providerResolver.GetRequired(target.ProviderKey);
                    var accessTokenResult = await provider.OAuth.GetValidAccessTokenAsync(
                        new RefreshFinanceIntegrationAccessTokenCommand(target.ProviderKey, target.CompanyId, target.ConnectionId),
                        targetToken);

                    if (!accessTokenResult.Succeeded)
                    {
                        skipped++;
                        _logger.LogWarning(
                            "Finance integration startup sync skipped provider {ProviderKey} for company {CompanyId}, connection {ConnectionId}: {Reason}",
                            target.ProviderKey,
                            target.CompanyId,
                            target.ConnectionId,
                            accessTokenResult.SafeFailureMessage ?? "The integration needs to be reconnected.");
                        continue;
                    }

                    var result = await provider.Sync.SyncAsync(
                        new RunFinanceIntegrationSyncCommand(
                            target.ProviderKey,
                            target.CompanyId,
                            target.ConnectionId,
                            correlationId,
                            ActorUserId: null,
                            options.FullSync),
                        targetToken);

                    synced++;
                    _logger.LogInformation(
                        "Finance integration startup sync completed provider {ProviderKey} for company {CompanyId}, connection {ConnectionId}. Status={Status}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}.",
                        result.ProviderKey,
                        result.CompanyId,
                        result.ConnectionId,
                        result.Status,
                        result.Created,
                        result.Updated,
                        result.Skipped,
                        result.Errors);
                }
                catch (OperationCanceledException) when (targetToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    failed++;
                    _logger.LogWarning(
                        "Finance integration startup sync timed out for provider {ProviderKey}, company {CompanyId}, connection {ConnectionId}.",
                        target.ProviderKey,
                        target.CompanyId,
                        target.ConnectionId);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Finance integration startup sync failed for provider {ProviderKey}, company {CompanyId}, connection {ConnectionId}.",
                        target.ProviderKey,
                        target.CompanyId,
                        target.ConnectionId);
                }
            }

            _logger.LogInformation(
                "Finance integration startup sync finished. Targets={TargetCount}, Synced={SyncedCount}, Skipped={SkippedCount}, Failed={FailedCount}.",
                targets.Count,
                synced,
                skipped,
                failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finance integration startup sync failed before any provider sync could run.");
        }
    }

    private static async Task<IReadOnlyList<StartupSyncTarget>> ResolveTargetsAsync(
        VirtualCompanyDbContext dbContext,
        FinanceIntegrationStartupSyncOptions options,
        CancellationToken cancellationToken)
    {
        var providerKeys = ResolveProviderKeys(options.ProviderKeys);
        if (providerKeys.Count == 0)
        {
            return [];
        }

        var providerKeyArray = providerKeys.ToArray();
        var targets = await dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => providerKeyArray.Contains(x.ProviderKey) && x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .Select(x => new StartupSyncTarget(x.ProviderKey, x.CompanyId, x.Id))
            .ToListAsync(cancellationToken);

        if (providerKeys.Contains(FinanceIntegrationProviderKeys.Fortnox))
        {
            var existing = targets
                .Where(x => string.Equals(x.ProviderKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase))
                .Select(x => (x.CompanyId, x.ConnectionId))
                .ToHashSet();

            var legacyFortnoxTargets = await dbContext.FortnoxConnections
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Status == FortnoxConnectionStatus.Connected)
                .OrderBy(x => x.CompanyId)
                .Select(x => new StartupSyncTarget(FinanceIntegrationProviderKeys.Fortnox, x.CompanyId, x.Id))
                .ToListAsync(cancellationToken);

            foreach (var legacyTarget in legacyFortnoxTargets)
            {
                if (existing.Add((legacyTarget.CompanyId, legacyTarget.ConnectionId)))
                {
                    targets.Add(legacyTarget);
                }
            }
        }

        return targets
            .OrderBy(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CompanyId)
            .ThenBy(x => x.ConnectionId)
            .ToList();
    }

    private static HashSet<string> ResolveProviderKeys(IEnumerable<string>? providerKeys)
    {
        var resolved = providerKeys?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        return resolved.Count == 0
            ? [FinanceIntegrationProviderKeys.Fortnox]
            : resolved;
    }

    private static bool IsProviderEnabled(IServiceProvider serviceProvider, string providerKey) =>
        string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase)
            ? serviceProvider.GetRequiredService<IOptionsMonitor<FortnoxOptions>>().CurrentValue.Enabled
            : true;

    private sealed record StartupSyncTarget(string ProviderKey, Guid CompanyId, Guid ConnectionId);
}
