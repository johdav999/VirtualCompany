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

    [Fact]
    public async Task Top_endpoint_returns_first_five_items_using_required_priority_order()
    {
        var seed = await SeedPrioritizedTaskQueueAsync();
        using var client = CreateAuthenticatedClient("founder", "founder@example.com");

        var items = await client.GetFromJsonAsync<List<ActionQueueItemResponse>>(
            $"/api/companies/{seed.CompanyId}/action-insights/top?count=5");

        Assert.NotNull(items);
        Assert.Equal(
            new[]
            {
                "Overdue critical release",
                "Due soon critical release",
                "Due soon normal supplier task",
                "Due soon low supplier task",
                "Medium due follow-up"
            },
            items!.Select(item => item.Title).ToArray());
        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task Paginated_endpoint_returns_stable_non_overlapping_pages()
    {
        var seed = await SeedPrioritizedTaskQueueAsync();
        using var client = CreateAuthenticatedClient("founder", "founder@example.com");

        var pageOne = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?pageNumber=1&pageSize=2");
        var pageTwo = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?pageNumber=2&pageSize=2");
        var repeatedPageTwo = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?pageNumber=2&pageSize=2");
        var otherTenantPage = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.OtherCompanyId}/action-insights?pageNumber=1&pageSize=10");

        Assert.NotNull(pageOne);
        Assert.NotNull(pageTwo);
        Assert.NotNull(repeatedPageTwo);
        Assert.Equal(7, pageOne!.TotalCount);
        Assert.Equal(4, pageOne.TotalPages);
        Assert.Equal(pageTwo!.Items.Select(item => item.InsightKey), repeatedPageTwo!.Items.Select(item => item.InsightKey));
        Assert.Empty(pageOne.Items.Select(item => item.InsightKey).Intersect(pageTwo.Items.Select(item => item.InsightKey)));
        Assert.Equal(new[] { "Due soon normal supplier task", "Due soon low supplier task" }, pageTwo.Items.Select(item => item.Title).ToArray());
        Assert.Empty(otherTenantPage!.Items);
    }

    [Fact]
    public async Task Paginated_endpoint_accepts_page_alias_and_uses_stable_source_id_tie_breaker()
    {
        var seed = await SeedDeterministicPaginationQueueAsync();
        using var client = CreateAuthenticatedClient("founder", "founder@example.com");

        var pageOne = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?page=1&pageSize=2");
        var pageTwo = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?page=2&pageSize=2");
        var repeatedPageTwo = await client.GetFromJsonAsync<ActionQueuePageResponse>(
            $"/api/companies/{seed.CompanyId}/action-insights?page=2&pageSize=2");

        Assert.NotNull(pageOne);
        Assert.NotNull(pageTwo);
        Assert.NotNull(repeatedPageTwo);
        Assert.Equal(new[] { "Tied action 1", "Tied action 2" }, pageOne!.Items.Select(item => item.Title).ToArray());
        Assert.Equal(new[] { "Tied action 3", "Tied action 4" }, pageTwo!.Items.Select(item => item.Title).ToArray());
        Assert.Equal(pageTwo.Items.Select(item => item.InsightKey), repeatedPageTwo!.Items.Select(item => item.InsightKey));
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

    private async Task<ActionQueueSeed> SeedPrioritizedTaskQueueAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Priority Queue Company"),
                new Company(otherCompanyId, "Other Priority Queue Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Operations Lead", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            dbContext.WorkTasks.AddRange(
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Overdue critical release", WorkTaskPriority.High, DateTime.UtcNow.AddHours(-1)),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Due soon critical release", WorkTaskPriority.High, DateTime.UtcNow.AddHours(1)),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Due soon normal supplier task", WorkTaskPriority.Normal, DateTime.UtcNow.AddHours(2)),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Due soon low supplier task", WorkTaskPriority.Low, DateTime.UtcNow.AddHours(2)),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Medium due follow-up", WorkTaskPriority.Low, DateTime.UtcNow.AddHours(12)),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Medium no due follow-up", WorkTaskPriority.High, null),
                BuildTask(Guid.NewGuid(), companyId, userId, agentId, "Low no due follow-up", WorkTaskPriority.Low, null),
                BuildTask(Guid.NewGuid(), otherCompanyId, userId, null, "Other tenant follow-up", WorkTaskPriority.High, DateTime.UtcNow.AddHours(1)));

            return Task.CompletedTask;
        });

        return new ActionQueueSeed(companyId, otherCompanyId);
    }

    private async Task<ActionQueueSeed> SeedDeterministicPaginationQueueAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dueUtc = new DateTime(2026, 4, 14, 16, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Deterministic Queue Company"),
                new Company(otherCompanyId, "Other Deterministic Queue Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.WorkTasks.AddRange(
                BuildTask(Guid.Parse("11111111-1111-1111-1111-111111111111"), companyId, userId, null, "Tied action 1", WorkTaskPriority.High, dueUtc),
                BuildTask(Guid.Parse("22222222-2222-2222-2222-222222222222"), companyId, userId, null, "Tied action 2", WorkTaskPriority.High, dueUtc),
                BuildTask(Guid.Parse("33333333-3333-3333-3333-333333333333"), companyId, userId, null, "Tied action 3", WorkTaskPriority.High, dueUtc),
                BuildTask(Guid.Parse("44444444-4444-4444-4444-444444444444"), companyId, userId, null, "Tied action 4", WorkTaskPriority.High, dueUtc),
                BuildTask(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), otherCompanyId, userId, null, "Other tenant tied action", WorkTaskPriority.High, dueUtc));

            return Task.CompletedTask;
        });

        return new ActionQueueSeed(companyId, otherCompanyId);
    }

    private static WorkTask BuildTask(
        Guid taskId,
        Guid companyId,
        Guid userId,
        Guid? assignedAgentId,
        string title,
        WorkTaskPriority priority,
        DateTime? dueUtc)
    {
        var task = new WorkTask(
            taskId,
            companyId,
            "queue-test",
            title,
            "Priority ordering test task.",
            priority,
            assignedAgentId,
            null,
            "user",
            userId);

        if (dueUtc.HasValue)
        {
            task.SetDueDate(dueUtc.Value);
        }

        return task;
    }

    private sealed record ActionQueueSeed(Guid CompanyId, Guid OtherCompanyId);

    private sealed class ActionQueuePageResponse
    {
        public List<ActionQueueItemResponse> Items { get; set; } = [];
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

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
        public int ImpactScore { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string DeepLink { get; set; } = string.Empty;
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string StableSortKey { get; set; } = string.Empty;
    }
}