using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ConversionAnalyticsServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConversionAnalyticsServiceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Recording_first_event_creates_message_performance_row()
    {
        var seed = await SeedCampaignAsync("analytics-create");
        using var scope = CreateCompanyScope(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        var recorded = await service.RecordMessagePerformanceEventAsync(
            Command(seed, "msg-create", ConversionAnalyticsEventType.Sent, seed.SentAt),
            CancellationToken.None);

        Assert.Equal(seed.CompanyId, recorded.CompanyId);
        Assert.Equal(seed.ContactId, recorded.ContactId);
        Assert.Equal(seed.CampaignId, recorded.CampaignId);
        Assert.Equal(seed.SequenceId, recorded.SequenceId);
        Assert.Equal(seed.SequenceStepId, recorded.SequenceStepId);
        Assert.Equal(seed.SentAt, recorded.SentAt);
    }

    [Fact]
    public async Task Duplicate_events_are_idempotent_and_keep_earliest_timestamp()
    {
        var seed = await SeedCampaignAsync("analytics-idempotent");
        using var scope = CreateCompanyScope(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-idempotent", ConversionAnalyticsEventType.Sent, seed.SentAt.AddMinutes(5)), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-idempotent", ConversionAnalyticsEventType.Sent, seed.SentAt), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-idempotent", ConversionAnalyticsEventType.Replied, seed.SentAt.AddHours(2)), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-idempotent", ConversionAnalyticsEventType.Replied, seed.SentAt.AddHours(3)), CancellationToken.None);

        var summary = await service.GetCampaignPerformanceAsync(seed.CompanyId, seed.CampaignId, CancellationToken.None);
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Counts.Sent);
        Assert.Equal(1, summary.Counts.Replied);

        var row = await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().SalesMessagePerformances.SingleAsync(x => x.MessageKey == "msg-idempotent");
        Assert.Equal(seed.SentAt, row.SentAt);
        Assert.Equal(seed.SentAt.AddHours(2), row.RepliedAt);
    }

    [Fact]
    public async Task Out_of_order_events_still_build_final_funnel_state()
    {
        var seed = await SeedCampaignAsync("analytics-out-of-order");
        using var scope = CreateCompanyScope(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-order", ConversionAnalyticsEventType.Replied, seed.SentAt.AddHours(4)), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-order", ConversionAnalyticsEventType.Delivered, seed.SentAt.AddMinutes(10)), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-order", ConversionAnalyticsEventType.Sent, seed.SentAt), CancellationToken.None);
        await service.RecordMessagePerformanceEventAsync(Command(seed, "msg-order", ConversionAnalyticsEventType.Opened, seed.SentAt.AddHours(1)), CancellationToken.None);

        var summary = await service.GetCampaignPerformanceAsync(seed.CompanyId, seed.CampaignId, CancellationToken.None);
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Counts.Sent);
        Assert.Equal(1, summary.Counts.Delivered);
        Assert.Equal(1, summary.Counts.Opened);
        Assert.Equal(1, summary.Counts.Replied);
        Assert.Equal(1m, summary.Rates.ReplyRate);
    }

    [Fact]
    public async Task Tenant_context_blocks_cross_company_recording_and_queries()
    {
        var companyA = await SeedCampaignAsync("analytics-tenant-a");
        var companyB = await SeedCampaignAsync("analytics-tenant-b");
        using var scope = CreateCompanyScope(companyA.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMessagePerformanceEventAsync(Command(companyB, "msg-cross", ConversionAnalyticsEventType.Sent, companyB.SentAt), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCampaignPerformanceAsync(companyB.CompanyId, companyB.CampaignId, CancellationToken.None));
    }

    [Fact]
    public async Task Variant_aggregation_returns_reply_and_conversion_rates()
    {
        var seed = await SeedCampaignAsync("analytics-variants");
        using var scope = CreateCompanyScope(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        await RecordVariantAsync(service, seed, "variant-a-1", "variant-a", reply: true, converted: true);
        await RecordVariantAsync(service, seed, "variant-a-2", "variant-a", reply: false, converted: false);
        await RecordVariantAsync(service, seed, "variant-b-1", "variant-b", reply: true, converted: false);

        var variants = await service.GetVariantPerformanceAsync(seed.CompanyId, seed.CampaignId, seed.SequenceId, seed.SequenceStepId, CancellationToken.None);

        var variantA = Assert.Single(variants.Where(x => x.VariantKey == "variant-a"));
        Assert.Equal(2, variantA.Counts.Sent);
        Assert.Equal(1, variantA.Counts.Replied);
        Assert.Equal(1, variantA.Counts.Converted);
        Assert.Equal(0.5m, variantA.Rates.ReplyRate);
        Assert.Equal(0.5m, variantA.Rates.ConversionRate);

        var variantB = Assert.Single(variants.Where(x => x.VariantKey == "variant-b"));
        Assert.Equal(1, variantB.Counts.Sent);
        Assert.Equal(1m, variantB.Rates.ReplyRate);
        Assert.Equal(0m, variantB.Rates.ConversionRate);
    }

    [Fact]
    public async Task Conversion_timestamp_and_revenue_context_are_persisted()
    {
        var seed = await SeedCampaignAsync("analytics-conversion");
        using var scope = CreateCompanyScope(seed.CompanyId);
        var service = scope.ServiceProvider.GetRequiredService<IConversionAnalyticsService>();

        var convertedAt = seed.SentAt.AddDays(2);
        await service.RecordMessagePerformanceEventAsync(
            Command(seed, "msg-converted", ConversionAnalyticsEventType.Converted, convertedAt, dealId: seed.DealId),
            CancellationToken.None);

        var row = await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>()
            .SalesMessagePerformances
            .SingleAsync(x => x.MessageKey == "msg-converted");

        Assert.Equal(convertedAt, row.ConvertedAt);
        Assert.Equal(seed.DealId, row.DealId);
        Assert.Equal(12000m, row.ExpectedRevenueAmount);
        Assert.Equal("USD", row.ExpectedRevenueCurrency);
        Assert.Equal(seed.ExpectedCloseAt, row.ExpectedCloseAt);
    }

    [Fact]
    public async Task Sales_model_declares_conversion_analytics_indexes()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.MessageKey));
        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.CampaignId));
        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.SequenceId));
        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.SequenceStepId));
        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.ContactId), nameof(SalesMessagePerformance.UpdatedUtc));
        AssertIndex<SalesMessagePerformance>(dbContext, nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.CampaignId), nameof(SalesMessagePerformance.SequenceId), nameof(SalesMessagePerformance.SequenceStepId), nameof(SalesMessagePerformance.VariantKey));
    }

    private IServiceScope CreateCompanyScope(Guid companyId)
    {
        var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        return scope;
    }

    private async Task<AnalyticsSeed> SeedCampaignAsync(string suffix)
    {
        var seed = new AnalyticsSeed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(seed.CompanyId, $"Analytics {suffix}"));
            dbContext.CustomerCompanies.Add(new CustomerCompany(seed.CustomerCompanyId, seed.CompanyId, $"Customer {suffix}"));
            dbContext.Contacts.Add(new Contact(seed.ContactId, seed.CompanyId, $"Contact {suffix}", $"{suffix}@example.com", seed.CustomerCompanyId));
            var sequence = new SalesSequence(seed.SequenceId, seed.CompanyId, $"Sequence {suffix}");
            sequence.Steps.Add(new SalesSequenceStep(seed.SequenceStepId, seed.CompanyId, seed.SequenceId, 1, 0, "Hello", templateSubject: "Hello"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), seed.CompanyId, seed.SequenceId, 2, 3, "Follow up", templateSubject: "Follow up"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), seed.CompanyId, seed.SequenceId, 3, 7, "Checking in", templateSubject: "Checking in"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), seed.CompanyId, seed.SequenceId, 4, 14, "Last note", templateSubject: "Last note"));
            dbContext.SalesSequences.Add(sequence);
            dbContext.SalesCampaigns.Add(new SalesCampaign(seed.CampaignId, seed.CompanyId, seed.SequenceId, $"Campaign {suffix}", "contacts"));
            dbContext.Deals.Add(new Deal(seed.DealId, seed.CompanyId, $"Deal {suffix}", SalesPipelineStage.QualifiedStageId, 12000m, "USD", primaryContactId: seed.ContactId, customerCompanyId: seed.CustomerCompanyId, expectedCloseUtc: seed.ExpectedCloseAt));
            return Task.CompletedTask;
        });

        return seed;
    }

    private static RecordMessagePerformanceEventCommand Command(
        AnalyticsSeed seed,
        string messageKey,
        ConversionAnalyticsEventType eventType,
        DateTime occurredUtc,
        string? variantKey = "variant-a",
        Guid? dealId = null) =>
        new(
            seed.CompanyId,
            messageKey,
            seed.ContactId,
            eventType,
            occurredUtc,
            CampaignId: seed.CampaignId,
            SequenceId: seed.SequenceId,
            SequenceStepId: seed.SequenceStepId,
            DealId: dealId,
            Provider: "test",
            ProviderMessageId: messageKey,
            VariantKey: variantKey,
            StepOrder: 1);

    private static async Task RecordVariantAsync(IConversionAnalyticsService service, AnalyticsSeed seed, string messageKey, string variantKey, bool reply, bool converted)
    {
        await service.RecordMessagePerformanceEventAsync(Command(seed, messageKey, ConversionAnalyticsEventType.Sent, seed.SentAt, variantKey), CancellationToken.None);
        if (reply)
        {
            await service.RecordMessagePerformanceEventAsync(Command(seed, messageKey, ConversionAnalyticsEventType.Replied, seed.SentAt.AddHours(1), variantKey), CancellationToken.None);
        }

        if (converted)
        {
            await service.RecordMessagePerformanceEventAsync(Command(seed, messageKey, ConversionAnalyticsEventType.Converted, seed.SentAt.AddDays(1), variantKey, seed.DealId), CancellationToken.None);
        }
    }

    private static void AssertIndex<TEntity>(VirtualCompanyDbContext dbContext, params string[] propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private sealed record AnalyticsSeed(
        Guid CompanyId,
        Guid CustomerCompanyId,
        Guid ContactId,
        Guid SequenceId,
        Guid SequenceStepId,
        Guid CampaignId,
        Guid SequenceExecutionId,
        Guid SequenceExecutionStepId,
        Guid DealId,
        DateTime SentAt,
        DateTime ExpectedCloseAt);
}
