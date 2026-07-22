using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SalesAnalyticsDashboardEndpointTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

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
        await analytics.RecordMessagePerformanceEventAsync(Command(seed, "tenant-a-secondary", "variant-c", ConversionAnalyticsEventType.Sent, campaignId: seed.SecondCompanyACampaignId), CancellationToken.None);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "sales-analytics-owner");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "sales-analytics-owner@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Sales Analytics Owner");
        client.DefaultRequestHeaders.Add("X-Company-Id", seed.CompanyAId.ToString("D"));
        var response = await client.GetFromJsonAsync<SalesAnalyticsDashboardResponse>("/api/sales/analytics");

        Assert.NotNull(response);
        Assert.Equal(seed.CompanyAId, response!.CompanyId);
        Assert.Equal(3, response.Funnel.Sent);
        Assert.Equal(1, response.Funnel.Replied);
        Assert.Contains(response.Variants, x => x.VariantKey == "variant-a" && x.Counts.Replied == 1);
        Assert.DoesNotContain(response.Campaigns, x => x.CampaignId == seed.CompanyBCampaignId);
        Assert.Equal(seed.CampaignId, response.Campaigns[0].CampaignId);
        Assert.Equal(2, response.Campaigns[0].Counts.Sent);
        Assert.Equal(seed.SecondCompanyACampaignId, response.Campaigns[1].CampaignId);
        Assert.Equal(1, response.Campaigns[1].Counts.Sent);
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
            Guid.NewGuid(),
            new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc));

        await _factory.SeedAsync(dbContext =>
        {
            var ownerId = Guid.NewGuid();
            dbContext.Users.Add(new User(ownerId, "sales-analytics-owner@example.com", "Sales Analytics Owner", "dev-header", "sales-analytics-owner"));
            dbContext.Companies.Add(new Company(seed.CompanyAId, "Analytics API A"));
            dbContext.Companies.Add(new Company(seed.CompanyBId, "Analytics API B"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), seed.CompanyAId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.CustomerCompanies.Add(new CustomerCompany(seed.CustomerCompanyId, seed.CompanyAId, "Customer"));
            dbContext.Contacts.Add(new Contact(seed.ContactId, seed.CompanyAId, "Buyer", "buyer@example.com", seed.CustomerCompanyId));
            var sequence = new SalesSequence(seed.SequenceId, seed.CompanyAId, "Sequence");
            sequence.Steps.Add(new SalesSequenceStep(seed.SequenceStepId, seed.CompanyAId, seed.SequenceId, 1, 0, "Hello", templateSubject: "Hello"));
            dbContext.SalesSequences.Add(sequence);
            var otherTenantSequence = new SalesSequence(Guid.NewGuid(), seed.CompanyBId, "Other tenant sequence");
            dbContext.SalesSequences.Add(otherTenantSequence);
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.CampaignId, seed.CompanyAId, seed.SequenceId, "Campaign", "contacts"));
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.SecondCompanyACampaignId, seed.CompanyAId, seed.SequenceId, "Secondary campaign", "contacts"));
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.CompanyBCampaignId, seed.CompanyBId, otherTenantSequence.Id, "Other tenant campaign", "contacts"));
            return Task.CompletedTask;
        });

        return seed;
    }

    private static RecordMessagePerformanceEventCommand Command(Seed seed, string messageKey, string variantKey, ConversionAnalyticsEventType eventType, int hours = 0, Guid? campaignId = null) =>
        new(
            seed.CompanyAId,
            messageKey,
            seed.ContactId,
            eventType,
            seed.BaseUtc.AddHours(hours),
            CampaignId: campaignId ?? seed.CampaignId,
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
        Guid SecondCompanyACampaignId,
        Guid CompanyBCampaignId,
        Guid DealId,
        DateTime BaseUtc);

    private sealed record SalesAnalyticsDashboardResponse(Guid CompanyId, PerformanceFunnelCounts Funnel, PerformanceFunnelRates Rates, IReadOnlyList<CampaignPerformanceListItemDto> Campaigns, IReadOnlyList<VariantPerformanceSummaryDto> Variants);
}
