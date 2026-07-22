using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class RevenueForecastServiceTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Forecast_uses_active_deals_in_30_60_90_day_windows()
    {
        var seed = await SeedForecastAsync("forecast-windows");
        using var scope = CreateCompanyScope(seed.CompanyAId);
        var service = scope.ServiceProvider.GetRequiredService<IRevenueForecastService>();

        var result = await service.CalculateAndPersistForecastAsync(seed.CompanyAId, seed.AsOfUtc, CancellationToken.None);

        Assert.Equal(seed.CompanyAId, result.CompanyId);
        Assert.Equal(3, result.Windows.Single(x => x.Days == 30).DealCount);
        Assert.Equal(4, result.Windows.Single(x => x.Days == 60).DealCount);
        Assert.Equal(5, result.Windows.Single(x => x.Days == 90).DealCount);
        Assert.DoesNotContain(result.Windows, x => x.ExpectedRevenue <= 0);
    }

    [Fact]
    public async Task Higher_risk_reduces_expected_revenue()
    {
        var seed = await SeedForecastAsync("forecast-risk");
        using var scope = CreateCompanyScope(seed.CompanyAId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        dbContext.DealRiskScoreSnapshots.Add(new DealRiskScoreSnapshot(Guid.NewGuid(), seed.CompanyAId, seed.LowRiskDealId, seed.AsOfUtc, 0.10m, DealRiskBands.Low, "low risk", seed.AsOfUtc));
        dbContext.DealRiskScoreSnapshots.Add(new DealRiskScoreSnapshot(Guid.NewGuid(), seed.CompanyAId, seed.HighRiskDealId, seed.AsOfUtc, 0.90m, DealRiskBands.High, "high risk", seed.AsOfUtc));
        await dbContext.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IRevenueForecastService>();
        var result = await service.CalculateAndPersistForecastAsync(seed.CompanyAId, seed.AsOfUtc, CancellationToken.None);

        var thirty = result.Windows.Single(x => x.Days == 30);
        Assert.True(thirty.ExpectedRevenue < thirty.GrossPipelineValue);
        Assert.Equal(1, result.RiskDistribution.Low);
        Assert.Equal(1, result.RiskDistribution.High);
    }

    [Fact]
    public async Task Forecast_queries_are_tenant_scoped()
    {
        var seed = await SeedForecastAsync("forecast-tenant");
        using var scope = CreateCompanyScope(seed.CompanyAId);
        var service = scope.ServiceProvider.GetRequiredService<IRevenueForecastService>();

        await service.CalculateAndPersistForecastAsync(seed.CompanyAId, seed.AsOfUtc, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CalculateAndPersistForecastAsync(seed.CompanyBId, seed.AsOfUtc, CancellationToken.None));
    }

    [Fact]
    public async Task Daily_risk_job_updates_active_deals_only_and_is_idempotent_per_day()
    {
        var seed = await SeedForecastAsync("risk-job");
        using var scope = CreateCompanyScope(seed.CompanyAId);
        var runner = scope.ServiceProvider.GetRequiredService<IPipelineRiskScoringJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var first = await runner.RunDailyAsync(seed.AsOfUtc, CancellationToken.None);
        var second = await runner.RunDailyAsync(seed.AsOfUtc, CancellationToken.None);

        Assert.True(first.DealCount >= 5);
        Assert.True(second.DealCount >= 5);
        var scores = await dbContext.DealRiskScoreSnapshots
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyAId)
            .ToListAsync();
        Assert.Equal(scores.Select(x => x.DealId).Distinct().Count(), scores.Count);
        Assert.DoesNotContain(scores, x => x.DealId == seed.ClosedDealId);
        Assert.All(scores, x => Assert.InRange(x.Score, 0m, 1m));
    }

    [Fact]
    public async Task Sales_model_declares_forecast_and_risk_indexes()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        AssertIndex<RevenueForecastSnapshot>(dbContext, nameof(RevenueForecastSnapshot.CompanyId), nameof(RevenueForecastSnapshot.AsOfUtc));
        AssertIndex<RevenueForecastSnapshot>(dbContext, nameof(RevenueForecastSnapshot.CompanyId), nameof(RevenueForecastSnapshot.CalculatedUtc));
        AssertIndex<DealRiskScoreSnapshot>(dbContext, nameof(DealRiskScoreSnapshot.CompanyId), nameof(DealRiskScoreSnapshot.DealId), nameof(DealRiskScoreSnapshot.ScoreDateUtc));
        AssertIndex<DealRiskScoreSnapshot>(dbContext, nameof(DealRiskScoreSnapshot.CompanyId), nameof(DealRiskScoreSnapshot.Band), nameof(DealRiskScoreSnapshot.ScoreDateUtc));
    }

    private IServiceScope CreateCompanyScope(Guid companyId)
    {
        var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        return scope;
    }

    private async Task<ForecastSeed> SeedForecastAsync(string suffix)
    {
        var seed = new ForecastSeed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(seed.CompanyAId, $"Forecast A {suffix}"));
            dbContext.Companies.Add(new Company(seed.CompanyBId, $"Forecast B {suffix}"));
            dbContext.Deals.AddRange(
                new Deal(seed.LowRiskDealId, seed.CompanyAId, $"30 day low {suffix}", SalesPipelineStage.ProposalStageId, 10000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(10), updatedUtc: seed.AsOfUtc.AddDays(-1)),
                new Deal(seed.HighRiskDealId, seed.CompanyAId, $"30 day high {suffix}", SalesPipelineStage.ProposalStageId, 10000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(15), updatedUtc: seed.AsOfUtc.AddDays(-25)),
                new Deal(Guid.NewGuid(), seed.CompanyAId, $"30 day new {suffix}", SalesPipelineStage.NewStageId, 5000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(25)),
                new Deal(Guid.NewGuid(), seed.CompanyAId, $"60 day {suffix}", SalesPipelineStage.QualifiedStageId, 8000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(45)),
                new Deal(Guid.NewGuid(), seed.CompanyAId, $"90 day {suffix}", SalesPipelineStage.QualifiedStageId, 12000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(75)),
                new Deal(Guid.NewGuid(), seed.CompanyAId, $"too far {suffix}", SalesPipelineStage.QualifiedStageId, 9000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(120)),
                new Deal(seed.ClosedDealId, seed.CompanyAId, $"closed {suffix}", SalesPipelineStage.WonStageId, 30000m, "USD", SalesStatuses.Won, expectedCloseUtc: seed.AsOfUtc.AddDays(12)),
                new Deal(Guid.NewGuid(), seed.CompanyBId, $"other tenant {suffix}", SalesPipelineStage.ProposalStageId, 99000m, "USD", expectedCloseUtc: seed.AsOfUtc.AddDays(10)));
            dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), seed.CompanyAId, "reply", "Customer replied.", seed.AsOfUtc.AddDays(-1), dealId: seed.LowRiskDealId));
            return Task.CompletedTask;
        });

        return seed;
    }

    private static void AssertIndex<TEntity>(VirtualCompanyDbContext dbContext, params string[] propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private sealed record ForecastSeed(
        Guid CompanyAId,
        Guid CompanyBId,
        Guid LowRiskDealId,
        Guid HighRiskDealId,
        Guid ClosedDealId,
        Guid ContactId,
        Guid CustomerCompanyId,
        DateTime AsOfUtc);
}