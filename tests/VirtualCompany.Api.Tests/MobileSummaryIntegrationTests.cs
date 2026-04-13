using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Shared.Mobile;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MobileSummaryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MobileSummaryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Mobile_summary_returns_empty_company_status_for_sparse_workspace()
    {
        var companyId = await SeedSparseCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{companyId}/mobile/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<MobileHomeSummaryResponse>();

        Assert.NotNull(summary);
        Assert.Equal(companyId, summary!.CompanyStatus.CompanyId);
        Assert.Equal("Operations steady", summary.CompanyStatus.Headline);
        Assert.Equal(0, summary.CompanyStatus.PendingApprovalCount);
        Assert.Equal(0, summary.CompanyStatus.ActiveAlertCount);
        Assert.Equal(0, summary.CompanyStatus.OpenTaskCount);
        Assert.Equal(0, summary.CompanyStatus.BlockedTaskCount);
        Assert.Equal(0, summary.CompanyStatus.OverdueTaskCount);
        Assert.False(summary.HasTaskFollowUps);
        Assert.Empty(summary.TaskFollowUps);
    }

    [Fact]
    public async Task Mobile_summary_is_tenant_scoped_and_orders_recent_task_follow_ups()
    {
        var seed = await SeedMobileSummaryCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/mobile/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<MobileHomeSummaryResponse>();

        Assert.NotNull(summary);
        Assert.Equal(seed.CompanyId, summary!.CompanyStatus.CompanyId);
        Assert.Equal("Needs executive follow-up", summary.CompanyStatus.Headline);
        Assert.Equal(1, summary.CompanyStatus.PendingApprovalCount);
        Assert.Equal(1, summary.CompanyStatus.ActiveAlertCount);
        Assert.Equal(2, summary.CompanyStatus.OpenTaskCount);
        Assert.Equal(1, summary.CompanyStatus.BlockedTaskCount);
        Assert.Equal(1, summary.CompanyStatus.OverdueTaskCount);
        Assert.Contains(summary.CompanyStatus.Metrics, x => x.Key == "open_tasks" && x.Value == 2);
        Assert.True(summary.HasTaskFollowUps);
        Assert.Equal([seed.NewerTaskId, seed.OlderTaskId], summary.TaskFollowUps.Select(x => x.TaskId).ToArray());

        var newer = summary.TaskFollowUps[0];
        Assert.Equal("critical", newer.Priority);
        Assert.Equal("blocked", newer.Status);
        Assert.Equal("Avery Ops", newer.AssignedAgentDisplayName);
        Assert.Contains("Vendor response is missing", newer.Summary);
        Assert.True(newer.IsOverdue);
        Assert.DoesNotContain(summary.TaskFollowUps, x => x.TaskId == seed.OtherCompanyTaskId);

        var otherCompanyResponse = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/mobile/summary");
        Assert.Equal(HttpStatusCode.Forbidden, otherCompanyResponse.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "mobile-summary-founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "mobile-summary-founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Mobile Summary Founder");
        return client;
    }

    private async Task<Guid> SeedSparseCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "mobile-summary-founder@example.com", "Mobile Summary Founder", "dev-header", "mobile-summary-founder"));
            dbContext.Companies.Add(new Company(companyId, "Sparse Mobile Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return companyId;
    }

    private async Task<MobileSummarySeed> SeedMobileSummaryCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var newerTaskId = Guid.NewGuid();
        var olderTaskId = Guid.NewGuid();
        var otherCompanyTaskId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "mobile-summary-founder@example.com", "Mobile Summary Founder", "dev-header", "mobile-summary-founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Mobile Summary Company"), new Company(otherCompanyId, "Other Mobile Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var newerTask = new WorkTask(newerTaskId, companyId, "ops", "Unblock vendor launch", null, WorkTaskPriority.Critical, agentId, null, "user", userId);
            newerTask.SetDueDate(DateTime.UtcNow.AddDays(-1));
            newerTask.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: "Vendor response is missing; confirm ownership before tomorrow's launch review.");
            SetTaskTimestamps(newerTask, DateTime.UtcNow.AddMinutes(-10), null);

            var olderTask = new WorkTask(olderTaskId, companyId, "ops", "Prepare launch readout", null, WorkTaskPriority.High, agentId, null, "user", userId);
            olderTask.UpdateStatus(WorkTaskStatus.InProgress);
            SetTaskTimestamps(olderTask, DateTime.UtcNow.AddHours(-2), null);

            var otherCompanyTask = new WorkTask(otherCompanyTaskId, otherCompanyId, "ops", "Other company task", null, WorkTaskPriority.Critical, null, null, "user", userId);
            otherCompanyTask.UpdateStatus(WorkTaskStatus.Blocked);

            dbContext.WorkTasks.AddRange(newerTask, olderTask, otherCompanyTask);
            dbContext.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(Guid.NewGuid(), companyId, ApprovalTargetEntityType.Task, olderTaskId, "agent", agentId, "threshold", Payload(("amount", JsonValue.Create(25000))), "owner", null, []));
            dbContext.CompanyNotifications.Add(new CompanyNotification(Guid.NewGuid(), companyId, userId, CompanyNotificationType.WorkflowFailure, CompanyNotificationPriority.Critical, "Workflow failed", "A workflow step failed.", "workflow_exception", Guid.NewGuid(), "/workflows", "{}", $"mobile-summary:{Guid.NewGuid():N}"));
            return Task.CompletedTask;
        });

        return new MobileSummarySeed(companyId, otherCompanyId, newerTaskId, olderTaskId, otherCompanyTaskId);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties) =>
        properties.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void SetTaskTimestamps(WorkTask task, DateTime updatedUtc, DateTime? completedUtc)
    {
        typeof(WorkTask).GetProperty(nameof(WorkTask.UpdatedUtc))!.SetValue(task, updatedUtc);
        if (completedUtc.HasValue)
        {
            typeof(WorkTask).GetProperty(nameof(WorkTask.CompletedUtc))!.SetValue(task, completedUtc.Value);
        }
    }

    private sealed record MobileSummarySeed(Guid CompanyId, Guid OtherCompanyId, Guid NewerTaskId, Guid OlderTaskId, Guid OtherCompanyTaskId);
}
