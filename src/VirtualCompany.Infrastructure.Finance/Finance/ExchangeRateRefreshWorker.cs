using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ExchangeRateRefreshOptions
{
    public const string SectionName = "ExchangeRates:Refresh";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int ClaimBatchSize { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 180;
    public int MaximumAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaximumRetryDelaySeconds { get; set; } = 1800;
}

public sealed class ExchangeRateRefreshRunner(
    VirtualCompanyDbContext db,
    IExchangeRateProviderRegistry providers,
    ExchangeRateService service,
    IAuditEventWriter audit,
    IOptions<ExchangeRateRefreshOptions> options,
    ExchangeRateTelemetry telemetry,
    TimeProvider time,
    ILogger<ExchangeRateRefreshRunner> logger) : IExchangeRateRefreshRunner
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return 0;
        var now = Now();
        await PurgeExpiredEvidenceAsync(now, cancellationToken);
        await QueueScheduledAsync(now, cancellationToken);
        var candidates = await db.ExchangeRateRefreshJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == ExchangeRateRefreshJobStatuses.Queued ||
                         x.Status == ExchangeRateRefreshJobStatuses.RetryScheduled ||
                         x.Status == ExchangeRateRefreshJobStatuses.Running && x.LeaseExpiresUtc <= now) &&
                        (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
            .OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.CompanyId).ThenBy(x => x.Id)
            .Select(x => new { x.CompanyId, x.Id })
            .Take(Math.Clamp(options.Value.ClaimBatchSize, 1, 100)).ToArrayAsync(cancellationToken);
        var handled = 0;
        foreach (var candidate in candidates)
        {
            var claim = await ClaimAsync(candidate.CompanyId, candidate.Id, cancellationToken);
            if (claim is null) continue;
            handled++;
            try { await ProcessAsync(claim, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ExchangeRateProviderException exception)
            {
                await HandleFailureAsync(claim, exception.ReasonCode, exception.SafeMessage,
                    exception.IsTransient, exception.RetryAfter, cancellationToken);
            }
            catch (ExchangeRateOperationException exception)
            {
                await HandleFailureAsync(claim, exception.ReasonCode, exception.SafeMessage,
                    false, null, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Exchange-rate refresh job {JobId} failed unexpectedly.", claim.JobId);
                await HandleFailureAsync(claim, ExchangeRateReasonCodes.ProviderFailure,
                    "The exchange-rate refresh failed unexpectedly and can be retried.", true, null, cancellationToken);
            }
        }
        return handled;
    }

    private async Task QueueScheduledAsync(DateTime now, CancellationToken cancellationToken)
    {
        var sources = await db.ExchangeRateSources.IgnoreQueryFilters()
            .Where(x => x.SourceKind == ExchangeRateSourceKinds.Provider && x.IsEnabled &&
                        (x.NextRefreshUtc == null || x.NextRefreshUtc <= now))
            .OrderBy(x => x.NextRefreshUtc).ThenBy(x => x.CompanyId)
            .Take(Math.Clamp(options.Value.ClaimBatchSize, 1, 100)).ToArrayAsync(cancellationToken);
        foreach (var source in sources)
        {
            var descriptor = providers.GetRequired(source.SourceKey).Descriptor;
            var currencies = await db.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == source.CompanyId && x.IsEnabled && x.Code != descriptor.BaseCurrency)
                .OrderBy(x => x.Code).Select(x => x.Code).Take(100).ToArrayAsync(cancellationToken);
            if (currencies.Length == 0) currencies = descriptor.DefaultCurrencies.OrderBy(x => x).ToArray();
            var requestedDate = DateOnly.FromDateTime(now);
            var key = $"scheduled:{source.Id:N}:{requestedDate:yyyyMMdd}";
            if (await db.ExchangeRateRefreshJobs.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == source.CompanyId && x.IdempotencyKey == key, cancellationToken))
                continue;
            db.ExchangeRateRefreshJobs.Add(new ExchangeRateRefreshJob(Guid.NewGuid(), source.CompanyId,
                source.Id, key, requestedDate, string.Join(',', currencies), null,
                $"scheduled:{requestedDate:yyyyMMdd}", now));
        }
        if (sources.Length > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Claim?> ClaimAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var job = await db.ExchangeRateRefreshJobs.IgnoreQueryFilters()
                .Include(x => x.Source).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == jobId,
                    cancellationToken);
            if (job is null) return null;
            var owner = $"exchange-rates:{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!job.TryClaim(owner, now, TimeSpan.FromSeconds(Math.Clamp(options.Value.LeaseSeconds, 30, 900)))) return null;
            job.Source.RecordAttempt(now);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            telemetry.Refresh(job.Source.SourceKey, "claimed", null);
            return new Claim(companyId, job.Id, job.SourceId, owner);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task ProcessAsync(Claim claim, CancellationToken cancellationToken)
    {
        var job = await db.ExchangeRateRefreshJobs.IgnoreQueryFilters().Include(x => x.Source)
            .SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.JobId, cancellationToken);
        if (!job.IsClaimedBy(claim.Owner, Now())) return;
        var provider = providers.GetRequired(job.Source.SourceKey);
        var request = new ExchangeRateProviderRequest(job.CompanyId, job.RequestedDate,
            job.RequestedCurrencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            job.CorrelationId ?? $"exchange-rate-refresh:{job.Id:N}");
        var response = await provider.FetchAsync(request, cancellationToken);
        var set = await service.ImportProviderResponseAsync(job, job.Source, response, cancellationToken);
        if (!job.IsClaimedBy(claim.Owner, Now())) return;
        job.Complete(claim.Owner, set.Id, Now());
        job.Source.RecordSuccess(Now());
        await audit.WriteAsync(new AuditEventWriteRequest(job.CompanyId,
            job.RequestedByUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System,
            job.RequestedByUserId, "exchange_rate.refresh_completed", "exchange_rate_refresh_job",
            job.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            "The durable exchange-rate provider refresh completed.", [job.Source.SourceKey],
            new Dictionary<string, string?> { ["rateSetId"] = set.Id.ToString("D"), ["sourceKey"] = job.Source.SourceKey },
            job.CorrelationId, Now()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Refresh(job.Source.SourceKey, ExchangeRateRefreshJobStatuses.Completed, null);
    }

    private async Task HandleFailureAsync(Claim claim, string reasonCode, string summary,
        bool transient, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var job = await db.ExchangeRateRefreshJobs.IgnoreQueryFilters().Include(x => x.Source)
            .SingleOrDefaultAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.JobId, cancellationToken);
        if (job is null || !job.IsClaimedBy(claim.Owner, Now())) return;
        var maxAttempts = Math.Clamp(options.Value.MaximumAttempts, 1, 20);
        var retry = transient && job.AttemptCount < maxAttempts;
        if (retry)
        {
            var seconds = Math.Min(Math.Clamp(options.Value.MaximumRetryDelaySeconds, 1, 86400),
                Math.Clamp(options.Value.BaseRetryDelaySeconds, 1, 3600) * Math.Pow(2, Math.Max(0, job.AttemptCount - 1)));
            var delay = retryAfter.HasValue && retryAfter.Value.TotalSeconds > seconds
                ? retryAfter.Value : TimeSpan.FromSeconds(seconds);
            job.Retry(claim.Owner, reasonCode, summary, Now(), delay);
            job.Source.RecordFailure(reasonCode, summary, Now(), delay);
        }
        else
        {
            job.Fail(claim.Owner, reasonCode, summary, Now());
            job.Source.RecordFailure(reasonCode, summary, Now(), TimeSpan.FromHours(job.Source.RefreshIntervalHours));
        }
        await audit.WriteAsync(new AuditEventWriteRequest(job.CompanyId,
            job.RequestedByUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System,
            job.RequestedByUserId, retry ? "exchange_rate.refresh_retry_scheduled" : "exchange_rate.refresh_failed",
            "exchange_rate_refresh_job", job.Id.ToString("D"),
            retry ? AuditEventOutcomes.Pending : AuditEventOutcomes.Failed, summary,
            [job.Source.SourceKey], new Dictionary<string, string?>
            {
                ["reasonCode"] = reasonCode, ["attemptCount"] = job.AttemptCount.ToString(),
                ["sourceKey"] = job.Source.SourceKey
            }, job.CorrelationId, Now()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Refresh(job.Source.SourceKey, job.Status, reasonCode);
    }

    private async Task PurgeExpiredEvidenceAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expired = await db.ExchangeRateEvidence.IgnoreQueryFilters()
            .Where(x => x.RetentionExpiresUtc <= now && x.ProtectedPayload != ExchangeRateEvidence.ExpiredPayloadMarker)
            .OrderBy(x => x.RetentionExpiresUtc).Take(100).ToArrayAsync(cancellationToken);
        foreach (var evidence in expired) evidence.ExpireProtectedPayload(now);
        if (expired.Length > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
    private sealed record Claim(Guid CompanyId, Guid JobId, Guid SourceId, string Owner);
}

public sealed class ExchangeRateRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExchangeRateRefreshOptions> options,
    ILogger<ExchangeRateRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IExchangeRateRefreshRunner>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "The exchange-rate refresh worker cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 10, 3600)), stoppingToken);
        }
    }
}
