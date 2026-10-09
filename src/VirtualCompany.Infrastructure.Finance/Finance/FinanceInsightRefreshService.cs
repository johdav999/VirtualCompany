using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using static VirtualCompany.Infrastructure.Finance.CompanyFinanceReadService;

namespace VirtualCompany.Infrastructure.Finance;

/// <summary>Owns reconciliation, snapshot publication and durable refresh requests.</summary>
public sealed class FinanceInsightRefreshService(
    VirtualCompanyDbContext dbContext,
    CompanyFinanceReadService reader,
    IFinanceInsightPersistenceService persistence,
    IDistributedCache? insightSnapshotCache = null,
    TimeProvider? timeProvider = null) : IFinanceInsightRefreshService
{
    private static readonly JsonSerializerOptions InsightSnapshotSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _dbContext = dbContext;
    private readonly CompanyFinanceReadService _reader = reader;
    private readonly IFinanceInsightPersistenceService _persistence = persistence;
    private readonly IDistributedCache? _insightSnapshotCache = insightSnapshotCache;
    private DateTime GetUtcNow() => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<FinanceInsightsSnapshotRefreshResultDto> RefreshInsightsSnapshotAsync(
        RefreshFinanceInsightsSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        await _reader.EnsureInsightAccessAsync(command.CompanyId, cancellationToken);

        var parameters = _reader.NormalizeInsightsQuery(
            command.CompanyId,
            command.AsOfUtc,
            command.ExpenseWindowDays,
            command.TrendWindowDays,
            command.PayableWindowDays,
            command.SnapshotKey);

        var evaluation = await _reader.EvaluateInsightChecksAsync(parameters, cancellationToken);
        await _persistence.ReconcileAsync(evaluation.Context, evaluation.CheckCodes, evaluation.Results, cancellationToken);
        var items = await _persistence.ListAsync(command.CompanyId, null, null, true, cancellationToken);
        var refreshed = new FinanceInsightsDto(command.CompanyId, parameters.GeneratedAtUtc, false, null, items);
        var retention = NormalizeSnapshotRetention(command.Retention);
        var expiresAtUtc = GetUtcNow().Add(retention);

        if (_insightSnapshotCache is not null)
        {
            var payload = JsonSerializer.Serialize(
                new FinanceInsightsSnapshotCacheEnvelope(
                    parameters.SnapshotKey,
                    parameters.CacheKey,
                    expiresAtUtc,
                    refreshed with { SnapshotExpiresAtUtc = expiresAtUtc }),
                InsightSnapshotSerializerOptions);

            await _insightSnapshotCache.SetStringAsync(
                parameters.CacheKey,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = retention
                },
                cancellationToken);
        }

        return new FinanceInsightsSnapshotRefreshResultDto(
            command.CompanyId,
            parameters.SnapshotKey,
            parameters.CacheKey,
            GetUtcNow(),
            $"finance-insights-refresh:{command.CompanyId:N}:{parameters.SnapshotKey}",
            Queued: false,
            Refreshed: true,
            expiresAtUtc,
            refreshed with { SnapshotExpiresAtUtc = expiresAtUtc });
    }

    public async Task<FinanceInsightsSnapshotRefreshResultDto> QueueInsightsSnapshotRefreshAsync(
        QueueFinanceInsightsSnapshotRefreshCommand command,
        CancellationToken cancellationToken)
    {
        await _reader.EnsureInsightAccessAsync(command.CompanyId, cancellationToken);

        var parameters = _reader.NormalizeInsightsQuery(
            command.CompanyId,
            command.AsOfUtc,
            command.ExpenseWindowDays,
            command.TrendWindowDays,
            command.PayableWindowDays,
            command.SnapshotKey);

        var descriptor = new FinanceInsightSnapshotExecutionDescriptor(
            parameters.SnapshotKey,
            command.AsOfUtc?.Date,
            parameters.ExpenseWindowDays,
            parameters.TrendWindowDays,
            parameters.PayableWindowDays,
            Math.Clamp(command.RetentionMinutes, 15, 60 * 24 * 7));
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? $"finance-insights-refresh:{command.CompanyId:N}:{descriptor.ToStorageValue()}"
            : command.CorrelationId.Trim();
        var idempotencyKey = $"finance-insights:{command.CompanyId:N}:{descriptor.ToStorageValue()}";
        var utcNow = GetUtcNow();

        var execution = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId &&
                     x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh &&
                     x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (execution is null)
        {
            execution = new BackgroundExecution(
                Guid.NewGuid(),
                command.CompanyId,
                BackgroundExecutionType.FinanceInsightRefresh,
                BackgroundExecutionRelatedEntityTypes.FinanceInsightSnapshot,
                descriptor.ToStorageValue(),
                correlationId,
                idempotencyKey,
                maxAttempts: 3);
            _dbContext.BackgroundExecutions.Add(execution);
        }
        else if (command.ResetAttempts || execution.IsTerminal)
        {
            execution.Queue(utcNow, correlationId, resetAttempts: command.ResetAttempts);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceInsightsSnapshotRefreshResultDto(
            command.CompanyId,
            parameters.SnapshotKey,
            parameters.CacheKey,
            utcNow,
            correlationId,
            Queued: true,
            Refreshed: false,
            ExpiresAtUtc: null,
            Insights: null);
    }

}
