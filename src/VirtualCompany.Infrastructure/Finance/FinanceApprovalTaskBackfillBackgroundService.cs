using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceApprovalTaskBackfillWorkerOptions
{
    public const string SectionName = "FinanceApprovalTaskBackfillWorker";

    public bool Enabled { get; set; } = false;
    public int PollIntervalMilliseconds { get; set; } = 600000;
    public int BatchSize { get; set; } = 25;
    public int BackfillBatchSize { get; set; } = 250;
}

public sealed class FinanceApprovalTaskBackfillJobRunner : IFinanceApprovalTaskBackfillJobRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceApprovalTaskService _approvalTaskService;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IOptions<FinanceApprovalTaskBackfillWorkerOptions> _options;
    private readonly ILogger<FinanceApprovalTaskBackfillJobRunner> _logger;

    public FinanceApprovalTaskBackfillJobRunner(
        VirtualCompanyDbContext dbContext,
        IFinanceApprovalTaskService approvalTaskService,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IOptions<FinanceApprovalTaskBackfillWorkerOptions> options,
        ILogger<FinanceApprovalTaskBackfillJobRunner> logger)
    {
        _dbContext = dbContext;
        _approvalTaskService = approvalTaskService;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var companyIds = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                _dbContext.FinanceBills.IgnoreQueryFilters().Any(bill => bill.CompanyId == x.Id) ||
                _dbContext.Payments.IgnoreQueryFilters().Any(payment => payment.CompanyId == x.Id))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(Math.Max(1, _options.Value.BatchSize))
            .ToArrayAsync(cancellationToken);

        var handled = 0;
        foreach (var companyId in companyIds)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(companyId);
            var result = await _approvalTaskService.BackfillApprovalTasksAsync(
                new BackfillFinanceApprovalTasksCommand(
                    companyId,
                    _options.Value.BackfillBatchSize,
                    $"finance-approval-backfill:{companyId:N}",
                    IncludePayments: true),
                cancellationToken);

            handled++;
            _logger.LogInformation(
                "Processed finance approval task backfill job for company {CompanyId}. Created={CreatedCount} SkippedExisting={SkippedExistingCount}.",
                companyId,
                result.CreatedCount,
                result.SkippedExistingCount);
        }

        return handled;
    }
}

public sealed class FinanceApprovalTaskBackfillBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FinanceApprovalTaskBackfillWorkerOptions> _options;
    private readonly ILogger<FinanceApprovalTaskBackfillBackgroundService> _logger;

    public FinanceApprovalTaskBackfillBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceApprovalTaskBackfillWorkerOptions> options,
        ILogger<FinanceApprovalTaskBackfillBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Value.Enabled)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds)), stoppingToken);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IFinanceApprovalTaskBackfillJobRunner>();
                await runner.RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finance approval task backfill background service failed.");
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds)),
                stoppingToken);
        }
    }
}
