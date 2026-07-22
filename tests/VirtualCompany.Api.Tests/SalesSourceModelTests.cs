using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesSourceModelTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Records_first_last_conversion_and_cost_without_cross_tenant_reads()
    {
        var company = Guid.NewGuid(); var other = Guid.NewGuid(); var lead = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ISalesSourceService>();
        await service.RecordAsync(company, new("lead", lead, "event", "sme_fair", "event", "discovery", "badge-1", DateTime.UtcNow.AddDays(-2), "human", "owner", Cost: 100, Currency: "SEK"), default);
        await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().SaveChangesAsync();
        await service.RecordAsync(company, new("lead", lead, "email", "outlook", "email", "inquiry", "message-1", DateTime.UtcNow, "visitor", "buyer@example.com", Cost: 20, Currency: "SEK", IsConversion: true), default);
        await scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>().SaveChangesAsync();

        var attribution = await service.GetAsync(company, "lead", lead, default);
        Assert.NotNull(attribution); Assert.Equal(2, attribution!.TouchCount); Assert.Equal(120, attribution.TotalAcquisitionCost); Assert.NotNull(attribution.ConversionTouchId); Assert.Equal(2, attribution.Timeline.Count);
        Assert.Null(await service.GetAsync(other, "lead", lead, default));
    }
}

public sealed class SalesProspectingToolRegistryTests
{
    [Theory]
    [InlineData("sales.plan_prospecting_run", ToolActionType.Recommend)]
    [InlineData("sales.start_prospecting_run", ToolActionType.Execute)]
    [InlineData("sales.list_prospects", ToolActionType.Read)]
    [InlineData("sales.research_prospect", ToolActionType.Recommend)]
    [InlineData("sales.recommend_prospect_decision", ToolActionType.Recommend)]
    public void Alex_prospecting_tools_have_explicit_action_boundaries(string name, ToolActionType action)
    {
        var registry = new StaticCompanyToolRegistry();
        Assert.True(registry.TryGetToolDefinition(name, out var definition));
        Assert.Equal(action, definition.ActionType);
        Assert.True(registry.TryGetTool(name, out var registration));
        Assert.Contains("sales", registration.Scopes);
    }
}
