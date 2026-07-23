using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceInsightsSnapshotWorkerOptions
{
    public const string SectionName = "FinanceInsightsSnapshotWorker";

    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int BatchSize { get; set; } = 10;
    public int ClaimTimeoutSeconds { get; set; } = 300;
}

internal readonly record struct FinanceInsightSnapshotExecutionDescriptor(
    string SnapshotKey,
    DateTime? AsOfUtc,
    int ExpenseWindowDays,
    int TrendWindowDays,
    int PayableWindowDays,
    int RetentionMinutes)
{
    public string ToStorageValue() =>
        string.Join(
            '|',
            FinanceInsightSnapshotKeys.Normalize(SnapshotKey),
            AsOfUtc?.ToString("yyyyMMdd") ?? "latest",
            ExpenseWindowDays,
            TrendWindowDays,
            PayableWindowDays,
            Math.Clamp(RetentionMinutes, 15, 60 * 24 * 7));

    public RefreshFinanceInsightsSnapshotCommand ToCommand(Guid companyId)
    {
        var asOfUtc = AsOfUtc.HasValue
            ? (DateTime?)DateTime.SpecifyKind(AsOfUtc.Value.Date, DateTimeKind.Utc)
            : null;
        return new RefreshFinanceInsightsSnapshotCommand(
            companyId,
            asOfUtc,
            ExpenseWindowDays,
            TrendWindowDays,
            PayableWindowDays,
            FinanceInsightSnapshotKeys.Normalize(SnapshotKey),
            TimeSpan.FromMinutes(Math.Clamp(RetentionMinutes, 15, 60 * 24 * 7)));
    }

    public static bool TryParse(string? value, out FinanceInsightSnapshotExecutionDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 6 ||
            !int.TryParse(parts[2], out var expenseWindowDays) ||
            !int.TryParse(parts[3], out var trendWindowDays) ||
            !int.TryParse(parts[4], out var payableWindowDays) ||
            !int.TryParse(parts[5], out var retentionMinutes))
        {
            return false;
        }

        DateTime? asOfUtc = null;
        if (!string.Equals(parts[1], "latest", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedAsOf))
            {
                return false;
            }

            asOfUtc = DateTime.SpecifyKind(parsedAsOf.Date, DateTimeKind.Utc);
        }

        descriptor = new FinanceInsightSnapshotExecutionDescriptor(
            parts[0],
            asOfUtc,
            expenseWindowDays,
            trendWindowDays,
            payableWindowDays,
            retentionMinutes);
        return true;
    }
}

public sealed class FinanceInsightsSnapshotJobRunner : IFinanceInsightsSnapshotJobRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceReadService _financeReadService;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IOptions<FinanceInsightsSnapshotWorkerOptions> _options;
    private readonly ILogger<FinanceInsightsSnapshotJobRunner> _logger;

    public FinanceInsightsSnapshotJobRunner(
        VirtualCompanyDbContext dbContext,
        IFinanceReadService financeReadService,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IOptions<FinanceInsightsSnapshotWorkerOptions> options,
        ILogger<FinanceInsightsSnapshotJobRunner> logger)
    {
        _dbContext = dbContext;
        _financeReadService = financeReadService;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var executions = await ClaimDueExecutionsAsync(cancellationToken);
        var handled = 0;

        foreach (var execution in executions)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(execution.CompanyId);
            execution.StartAttempt(execution.CorrelationId, execution.AttemptCount + 1, execution.MaxAttempts);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!FinanceInsightSnapshotExecutionDescriptor.TryParse(execution.RelatedEntityId, out var descriptor))
            {
                execution.MarkFailed(
                    BackgroundExecutionFailureCategory.Validation,
                    "invalid_snapshot_descriptor",
                    "Finance insight snapshot execution does not contain a valid descriptor.");
                await _dbContext.SaveChangesAsync(cancellationToken);
                handled++;
                continue;
            }

            try
            {
                await _financeReadService.RefreshInsightsSnapshotAsync(descriptor.ToCommand(execution.CompanyId), cancellationToken);
                execution.MarkSucceeded();
                await _dbContext.SaveChangesAsync(cancellationToken);
                handled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finance insight snapshot refresh failed for company {CompanyId}.", execution.CompanyId);
                execution.MarkFailed(
                    BackgroundExecutionFailureCategory.TransientInfrastructure,
                    ex.GetType().Name,
                    string.IsNullOrWhiteSpace(ex.Message) ? "Finance insight snapshot refresh failed." : ex.Message);
                await _dbContext.SaveChangesAsync(cancellationToken);
                handled++;
            }
        }

        return handled;
    }

    private async Task<IReadOnlyList<BackgroundExecution>> ClaimDueExecutionsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var staleBeforeUtc = nowUtc.Subtract(TimeSpan.FromSeconds(Math.Max(30, _options.Value.ClaimTimeoutSeconds)));
        var candidateIds = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh &&
                (((x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled) &&
                  (x.NextRetryUtc == null || x.NextRetryUtc <= nowUtc)) ||
                 (x.Status == BackgroundExecutionStatus.InProgress &&
                  x.HeartbeatUtc != null &&
                  x.HeartbeatUtc <= staleBeforeUtc)))
            .OrderBy(x => x.NextRetryUtc ?? x.CreatedUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(Math.Max(1, _options.Value.BatchSize))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
        {
            return [];
        }

        await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x =>
                candidateIds.Contains(x.Id) &&
                x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh &&
                (((x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled) &&
                  (x.NextRetryUtc == null || x.NextRetryUtc <= nowUtc)) ||
                 (x.Status == BackgroundExecutionStatus.InProgress &&
                  x.HeartbeatUtc != null &&
                  x.HeartbeatUtc <= staleBeforeUtc)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, BackgroundExecutionStatus.InProgress)
                    .SetProperty(x => x.StartedUtc, nowUtc)
                    .SetProperty(x => x.HeartbeatUtc, nowUtc)
                    .SetProperty(x => x.NextRetryUtc, (DateTime?)null)
                    .SetProperty(x => x.UpdatedUtc, nowUtc),
                cancellationToken);

        return await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x =>
                candidateIds.Contains(x.Id) &&
                x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh &&
                x.Status == BackgroundExecutionStatus.InProgress &&
                x.StartedUtc == nowUtc)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);
    }
}

public sealed class FinanceInsightsSnapshotBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FinanceInsightsSnapshotWorkerOptions> _options;
    private readonly ILogger<FinanceInsightsSnapshotBackgroundService> _logger;

    public FinanceInsightsSnapshotBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceInsightsSnapshotWorkerOptions> options,
        ILogger<FinanceInsightsSnapshotBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Finance insight snapshot worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IFinanceInsightsSnapshotJobRunner>();
                var handled = await runner.RunDueAsync(stoppingToken);
                if (handled == 0)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finance insight snapshot worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
