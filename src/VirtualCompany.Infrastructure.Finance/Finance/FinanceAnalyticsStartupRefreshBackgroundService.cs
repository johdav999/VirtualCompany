using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAnalyticsStartupRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinanceAnalyticsStartupRefreshBackgroundService> _logger;

    public FinanceAnalyticsStartupRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<FinanceAnalyticsStartupRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
