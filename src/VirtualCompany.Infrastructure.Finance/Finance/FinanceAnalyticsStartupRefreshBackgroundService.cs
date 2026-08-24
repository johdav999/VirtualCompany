using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAnalyticsStartupRefreshOptions
{
    public const string SectionName = "FinanceAnalyticsStartupRefresh";
    public bool Enabled { get; set; } = true;
    public int CompanyBatchSize { get; set; } = 500;
}

public sealed class FinanceAnalyticsStartupRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinanceAnalyticsStartupRefreshBackgroundService> _logger;
    private readonly IOptions<FinanceAnalyticsStartupRefreshOptions> _options;

    public FinanceAnalyticsStartupRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceAnalyticsStartupRefreshOptions> options,
        ILogger<FinanceAnalyticsStartupRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Finance analytics startup refresh is disabled.");
            return;
        }
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var financeReadService = scope.ServiceProvider.GetRequiredService<IFinanceReadService>();
            var companyExecutionScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();

            var companyIds = await dbContext.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.FinanceSeedStatus == FinanceSeedingState.Seeded)
                .OrderBy(x => x.CreatedUtc)
                .Select(x => x.Id)
                .Take(Math.Max(1, _options.Value.CompanyBatchSize))
                .ToListAsync(stoppingToken);

            var queued = 0;
            foreach (var companyId in companyIds)
            {
                using var companyScope = companyExecutionScopeFactory.BeginScope(companyId);
                await financeReadService.QueueInsightsSnapshotRefreshAsync(
                    new QueueFinanceInsightsSnapshotRefreshCommand(
                        companyId,
                        CorrelationId: $"finance-analytics-startup:{companyId:N}"),
                    stoppingToken);
                queued++;
            }

            _logger.LogInformation(
                "Finance analytics startup refresh queued {QueuedCount} seeded companie(s).",
                queued);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finance analytics startup refresh failed.");
        }
    }
}
