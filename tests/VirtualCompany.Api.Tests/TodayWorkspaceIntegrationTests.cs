using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayWorkspaceIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Sales_manager_defaults_to_explicit_sales_lens_without_finance_details()
    {
        var seed = await SeedAsync();
        using var client = Client("today-manager", "today-manager@example.com", "Sales Manager");

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today");
        var workspace = await response.Content.ReadFromJsonAsync<TodayWorkspaceDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(workspace);
        Assert.Equal(TodayWorkspaceLenses.Sales, workspace!.ActiveLens);
        Assert.Single(workspace.AvailableLenses);
        Assert.NotNull(workspace.Sales);
        Assert.Null(workspace.Finance);
        Assert.True(workspace.Metrics.Count <= 4);
        Assert.True(workspace.Priorities.Count <= 5);
        Assert.All(workspace.Priorities, item => Assert.Equal(TodayWorkspaceLenses.Sales, item.Lens));
    }

    [Fact]
    public async Task Owner_oversight_can_request_company_lens_and_manager_falls_back_from_unassigned_finance_lens()
    {
        var seed = await SeedAsync();
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");
        using var manager = Client("today-manager", "today-manager@example.com", "Sales Manager");

        var ownerResponse = await owner.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today?lens=company");
        var managerResponse = await manager.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today?lens=finance");

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var ownerWorkspace = await ownerResponse.Content.ReadFromJsonAsync<TodayWorkspaceDto>();
        Assert.Equal(TodayWorkspaceLenses.Company, ownerWorkspace!.ActiveLens);
        Assert.Contains(ownerWorkspace.AvailableLenses, x => x.Value == TodayWorkspaceLenses.Sales);
        Assert.NotNull(ownerWorkspace.Sales);
        managerResponse.EnsureSuccessStatusCode();
        var managerWorkspace = await managerResponse.Content.ReadFromJsonAsync<TodayWorkspaceDto>();
        Assert.NotNull(managerWorkspace);
        Assert.Equal(TodayWorkspaceLenses.Sales, managerWorkspace.ActiveLens);
        Assert.Null(managerWorkspace.Finance);
    }

    [Fact]
    public async Task Invalid_lens_is_bad_request_and_inactive_or_cross_company_access_is_forbidden()
    {
        var seed = await SeedAsync();
        using var manager = Client("today-manager", "today-manager@example.com", "Sales Manager");
        using var inactive = Client("today-inactive", "today-inactive@example.com", "Inactive");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today?lens=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await inactive.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/companies/{seed.OtherCompanyId:D}/workspace/today")).StatusCode);
    }

    [Fact]
    public async Task Ordinary_member_gets_safe_company_fallback_without_feature_sections()
    {
        var seed = await SeedAsync();
        using var member = Client("today-member", "today-member@example.com", "Member");

        var response = await member.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today");
        var workspace = await response.Content.ReadFromJsonAsync<TodayWorkspaceDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TodayWorkspaceLenses.Company, workspace!.ActiveLens);
        Assert.Single(workspace.AvailableLenses);
        Assert.Null(workspace.Finance);
        Assert.Null(workspace.Sales);
        Assert.Null(workspace.Support);
        Assert.Null(workspace.Marketing);
    }

    [Fact]
    public async Task Unconfigured_company_uses_membership_fallback_without_overriding_configured_company_assignment()
    {
        var seed = await SeedAsync();
        using var manager = Client("today-manager", "today-manager@example.com", "Sales Manager");

        var response = await manager.GetAsync($"/api/companies/{seed.UnconfiguredCompanyId:D}/workspace/today");
        var workspace = await response.Content.ReadFromJsonAsync<TodayWorkspaceDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TodayWorkspaceLenses.Company, workspace!.ActiveLens);
        Assert.Contains(workspace.AvailableLenses, x => x.Value == TodayWorkspaceLenses.Finance);
        Assert.Contains(workspace.AvailableLenses, x => x.Value == TodayWorkspaceLenses.Sales);
        Assert.All(workspace.AvailableLenses, x => Assert.Contains("fallback", x.AvailabilityReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Responsibility_change_invalidates_personalized_result_and_changes_default_lens()
    {
        var seed = await SeedAsync();
        using var manager = Client("today-manager", "today-manager@example.com", "Sales Manager");
        var first = await (await manager.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today"))
            .Content.ReadFromJsonAsync<TodayWorkspaceDto>();
        Assert.Equal(TodayWorkspaceLenses.Sales, first!.ActiveLens);

        await _factory.SeedAsync(async db =>
        {
            var sales = await db.CompanyResponsibilityAssignments.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId &&
                                  x.ResponsibilityArea == ResponsibilityArea.Sales &&
                                  x.AssignmentKind == ResponsibilityAssignmentKind.Primary);
            db.CompanyResponsibilityAssignments.Remove(sales);
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(
                Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Marketing, ResponsibilityAssignmentKind.Primary,
                seed.ManagerMembershipId, null, AgentAutonomyLevel.Level1, null, seed.OwnerMembershipId));
        });

        var second = await (await manager.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today"))
            .Content.ReadFromJsonAsync<TodayWorkspaceDto>();
        Assert.Equal(TodayWorkspaceLenses.Marketing, second!.ActiveLens);
        Assert.Null(second.CacheTimestampUtc);
    }

    [Fact]
    public async Task Authorized_manual_review_is_durable_idempotent_and_audited()
    {
        var seed = await SeedAsync();
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");

        var firstResponse = await owner.PostAsync($"/api/companies/{seed.CompanyId:D}/operating/reviews/request", null);
        var first = await firstResponse.Content.ReadFromJsonAsync<TodayWorkspaceManualReviewDto>();
        var second = await (await owner.PostAsync($"/api/companies/{seed.CompanyId:D}/operating/reviews/request", null))
            .Content.ReadFromJsonAsync<TodayWorkspaceManualReviewDto>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.Equal("queued", first!.State);
        Assert.Equal(first.RequestId, second!.RequestId);
        await _factory.SeedAsync(async db =>
        {
            Assert.Equal(1, await db.OperatingCycleRequests.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyId && x.TriggerType == "manual_review"));
            Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == seed.CompanyId && x.Action == "company.operating_cycle.manual_review_requested" &&
                x.TargetId == first.RequestId!.Value.ToString("N")));
        });
    }

    [Fact]
    public async Task Manual_review_rejects_ordinary_and_cross_company_members()
    {
        var seed = await SeedAsync();
        using var member = Client("today-member", "today-member@example.com", "Member");
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsync($"/api/companies/{seed.CompanyId:D}/operating/reviews/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsync($"/api/companies/{seed.OtherCompanyId:D}/operating/reviews/request", null)).StatusCode);
    }

    [Fact]
    public async Task Paused_operation_returns_stable_reason_without_queueing_review()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(async db =>
        {
            var configuration = await db.CompanyOperatingConfigurations.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId);
            configuration.Pause("Quarterly control check");
        });
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");

        var response = await owner.PostAsync($"/api/companies/{seed.CompanyId:D}/operating/reviews/request", null);
        var review = await response.Content.ReadFromJsonAsync<TodayWorkspaceManualReviewDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(review!.CanRequest);
        Assert.Equal("paused", review.UnavailableReasonCode);
        await _factory.SeedAsync(async db => Assert.False(await db.OperatingCycleRequests.IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.TriggerType == "manual_review")));
    }

    [Fact]
    public async Task Exhausted_cycle_budget_denies_manual_review_without_starting_agents()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(async db =>
        {
            var configuration = await db.CompanyOperatingConfigurations.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId);
            configuration.Update(configuration.CoordinatorAgentId, configuration.AutonomyLevel, configuration.Timezone,
                configuration.DailyCycleHour, configuration.MinimumCycleIntervalMinutes, 1,
                configuration.MaximumInitiativesPerCycle, configuration.MaximumTasksPerCycle,
                configuration.MaximumCollaborators, configuration.MaximumRuntimeSeconds,
                configuration.MaximumModelCallsPerCycle, configuration.MaximumToolCallsPerCycle,
                configuration.MaximumMonetaryBudgetPerCycle);
            db.OperatingCycles.Add(new OperatingCycle(Guid.NewGuid(), seed.CompanyId, "scheduled", null,
                configuration.CoordinatorAgentId!.Value, "budget-correlation", "budget-cycle", configuration.Version));
        });
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");

        var review = await (await owner.PostAsync($"/api/companies/{seed.CompanyId:D}/operating/reviews/request", null))
            .Content.ReadFromJsonAsync<TodayWorkspaceManualReviewDto>();

        Assert.False(review!.CanRequest);
        Assert.Equal("cycle_budget_reached", review.UnavailableReasonCode);
        await _factory.SeedAsync(async db => Assert.False(await db.OperatingCycleRequests.IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.TriggerType == "manual_review")));
    }

    [Fact]
    public async Task Executive_oversight_includes_material_sales_risk_but_excludes_routine_work()
    {
        var seed = await SeedAsync();
        using var owner = Client("today-owner", "today-owner@example.com", "Owner");

        var workspace = await (await owner.GetAsync($"/api/companies/{seed.CompanyId:D}/workspace/today?lens=company"))
            .Content.ReadFromJsonAsync<TodayWorkspaceDto>();

        Assert.Contains(workspace!.AgentUpdates, x => x.Title == "Critical renewal risk" &&
            x.VisibilityReason!.Contains("executive oversight", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workspace.AgentUpdates, x => x.Title == "Routine account notes");
    }

    private HttpClient Client(string subject, string email, string name)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, name);
        return client;
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await _factory.SeedAsync(db =>
        {
            var ownerUserId = Guid.NewGuid();
            var managerUserId = Guid.NewGuid();
            var memberUserId = Guid.NewGuid();
            var inactiveUserId = Guid.NewGuid();
            var otherOwnerId = Guid.NewGuid();
            var coordinatorAgentId = Guid.NewGuid();
            db.Users.AddRange(
                new User(ownerUserId, "today-owner@example.com", "Owner", "dev-header", "today-owner"),
                new User(managerUserId, "today-manager@example.com", "Sales Manager", "dev-header", "today-manager"),
                new User(memberUserId, "today-member@example.com", "Member", "dev-header", "today-member"),
                new User(inactiveUserId, "today-inactive@example.com", "Inactive", "dev-header", "today-inactive"),
                new User(otherOwnerId, "today-other@example.com", "Other", "dev-header", "today-other"));
            db.Companies.AddRange(
                new Company(seed.CompanyId, "Today Company"),
                new Company(seed.OtherCompanyId, "Other Company"),
                new Company(seed.UnconfiguredCompanyId, "Unconfigured Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(seed.OwnerMembershipId, seed.CompanyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(seed.ManagerMembershipId, seed.CompanyId, managerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.UnconfiguredCompanyId, managerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, memberUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, inactiveUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Revoked),
                new CompanyMembership(Guid.NewGuid(), seed.OtherCompanyId, otherOwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Agents.Add(new Agent(coordinatorAgentId, seed.CompanyId, "company-coordinator", "Orion",
                "Company Coordinator", "Company", null, AgentSeniority.Lead, AgentStatus.Active,
                AgentAutonomyLevel.Level1));
            var operatingConfiguration = new CompanyOperatingConfiguration(Guid.NewGuid(), seed.CompanyId);
            operatingConfiguration.Update(coordinatorAgentId, CompanyAutonomyLevel.Recommend, "UTC", 6, 60, 4,
                5, 12, 3, 120, 4, 20, null);
            db.CompanyOperatingConfigurations.Add(operatingConfiguration);
            db.WorkTasks.AddRange(
                new WorkTask(Guid.NewGuid(), seed.CompanyId, "sales_risk", "Critical renewal risk",
                    "A material customer renewal needs intervention.", WorkTaskPriority.Critical,
                    coordinatorAgentId, null, "agent", coordinatorAgentId, status: WorkTaskStatus.Blocked),
                new WorkTask(Guid.NewGuid(), seed.CompanyId, "sales_admin", "Routine account notes",
                    "Routine account administration.", WorkTaskPriority.Normal,
                    coordinatorAgentId, null, "agent", coordinatorAgentId, status: WorkTaskStatus.InProgress));
            db.CompanyResponsibilityAssignments.AddRange(
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                    ResponsibilityAssignmentKind.Primary, seed.ManagerMembershipId, coordinatorAgentId, AgentAutonomyLevel.Level1, null, seed.OwnerMembershipId),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.CompanyPerformance,
                    ResponsibilityAssignmentKind.ExecutiveOversight, seed.OwnerMembershipId, null, AgentAutonomyLevel.Level1, null, null),
                new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                    ResponsibilityAssignmentKind.ExecutiveOversight, seed.OwnerMembershipId, null, AgentAutonomyLevel.Level1, null, null));
            return Task.CompletedTask;
        });
        return seed;
    }

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid UnconfiguredCompanyId, Guid OwnerMembershipId, Guid ManagerMembershipId);
}
