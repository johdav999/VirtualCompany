using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ActionQueueEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActionQueueEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Queue_contains_all_action_types_with_required_fields()
    {
        var seed = await SeedActionQueueAsync();
        using var client = CreateAuthenticatedClient("founder", "founder@example.com");

        var items = await client.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.CompanyId}/action-insights/queue");

        Assert.NotNull(items);
        Assert.Contains(items!, item => item.Type == "approval");
        Assert.Contains(items!, item => item.Type == "task");
        Assert.Contains(items!, item => item.Type == "risk");
        Assert.Contains(items!, item => item.Type == "blocked_workflow");
        Assert.Contains(items!, item => item.Type == "opportunity");
        Assert.All(items!, item =>
        {
            Assert.NotEmpty(item.InsightKey);
            Assert.NotEmpty(item.Priority);
            Assert.NotEmpty(item.Reason);
            Assert.NotEmpty(item.Owner);
            Assert.False(string.IsNullOrWhiteSpace(item.SlaState));
            Assert.False(string.IsNullOrWhiteSpace(item.DeepLink));
            Assert.Contains($"companyId={item.CompanyId:D}", item.DeepLink);
            Assert.False(string.IsNullOrWhiteSpace(item.StableSortKey));
        });
    }

    [Fact]
    public async Task Queue_deep_links_are_resolved_for_tasks_workflows_and_approvals()
    {
        var seed = await SeedActionQueueAsync();
        using var client = CreateAuthenticatedClient("founder", "founder@example.com");

        var items = await client.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.CompanyId}/action-insights/queue");

        Assert.Contains(items!, item => item.TargetType == "approval" && item.DeepLink == $"/approvals?companyId={seed.CompanyId:D}&approvalId={item.TargetId:D}");
        Assert.Contains(items!, item => item.TargetType == "task" && item.DeepLink == $"/tasks?companyId={seed.CompanyId:D}&taskId={item.TargetId:D}");
        Assert.Contains(items!, item => item.TargetType == "workflow" && item.DeepLink == $"/workflows?companyId={seed.CompanyId:D}&workflowInstanceId={item.TargetId:D}");
    }

    [Fact]
    public async Task Acknowledgment_persists_for_same_user_and_does_not_leak()
    {
        var seed = await SeedActionQueueAsync();
        using var founder = CreateAuthenticatedClient("founder", "founder@example.com");
        using var otherUser = CreateAuthenticatedClient("other-user", "other@example.com");

        var initial = await founder.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.CompanyId}/action-insights/queue");
        var target = Assert.Single(initial!.Where(item => item.Type == "approval"));

        var acknowledge = await founder.PostAsync(
            $"/api/companies/{seed.CompanyId}/action-insights/{Uri.EscapeDataString(target.InsightKey)}/acknowledgment",
            null);

        Assert.Equal(HttpStatusCode.OK, acknowledge.StatusCode);

        var refreshed = await founder.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.CompanyId}/action-insights/queue");
        Assert.True(refreshed!.Single(item => item.InsightKey == target.InsightKey).IsAcknowledged);

        var otherUserQueue = await otherUser.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.CompanyId}/action-insights/queue");
        Assert.False(otherUserQueue!.Single(item => item.InsightKey == target.InsightKey).IsAcknowledged);

        var otherTenantQueue = await founder.GetFromJsonAsync<List<ActionQueueItemResponse>>($"/api/companies/{seed.OtherCompanyId}/action-insights/queue");
        Assert.DoesNotContain(otherTenantQueue!, item => item.InsightKey == target.InsightKey);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, subject);
        return client;
    }

    private async Task<ActionQueueSeed> SeedActionQueueAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, "founder@example.com", "Founder", "dev-header", "founder"),
                new User(otherUserId, "other@example.com", "Other", "dev-header", "other-user"));
            dbContext.Companies.AddRange(new Company(companyId, "Action Queue Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, otherUserId, CompanyMembershipRole.Member, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Operations Lead", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var task = new WorkTask(
                taskId,
                companyId,
                "approval-test",
                "Approve contract",
                "Contract needs approval.",
                WorkTaskPriority.High,
                agentId,
                null,
                "user",
                userId);
            task.SetDueDate(DateTime.UtcNow.AddHours(2));
            dbContext.WorkTasks.Add(task);

            dbContext.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(
                approvalId,
                companyId,
                ApprovalTargetEntityType.Task,
                taskId,
                "user",
                userId,
                "threshold",
                new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(10000) },
                "owner",
                null,
                []));

            var definition = new WorkflowDefinition(
                workflowDefinitionId,
                companyId,
                "FULFILLMENT",
                "Fulfillment workflow",
                "Operations",
                WorkflowTriggerType.Manual,
                1,
                new Dictionary<string, JsonNode?> { ["steps"] = new JsonArray("review") });
            var instance = new WorkflowInstance(workflowInstanceId, companyId, workflowDefinitionId, null, currentStep: "review");
            instance.UpdateState(WorkflowInstanceStatus.Blocked, "review");
            dbContext.WorkflowDefinitions.Add(definition);
            dbContext.WorkflowInstances.Add(instance);

            dbContext.Alerts.AddRange(
                new Alert(
                    Guid.NewGuid(),
                    companyId,
                    AlertType.Risk,
                    AlertSeverity.High,
                    "Margin at risk",
                    "Gross margin dropped below threshold.",
                    new Dictionary<string, JsonNode?> { ["metric"] = JsonValue.Create("margin") },
                    "risk-corr",
                    "risk-fp",
                    AlertStatus.Open,
                    agentId),
                new Alert(
                    Guid.NewGuid(),
                    companyId,
                    AlertType.Opportunity,
                    AlertSeverity.Medium,
                    "Upsell opportunity",
                    "Expansion signal detected.",
                    new Dictionary<string, JsonNode?> { ["metric"] = JsonValue.Create("usage") },
                    "opp-corr",
                    "opp-fp",
                    AlertStatus.Open,
                    agentId),
                new Alert(
                    Guid.NewGuid(),
                    otherCompanyId,
                    AlertType.Risk,
                    AlertSeverity.Critical,
                    "Other tenant risk",
                    "Should not leak.",
                    new Dictionary<string, JsonNode?> { ["metric"] = JsonValue.Create("other") },
                    "other-corr",
                    "other-fp",
                    AlertStatus.Open));

            return Task.CompletedTask;
        });

        return new ActionQueueSeed(companyId, otherCompanyId);
    }

    private sealed record ActionQueueSeed(Guid CompanyId, Guid OtherCompanyId);

    private sealed class ActionQueueItemResponse
    {
        public string InsightKey { get; set; } = string.Empty;
        public Guid CompanyId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string SourceEntityType { get; set; } = string.Empty;
        public Guid SourceEntityId { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public DateTime? DueUtc { get; set; }
        public string SlaState { get; set; } = string.Empty;
        public int PriorityScore { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string DeepLink { get; set; } = string.Empty;
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string StableSortKey { get; set; } = string.Empty;
    }
}