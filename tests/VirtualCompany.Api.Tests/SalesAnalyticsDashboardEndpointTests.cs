using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SalesAnalyticsDashboardEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SalesAnalyticsDashboardEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_analytics_are_tenant_scoped_and_include_variants()
    {
        var seed = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(seed.CompanyAId);
        var analytics = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        await analytics.RecordMessagePerformanceEventAsync(Command(seed, "tenant-a-1", "variant-a", ConversionAnalyticsEventType.Sent), CancellationToken.None);
        await analytics.RecordMessagePerformanceEventAsync(Command(seed, "tenant-a-1", "variant-a", ConversionAnalyticsEventType.Replied, 1), CancellationToken.None);
        await analytics.RecordMessagePerformanceEventAsync(Command(seed, "tenant-a-2", "variant-b", ConversionAnalyticsEventType.Sent), CancellationToken.None);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", seed.CompanyAId.ToString("D"));
        var response = await client.GetFromJsonAsync<SalesAnalyticsDashboardResponse>("/api/sales/analytics");

        Assert.NotNull(response);
        Assert.Equal(seed.CompanyAId, response!.CompanyId);
        Assert.Equal(2, response.Funnel.Sent);
        Assert.Equal(1, response.Funnel.Replied);
        Assert.Contains(response.Variants, x => x.VariantKey == "variant-a" && x.Counts.Replied == 1);
        Assert.DoesNotContain(response.Campaigns, x => x.CampaignId == seed.CompanyBCampaignId);
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc));

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(seed.CompanyAId, "Analytics API A"));
            dbContext.Companies.Add(new Company(seed.CompanyBId, "Analytics API B"));
            dbContext.CustomerCompanies.Add(new CustomerCompany(seed.CustomerCompanyId, seed.CompanyAId, "Customer"));
            dbContext.Contacts.Add(new Contact(seed.ContactId, seed.CompanyAId, "Buyer", "buyer@example.com", seed.CustomerCompanyId));
            var sequence = new SalesSequence(seed.SequenceId, seed.CompanyAId, "Sequence");
            sequence.Steps.Add(new SalesSequenceStep(seed.SequenceStepId, seed.CompanyAId, seed.SequenceId, 1, 0, "Hello", templateSubject: "Hello"));
            dbContext.SalesSequences.Add(sequence);
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.CampaignId, seed.CompanyAId, seed.SequenceId, "Campaign", "contacts"));
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.CompanyBCampaignId, seed.CompanyBId, null, "Other tenant campaign", "contacts"));
            return Task.CompletedTask;
        });

        return seed;
    }

    private static RecordMessagePerformanceEventCommand Command(Seed seed, string messageKey, string variantKey, ConversionAnalyticsEventType eventType, int hours = 0) =>
        new(
            seed.CompanyAId,
            messageKey,
            seed.ContactId,
            eventType,
            seed.BaseUtc.AddHours(hours),
            CampaignId: seed.CampaignId,
            SequenceId: seed.SequenceId,
            SequenceStepId: seed.SequenceStepId,
            VariantKey: variantKey,
            StepOrder: 1);

    private sealed record Seed(
        Guid CompanyAId,
        Guid CompanyBId,
        Guid CustomerCompanyId,
        Guid ContactId,
        Guid SequenceId,
        Guid SequenceStepId,
        Guid CampaignId,
        Guid CompanyBCampaignId,
        Guid DealId,
        DateTime BaseUtc);

    private sealed record SalesAnalyticsDashboardResponse(Guid CompanyId, PerformanceFunnelCounts Funnel, PerformanceFunnelRates Rates, IReadOnlyList<CampaignPerformanceListItemDto> Campaigns, IReadOnlyList<VariantPerformanceSummaryDto> Variants);
}
