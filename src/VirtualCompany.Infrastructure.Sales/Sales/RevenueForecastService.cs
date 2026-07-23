using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class PipelineRiskScoringWorkerOptions
{
    public const string SectionName = "PipelineRiskScoringWorker";

    public bool Enabled { get; set; } = true;
    public int RunIntervalHours { get; set; } = 24;
}

public sealed class RevenueForecastService : IRevenueForecastService, IPipelineRiskScoringJobRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RevenueForecastService> _logger;

    public RevenueForecastService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<RevenueForecastService> logger,
        ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<RevenueForecastSnapshotDto> CalculateAndPersistForecastAsync(Guid companyId, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        asOfUtc = NormalizeUtc(asOfUtc);
        var calculatedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var deals = await _dbContext.Deals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                !x.IsDeleted &&
                x.Status == SalesStatuses.Open &&
                x.Amount > 0m &&
                x.ExpectedCloseUtc != null &&
                x.ExpectedCloseUtc >= asOfUtc &&
                x.ExpectedCloseUtc <= asOfUtc.AddDays(90))
            .Select(x => new ForecastDealInput(
                x.Id,
                x.Amount,
                x.Currency,
                x.PipelineStageId,
                x.ExpectedCloseUtc!.Value))
            .ToListAsync(cancellationToken);

        var latestRiskScores = await LoadLatestRiskScoresAsync(companyId, deals.Select(x => x.DealId).ToArray(), cancellationToken);
        var windows = RevenueForecastWindows.SupportedDays
            .Select(days => CalculateWindow(days, asOfUtc, deals, latestRiskScores))
            .ToArray();
        var currency = deals.Select(x => x.Currency).FirstOrDefault() ?? "USD";
        var risk = BuildRiskDistribution(latestRiskScores.Values);

        var snapshotDate = asOfUtc.Date;
        var existing = await _dbContext.RevenueForecastSnapshots
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.AsOfUtc == snapshotDate, cancellationToken);

        if (existing is not null)
        {
            _dbContext.RevenueForecastSnapshots.Remove(existing);
        }

        var snapshot = new RevenueForecastSnapshot(
            Guid.NewGuid(),
            companyId,
            snapshotDate,
            currency,
            windows[0].GrossPipelineValue,
            windows[0].ExpectedRevenue,
            windows[0].DealCount,
            windows[1].GrossPipelineValue,
            windows[1].ExpectedRevenue,
            windows[1].DealCount,
            windows[2].GrossPipelineValue,
            windows[2].ExpectedRevenue,
            windows[2].DealCount,
            risk.Unknown,
            risk.Low,
            risk.Medium,
            risk.High,
            calculatedUtc);
        _dbContext.RevenueForecastSnapshots.Add(snapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(snapshot);
    }

    public async Task<RevenueForecastSnapshotDto?> GetLatestForecastAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var snapshot = await _dbContext.RevenueForecastSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.AsOfUtc)
            .ThenByDescending(x => x.CalculatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot is null ? null : Map(snapshot);
    }

    public async Task<DealRiskScoreDto?> GetLatestDealRiskScoreAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var snapshot = await _dbContext.DealRiskScoreSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == dealId)
            .OrderByDescending(x => x.ScoreDateUtc)
            .ThenByDescending(x => x.CalculatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot is null ? null : Map(snapshot);
    }

    public async Task<PipelineRiskScoringRunResult> RunDailyAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        asOfUtc = NormalizeUtc(asOfUtc);
        var companyIds = await _dbContext.Deals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == SalesStatuses.Open)
            .Select(x => x.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var processedDeals = 0;
        var snapshots = 0;

        foreach (var companyId in companyIds)
        {
            processedDeals += await RecalculateCompanyRiskScoresAsync(companyId, asOfUtc, cancellationToken);
            await CalculateAndPersistForecastAsync(companyId, asOfUtc, cancellationToken);
            snapshots++;
        }

        _logger.LogInformation(
            "Pipeline risk scoring completed for {CompanyCount} companies and {DealCount} active deals.",
            companyIds.Count,
            processedDeals);

        return new PipelineRiskScoringRunResult(companyIds.Count, processedDeals, snapshots);
    }

    private async Task<int> RecalculateCompanyRiskScoresAsync(Guid companyId, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        var deals = await _dbContext.Deals
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Open)
            .ToListAsync(cancellationToken);
        var scoreDate = asOfUtc.Date;
        var calculatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var processed = 0;

        foreach (var deal in deals)
        {
            var score = await ScoreDealAsync(companyId, deal, asOfUtc, cancellationToken);
            var existing = await _dbContext.DealRiskScoreSnapshots
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DealId == deal.Id && x.ScoreDateUtc == scoreDate, cancellationToken);

            if (existing is null)
            {
                _dbContext.DealRiskScoreSnapshots.Add(new DealRiskScoreSnapshot(
                    Guid.NewGuid(),
                    companyId,
                    deal.Id,
                    scoreDate,
                    score.Score,
                    score.Band,
                    score.FactorsSummary,
                    calculatedUtc));
            }
            else
            {
                existing.Recalculate(score.Score, score.Band, score.FactorsSummary, calculatedUtc);
            }

            processed++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private async Task<DealRiskScore> ScoreDealAsync(Guid companyId, Deal deal, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        var lastActivityUtc = await _dbContext.SalesActivities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id && !x.IsDeleted)
            .MaxAsync(x => (DateTime?)x.OccurredUtc, cancellationToken);
        var lastEmail = await _dbContext.SalesEmailLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new { x.CreatedUtc, x.DetectedIntent, x.ProductOrServiceInterest, x.Confidence })
            .FirstOrDefaultAsync(cancellationToken);
        var recommendations = await _dbContext.SalesAgentRecommendations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(5)
            .Select(x => new { x.RiskLevel, x.TriggerCondition, x.CreatedUtc })
            .ToListAsync(cancellationToken);
        var intelligenceSignals = await _dbContext.DealIntelligenceSignals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id)
            .OrderByDescending(x => x.DetectedUtc)
            .Take(10)
            .Select(x => new
            {
                x.SignalType,
                x.ConfidenceScore
            })
            .ToListAsync(cancellationToken);

        var score = 0.20m;
        var factors = new List<string>();
        var quietSinceUtc = lastActivityUtc ?? lastEmail?.CreatedUtc ?? deal.UpdatedUtc;
        var quietDays = Math.Max(0, (asOfUtc.Date - quietSinceUtc.Date).Days);

        if (quietDays >= 21)
        {
            score += 0.35m;
            factors.Add("no activity for three weeks");
        }
        else if (quietDays >= 7)
        {
            score += 0.18m;
            factors.Add("quiet for a week");
        }

        if (deal.ExpectedCloseUtc.HasValue)
        {
            var closeDays = (deal.ExpectedCloseUtc.Value.Date - asOfUtc.Date).Days;
            if (closeDays < 0)
            {
                score += 0.25m;
                factors.Add("close date has slipped");
            }
            else if (closeDays <= 7 && quietDays >= 3)
            {
                score += 0.12m;
                factors.Add("near close date with limited activity");
            }
        }

        var stageAgeDays = Math.Max(0, (asOfUtc.Date - deal.UpdatedUtc.Date).Days);
        if (stageAgeDays >= 30)
        {
            score += 0.15m;
            factors.Add("stage has not changed this month");
        }

        if (recommendations.Any(x => string.Equals(x.RiskLevel, "high", StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.15m;
            factors.Add("Alex has flagged high-risk follow-up");
        }

        var signalText = $"{lastEmail?.DetectedIntent} {lastEmail?.ProductOrServiceInterest}".ToLowerInvariant();
        if (signalText.Contains("price", StringComparison.OrdinalIgnoreCase) ||
            signalText.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
            signalText.Contains("expensive", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.12m;
            factors.Add("pricing concern detected");
        }

        if (intelligenceSignals.Any(x => x.SignalType == DealIntelligenceSignalTypes.Ghosting && x.ConfidenceScore >= 0.70m))
        {
            score += 0.22m;
            factors.Add("customer has gone quiet");
        }

        if (intelligenceSignals.Any(x => x.SignalType == DealIntelligenceSignalTypes.PriceResistance && x.ConfidenceScore >= 0.65m))
        {
            score += 0.14m;
            factors.Add("price resistance detected");
        }

        if (intelligenceSignals.Any(x => x.SignalType == DealIntelligenceSignalTypes.BuyingIntent && x.ConfidenceScore >= 0.65m))
        {
            score -= 0.16m;
            factors.Add("buying signal detected");
        }

        if (lastEmail?.Confidence >= 0.75m &&
            (signalText.Contains("buy", StringComparison.OrdinalIgnoreCase) ||
             signalText.Contains("proposal", StringComparison.OrdinalIgnoreCase) ||
             signalText.Contains("contract", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 0.12m;
            factors.Add("positive buying signal");
        }

        var bounded = Math.Round(Math.Clamp(score, 0m, 1m), 4, MidpointRounding.AwayFromZero);
        var band = bounded >= 0.67m ? DealRiskBands.High : bounded >= 0.34m ? DealRiskBands.Medium : DealRiskBands.Low;
        return new DealRiskScore(bounded, band, factors.Count == 0 ? "No material risk signals found." : string.Join("; ", factors));
    }

    private async Task<Dictionary<Guid, DealRiskScoreDto>> LoadLatestRiskScoresAsync(Guid companyId, IReadOnlyCollection<Guid> dealIds, CancellationToken cancellationToken)
    {
        if (dealIds.Count == 0)
        {
            return [];
        }

        var snapshots = await _dbContext.DealRiskScoreSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && dealIds.Contains(x.DealId))
            .OrderByDescending(x => x.ScoreDateUtc)
            .ThenByDescending(x => x.CalculatedUtc)
            .ToListAsync(cancellationToken);

        return snapshots
            .GroupBy(x => x.DealId)
            .ToDictionary(x => x.Key, x => Map(x.First()));
    }

    private static RevenueForecastWindowDto CalculateWindow(int days, DateTime asOfUtc, IReadOnlyList<ForecastDealInput> deals, IReadOnlyDictionary<Guid, DealRiskScoreDto> riskScores)
    {
        var windowEnd = asOfUtc.AddDays(days);
        var included = deals.Where(x => x.ExpectedCloseUtc <= windowEnd).ToList();
        var gross = included.Sum(x => x.Amount);
        var expected = included.Sum(x =>
        {
            var risk = riskScores.TryGetValue(x.DealId, out var score) ? score.Score : 0.50m;
            // Forecast is expected deal value: stage likelihood dampened by the latest pipeline risk score.
            return x.Amount * StageProbability(x.PipelineStageId) * (1m - (risk * 0.50m));
        });

        return new RevenueForecastWindowDto(days, Math.Round(gross, 2), Math.Round(expected, 2), included.Count);
    }

    private static decimal StageProbability(Guid stageId) =>
        stageId == SalesPipelineStage.ProposalStageId ? 0.70m :
        stageId == SalesPipelineStage.QualifiedStageId ? 0.45m :
        stageId == SalesPipelineStage.WonStageId ? 1m : 0.20m;

    private static RiskDistributionSummary BuildRiskDistribution(IEnumerable<DealRiskScoreDto> scores) =>
        new(
            0,
            scores.Count(x => x.Score < 0.34m),
            scores.Count(x => x.Score >= 0.34m && x.Score < 0.67m),
            scores.Count(x => x.Score >= 0.67m));

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new InvalidOperationException("The requested revenue forecast is outside the active company context.");
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static RevenueForecastSnapshotDto Map(RevenueForecastSnapshot snapshot) =>
        new(
            snapshot.Id,
            snapshot.CompanyId,
            snapshot.AsOfUtc,
            snapshot.CalculatedUtc,
            snapshot.Currency,
            [
                new RevenueForecastWindowDto(30, snapshot.GrossPipeline30Days, snapshot.ExpectedRevenue30Days, snapshot.DealCount30Days),
                new RevenueForecastWindowDto(60, snapshot.GrossPipeline60Days, snapshot.ExpectedRevenue60Days, snapshot.DealCount60Days),
                new RevenueForecastWindowDto(90, snapshot.GrossPipeline90Days, snapshot.ExpectedRevenue90Days, snapshot.DealCount90Days)
            ],
            new RiskDistributionSummary(snapshot.UnknownRiskDeals, snapshot.LowRiskDeals, snapshot.MediumRiskDeals, snapshot.HighRiskDeals));

    private static DealRiskScoreDto Map(DealRiskScoreSnapshot snapshot) =>
        new(snapshot.Id, snapshot.CompanyId, snapshot.DealId, snapshot.Score, snapshot.Band, snapshot.CalculatedUtc, snapshot.FactorsSummary);

    private sealed record ForecastDealInput(Guid DealId, decimal Amount, string Currency, Guid PipelineStageId, DateTime ExpectedCloseUtc);
    private sealed record DealRiskScore(decimal Score, string Band, string FactorsSummary);
}

public sealed class PipelineRiskScoringBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PipelineRiskScoringWorkerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PipelineRiskScoringBackgroundService> _logger;

    public PipelineRiskScoringBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PipelineRiskScoringWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<PipelineRiskScoringBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Pipeline risk scoring worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.Value.RunIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IPipelineRiskScoringJobRunner>();
                await runner.RunDailyAsync(_timeProvider.GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline risk scoring worker loop failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}