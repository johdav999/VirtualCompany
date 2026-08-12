using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOperatingEventServiceTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Equivalent_material_events_coalesce_into_one_cycle_request()
    {
        var companyId = await SeedCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(companyId);
        var service = scope.ServiceProvider.GetRequiredService<ICompanyOperatingEventService>();
        var observed = DateTime.UtcNow;

        var first = await service.RecordAsync(companyId, new RecordOperatingEventCommand(
            "workflow_outcome", "workflow", "workflow-1", 1, observed, "high", "workflow-1:v1", "corr-1"), CancellationToken.None);
        var second = await service.RecordAsync(companyId, new RecordOperatingEventCommand(
            "workflow_outcome", "workflow", "workflow-2", 1, observed.AddMinutes(1), "high", "workflow-2:v1", "corr-2"), CancellationToken.None);

        Assert.Equal("pending", first.Status);
        Assert.Equal("coalesced", second.Status);
        var requestCount = await _factory.ExecuteDbContextAsync(db => db.OperatingCycleRequests.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Administrative_self_events_are_suppressed_without_recursive_cycle_request()
    {
        var companyId = await SeedCompanyAsync();
        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(companyId);
        var service = scope.ServiceProvider.GetRequiredService<ICompanyOperatingEventService>();

        var result = await service.RecordAsync(companyId, new RecordOperatingEventCommand(
            "operating_plan_updated", "operating_plan", Guid.NewGuid().ToString("N"), 1,
            DateTime.UtcNow, "high", Guid.NewGuid().ToString("N"), "corr-self"), CancellationToken.None);

        Assert.Equal("suppressed", result.Status);
        var requestCount = await _factory.ExecuteDbContextAsync(db => db.OperatingCycleRequests.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(0, requestCount);
    }

    private async Task<Guid> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Event policy company"));
            db.CompanyOperatingConfigurations.Add(new CompanyOperatingConfiguration(Guid.NewGuid(), companyId));
            return Task.CompletedTask;
        });
        return companyId;
    }
}
