using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MonthlyWorkspaceIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Sales_manager_receives_only_authorized_period_aware_sales_review()
    {
        var seed = await SeedAsync();
        using var client = Client("monthly-manager", "monthly-manager@example.com", "Sales Manager");

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/monthly?lens=sales&year=2026&month=8");
        var workspace = await response.Content.ReadFromJsonAsync<MonthlyWorkspaceDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TodayWorkspaceLenses.Sales, workspace!.ActiveLens);
        Assert.Contains(workspace.AvailableLenses, x => x.Value == TodayWorkspaceLenses.Sales);
        Assert.Contains(workspace.AvailableLenses, x => x.Value == TodayWorkspaceLenses.Marketing);
        Assert.Single(workspace.Sections);
        Assert.All(workspace.Sections, x => Assert.Equal(TodayWorkspaceLenses.Sales, x.Lens));
        Assert.DoesNotContain(workspace.Results, x => x.Key.StartsWith("finance", StringComparison.Ordinal));
        var movement = Assert.Single(workspace.Results, x => x.Key == "sales.stage_movement");
        Assert.Equal(1m, movement.Value);
        Assert.Equal(1m, movement.ComparisonValue);
        Assert.Equal(8, workspace.Period.Month);
        Assert.Equal(workspace.Period.StartUtc, workspace.Period.ComparisonEndUtc);
    }

    [Fact]
    public async Task Owner_company_review_includes_authorized_company_and_sales_sources_with_deterministic_summary()
    {
        var seed = await SeedAsync();
        using var client = Client("monthly-owner", "monthly-owner@example.com", "Owner");

        var workspace = await (await client.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/monthly?lens=company&year=2026&month=8"))
            .Content.ReadFromJsonAsync<MonthlyWorkspaceDto>();

        Assert.NotNull(workspace);
        Assert.Equal(TodayWorkspaceLenses.Company, workspace!.ActiveLens);
        Assert.Contains(workspace.Sections, x => x.Lens == TodayWorkspaceLenses.Company);
        Assert.Contains(workspace.Sections, x => x.Lens == TodayWorkspaceLenses.Sales);
        Assert.True(workspace.ManagementSummary.IsDeterministicFallback);
        Assert.All(workspace.Priorities, x => Assert.Contains(x.Lens,
            new[] { TodayWorkspaceLenses.Company, TodayWorkspaceLenses.Sales, TodayWorkspaceLenses.Marketing }));
    }

    [Fact]
    public async Task Invalid_period_and_cross_company_access_are_rejected()
    {
        var seed = await SeedAsync();
        using var manager = Client("monthly-manager", "monthly-manager@example.com", "Sales Manager");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/monthly?year=2026&month=13")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/companies/{seed.OtherCompanyId:D}/workspace/monthly")).StatusCode);
    }

    [Fact]
    public async Task Missing_authoritative_marketing_outcomes_are_reported_as_unavailable()
    {
        var seed = await SeedAsync();
        using var client = Client("monthly-manager", "monthly-manager@example.com", "Sales Manager");

        var workspace = await (await client.GetAsync(
                $"/api/companies/{seed.CompanyId:D}/workspace/monthly?lens=marketing&year=2026&month=8"))
            .Content.ReadFromJsonAsync<MonthlyWorkspaceDto>();

        var outcome = Assert.Single(workspace!.Results, x => x.Key == "marketing.outcomes");
        Assert.False(outcome.IsAvailable);
        Assert.Equal("Unavailable", outcome.DisplayValue);
        Assert.NotEmpty(outcome.UnavailableReason!);
        Assert.Contains(workspace.SourceCoverage, x => x.Key == "marketing" && x.State == "unavailable");
        Assert.True(workspace.IsPartial);
    }

    [Fact]
    public async Task Contributor_failure_returns_a_partial_workspace_instead_of_failing_the_review()
    {
        using var factory = new ThrowingMonthlyContributorFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, "monthly-manager", "monthly-manager@example.com", "Sales Manager");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/workspace/monthly?lens=sales&year=2026&month=8");
        var workspace = await response.Content.ReadFromJsonAsync<MonthlyWorkspaceDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(workspace!.IsPartial);
        Assert.Contains(workspace.Diagnostics, x => x.Code == "monthly_contributor_failed" && x.Section == "sales");
        Assert.Contains(workspace.Sections, x => x.Lens == "sales" && !x.IsAvailable);
    }

    private HttpClient Client(string subject, string email, string name)
        => Client(_factory, subject, email, name);

    private static HttpClient Client(TestWebApplicationFactory factory, string subject, string email, string name)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, name);
        return client;
    }

    private Task<Seed> SeedAsync() => SeedAsync(_factory);

    private static async Task<Seed> SeedAsync(TestWebApplicationFactory factory)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await factory.SeedAsync(db =>
        {
            var ownerUser = Guid.NewGuid(); var managerUser = Guid.NewGuid(); var otherOwner = Guid.NewGuid();
            db.Users.AddRange(
                new User(ownerUser, "monthly-owner@example.com", "Owner", "dev-header", "monthly-owner"),
                new User(managerUser, "monthly-manager@example.com", "Sales Manager", "dev-header", "monthly-manager"),
                new User(otherOwner, "monthly-other@example.com", "Other", "dev-header", "monthly-other"));
            var company = new Company(seed.CompanyId, "Monthly Company");
            company.UpdateWorkspaceProfile("Monthly Company", null, null, "UTC", "SEK", "en", "SE");
            db.Companies.AddRange(company, new Company(seed.OtherCompanyId, "Other Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(seed.OwnerMembershipId, seed.CompanyId, ownerUser, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(seed.ManagerMembershipId, seed.CompanyId, managerUser, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.OtherCompanyId, otherOwner, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.AddRange(
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.CompanyPerformance,
                    ResponsibilityAssignmentKind.ExecutiveOversight, seed.OwnerMembershipId, null, AgentAutonomyLevel.Level1, null, null),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                    ResponsibilityAssignmentKind.Primary, seed.ManagerMembershipId, null, AgentAutonomyLevel.Level1, null, seed.OwnerMembershipId),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Marketing,
                    ResponsibilityAssignmentKind.Primary, seed.ManagerMembershipId, null, AgentAutonomyLevel.Level1, null, seed.OwnerMembershipId),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                    ResponsibilityAssignmentKind.ExecutiveOversight, seed.OwnerMembershipId, null, AgentAutonomyLevel.Level1, null, null),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Marketing,
                    ResponsibilityAssignmentKind.ExecutiveOversight, seed.OwnerMembershipId, null, AgentAutonomyLevel.Level1, null, null));
            db.SalesActivities.AddRange(
                new SalesActivity(Guid.NewGuid(), seed.CompanyId, "stage change", "Deal advanced during August.",
                    new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)),
                new SalesActivity(Guid.NewGuid(), seed.CompanyId, "stage change", "Deal advanced during July.",
                    new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc)),
                new SalesActivity(Guid.NewGuid(), seed.CompanyId, "stage change", "Deal advanced after the period.",
                    new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)));
            return Task.CompletedTask;
        });
        return seed;
    }

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid OwnerMembershipId, Guid ManagerMembershipId);

    private sealed class ThrowingMonthlyContributorFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMonthlyWorkspaceContributor>();
                services.AddScoped<IMonthlyWorkspaceContributor, ThrowingSalesContributor>();
            });
        }
    }

    private sealed class ThrowingSalesContributor : IMonthlyWorkspaceContributor
    {
        public string Lens => TodayWorkspaceLenses.Sales;

        public Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
            MonthlyWorkspaceContributorContext context,
            CancellationToken cancellationToken) =>
            Task.FromException<MonthlyWorkspaceFeatureContribution>(new InvalidOperationException("configured failure"));
    }
}
