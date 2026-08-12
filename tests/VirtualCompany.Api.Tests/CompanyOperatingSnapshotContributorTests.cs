using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOperatingSnapshotContributorTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public void All_operating_departments_register_distinct_bounded_snapshot_contributors()
    {
        using var scope = _factory.Services.CreateScope();
        var sections = scope.ServiceProvider.GetServices<ICompanyOperatingSnapshotContributor>()
            .Select(x => x.SectionName).ToArray();

        Assert.Contains("finance", sections);
        Assert.Contains("sales", sections);
        Assert.Contains("marketing", sections);
        Assert.Contains("support", sections);
        Assert.Equal(sections.Length, sections.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Failed_contributor_is_persisted_as_an_explicit_data_gap()
    {
        var seed = await SeedTwoCompaniesAsync();
        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyB);
        var service = CreateService(scope, [new FailingContributor("finance")]);

        var snapshot = await service.CaptureAsync(seed.CompanyB, seed.CycleB, CancellationToken.None);

        Assert.True(snapshot.DataGapCount > 0);
        var gaps = snapshot.Payload["dataGaps"]?.ToJsonString() ?? string.Empty;
        Assert.Contains("finance: source data was temporarily unavailable", gaps, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contributor_cancellation_is_not_converted_to_a_healthy_snapshot()
    {
        var seed = await SeedTwoCompaniesAsync();
        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyB);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(scope, [new CancellingContributor(cancellation.Cancel)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CaptureAsync(seed.CompanyB, seed.CycleB, cancellation.Token));
    }

    [Fact]
    public async Task Snapshot_capture_does_not_disclose_another_companys_goal_or_agent()
    {
        var seed = await SeedTwoCompaniesAsync();
        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyB);
        var service = CreateService(scope, []);

        var snapshot = await service.CaptureAsync(seed.CompanyB, seed.CycleB, CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot.Payload);

        Assert.Contains("Company B goal", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Company A confidential goal", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Agent A Secret", json, StringComparison.Ordinal);
    }

    private static CompanyOperatingSnapshotService CreateService(IServiceScope scope,
        IReadOnlyList<ICompanyOperatingSnapshotContributor> contributors) => new(
        scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>(),
        scope.ServiceProvider.GetRequiredService<ISignalEngine>(), contributors,
        scope.ServiceProvider.GetRequiredService<ICompanyMembershipContextResolver>(),
        NullLogger<CompanyOperatingSnapshotService>.Instance);

    private async Task<(Guid CompanyA, Guid CompanyB, Guid CycleB)> SeedTwoCompaniesAsync()
    {
        var companyA = Guid.NewGuid(); var companyB = Guid.NewGuid();
        var agentA = Guid.NewGuid(); var agentB = Guid.NewGuid(); var cycleB = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Companies.AddRange(new Company(companyA, "Company A"), new Company(companyB, "Company B"));
            db.Agents.AddRange(
                new Agent(agentA, companyA, "operations", "Agent A Secret", "Manager", "Operations", null,
                    AgentSeniority.Lead, AgentStatus.Active, AgentAutonomyLevel.Level1),
                new Agent(agentB, companyB, "operations", "Agent B", "Manager", "Operations", null,
                    AgentSeniority.Lead, AgentStatus.Active, AgentAutonomyLevel.Level1));
            var goalA = new CompanyGoal(Guid.NewGuid(), companyA, "Company A confidential goal", "Keep this private.",
                CompanyGoalPriority.High, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1), ownerAgentId: agentA);
            var goalB = new CompanyGoal(Guid.NewGuid(), companyB, "Company B goal", "Improve B operations.",
                CompanyGoalPriority.High, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1), ownerAgentId: agentB);
            goalA.Activate(); goalB.Activate(); db.CompanyGoals.AddRange(goalA, goalB);
            db.OperatingCycles.Add(new OperatingCycle(cycleB, companyB, "manual", null, agentB,
                "snapshot-correlation", "snapshot-cycle-b", 1));
            return Task.CompletedTask;
        });
        return (companyA, companyB, cycleB);
    }

    private sealed class FailingContributor(string section) : ICompanyOperatingSnapshotContributor
    {
        public string SectionName => section;
        public Task<CompanyOperatingSnapshotContribution> CaptureAsync(Guid companyId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated unavailable source.");
    }

    private sealed class CancellingContributor(Action cancel) : ICompanyOperatingSnapshotContributor
    {
        public string SectionName => "support";
        public Task<CompanyOperatingSnapshotContribution> CaptureAsync(Guid companyId, CancellationToken cancellationToken)
        {
            cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
