using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FixedAssetMaintenanceOptions
{
    public const string SectionName = "FixedAssetMaintenance";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 21600;
    public int CompanyBatchSize { get; set; } = 100;
}

public sealed class FixedAssetMaintenanceRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IFixedAssetService _assets;
    private readonly ICompanyExecutionScopeFactory _tenantScopes;
    private readonly IOptions<FixedAssetMaintenanceOptions> _options;

    public FixedAssetMaintenanceRunner(VirtualCompanyDbContext db, IFixedAssetService assets,
        ICompanyExecutionScopeFactory tenantScopes, IOptions<FixedAssetMaintenanceOptions> options)
    { _db = db; _assets = assets; _tenantScopes = tenantScopes; _options = options; }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var companyIds = await _db.FinanceAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !_db.FixedAssetMigrationConflicts.IgnoreQueryFilters()
                .Any(c => c.CompanyId == x.CompanyId && c.LegacyFinanceAssetId == x.Id))
            .OrderBy(x => x.CompanyId).Select(x => x.CompanyId).Distinct()
            .Take(Math.Clamp(_options.Value.CompanyBatchSize, 1, 500))
            .ToArrayAsync(cancellationToken);
        var discovered = 0;
        foreach (var companyId in companyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = _tenantScopes.BeginScope(companyId);
            discovered += await _assets.DiscoverLegacyConflictsAsync(companyId, cancellationToken);
            _db.ChangeTracker.Clear();
        }
        return discovered;
    }
}

public sealed class FixedAssetMaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FixedAssetMaintenanceOptions> _options;
    private readonly ILogger<FixedAssetMaintenanceBackgroundService> _logger;

    public FixedAssetMaintenanceBackgroundService(IServiceScopeFactory scopeFactory,
        IOptions<FixedAssetMaintenanceOptions> options,
        ILogger<FixedAssetMaintenanceBackgroundService> logger)
    { _scopeFactory = scopeFactory; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Value.Enabled)
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<FixedAssetMaintenanceRunner>()
                        .RunAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Fixed-asset legacy conflict discovery failed; no depreciation history was inferred.");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(
                _options.Value.PollIntervalSeconds, 60, 86400)), stoppingToken);
        }
    }
}
