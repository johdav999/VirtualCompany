using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ActivityFeedFilteringIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActivityFeedFilteringIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Feed_applies_top_filter_combinations_with_and_semantics()
    {
        var seed = await SeedFilterTenantAsync();
        using var client = CreateAuthenticatedClient();

        var agentAndTime = await GetFeedAsync(client, seed.CompanyId, $"agentId={seed.FinanceAgentId}&from={Escape(seed.BaseTime.AddMinutes(-1))}&to={Escape(seed.BaseTime.AddMinutes(2))}");
        Assert.Equal([seed.FinanceStartedId, seed.FinanceCompletedId], agentAndTime.Items.Select(x => x.EventId).ToArray());
        Assert.All(agentAndTime.Items, item =>
        {
            Assert.Equal(seed.FinanceAgentId, item.AgentId);
            Assert.InRange(item.OccurredAt, seed.BaseTime.AddMinutes(-1), seed.BaseTime.AddMinutes(2).AddTicks(-1));
        });

        var departmentAndEventType = await GetFeedAsync(client, seed.CompanyId, "department=Finance&eventType=task_completed");
        var financeCompleted = Assert.Single(departmentAndEventType.Items);
        Assert.Equal(seed.FinanceCompletedId, financeCompleted.EventId);
        Assert.Equal("Finance", financeCompleted.Department);
        Assert.Equal("task_completed", financeCompleted.EventType);

        var taskAndStatus = await GetFeedAsync(client, seed.CompanyId, $"task={seed.OpsTaskId}&status=failed");
        var opsFailed = Assert.Single(taskAndStatus.Items);
        Assert.Equal(seed.OpsFailedId, opsFailed.EventId);
        Assert.Equal(seed.OpsTaskId, opsFailed.TaskId);
        Assert.Equal("failed", opsFailed.Status);

        var agentDepartmentStatus = await GetFeedAsync(client, seed.CompanyId, $"agentId={seed.FinanceAgentId}&department=Finance&status=running");
        var financeRunning = Assert.Single(agentDepartmentStatus.Items);
        Assert.Equal(seed.FinanceStartedId, financeRunning.EventId);

        var eventStatusTime = await GetFeedAsync(client, seed.CompanyId, $"eventType=tool_execution_started&status=running&from={Escape(seed.BaseTime.AddMinutes(1))}&to={Escape(seed.BaseTime.AddMinutes(5))}");
        var toolStarted = Assert.Single(eventStatusTime.Items);
        Assert.Equal(seed.OpsToolStartedId, toolStarted.EventId);
        Assert.Equal("tool_execution_started", toolStarted.EventType);
        Assert.Equal("running", toolStarted.Status);

        var legacyTaskAlias = await GetFeedAsync(client, seed.CompanyId, $"taskId={seed.OpsTaskId}&status=failed");
        var legacyOpsFailed = Assert.Single(legacyTaskAlias.Items);
        Assert.Equal(seed.OpsFailedId, legacyOpsFailed.EventId);
        Assert.Equal(seed.OpsTaskId, legacyOpsFailed.TaskId);
    }

    [Fact]
    public async Task Feed_applies_all_supported_filters_together()
    {
        var seed = await SeedFilterTenantAsync();
        using var client = CreateAuthenticatedClient();

        var result = await GetFeedAsync(
            client,
            seed.CompanyId,
            $"agentId={seed.FinanceAgentId}" +
            "&department=Finance" +
            $"&task={seed.FinanceTaskId}" +
            "&eventType=task_completed" +
            "&status=completed" +
            $"&from={Escape(seed.BaseTime)}" +
            $"&to={Escape(seed.BaseTime.AddMinutes(2))}");

        var item = Assert.Single(result.Items);
        Assert.Equal(seed.FinanceCompletedId, item.EventId);
        Assert.Equal(seed.FinanceAgentId, item.AgentId);
        Assert.Equal("Finance", item.Department);
        Assert.Equal(seed.FinanceTaskId, item.TaskId);
        Assert.Equal("task_completed", item.EventType);
        Assert.Equal("completed", item.Status);
        Assert.InRange(item.OccurredAt, seed.BaseTime, seed.BaseTime.AddMinutes(2).AddTicks(-1));
    }

    [Fact]
    public async Task Feed_clear_filters_returns_broader_result_set()
    {
        var seed = await SeedFilterTenantAsync();
        using var client = CreateAuthenticatedClient();

        var filtered = await GetFeedAsync(client, seed.CompanyId, "department=Finance&eventType=task_completed");
        var unfiltered = await GetFeedAsync(client, seed.CompanyId, "pageSize=20");

        Assert.Single(filtered.Items);
        Assert.True(unfiltered.Items.Count > filtered.Items.Count);
        Assert.Contains(unfiltered.Items, item => item.EventId == filtered.Items[0].EventId);
    }

    [Fact]
    public async Task Feed_rejects_invalid_date_ranges()
    {
        var seed = await SeedFilterTenantAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/activity-feed?from={Escape(seed.BaseTime.AddMinutes(5))}&to={Escape(seed.BaseTime)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Activity_detail_contract_exposes_summary_payload_correlation_links_and_audit_link()
    {
        var seed = await SeedAuditLinkTenantAsync();
        using var client = CreateAuthenticatedClient();

        var feed = await GetFeedAsync(client, seed.CompanyId, "pageSize=20");
        var linked = Assert.Single(feed.Items.Where(x => x.EventId == seed.LinkedActivityId));

        Assert.Equal("Ops completed tool execution", linked.Summary);
        Assert.Equal("tool_execution_completed", linked.NormalizedSummary.EventType);
        Assert.Contains("Operations Agent", linked.NormalizedSummary.SummaryText);
        Assert.Contains("Payroll export", linked.NormalizedSummary.SummaryText);
        Assert.Equal("audit-link-corr", linked.CorrelationId);
        Assert.Equal(seed.TaskId, linked.TaskId);
        Assert.Equal("Payroll export", linked.RawPayload["targetName"].GetString());
        Assert.Equal("Operations Agent", linked.RawPayload["actor"].GetString());
        Assert.Equal(seed.AuditEventId.ToString(), linked.RawPayload["auditEventId"].GetString());
        Assert.NotNull(linked.AuditLink);
        Assert.Equal(seed.AuditEventId, linked.AuditLink!.AuditEventId);
        Assert.Equal($"/audit/{seed.AuditEventId}?companyId={seed.CompanyId}", linked.AuditLink.Href);

        var correlation = await client.GetFromJsonAsync<ActivityCorrelationTimelineResponse>(
            $"/api/companies/{seed.CompanyId}/activity-feed/{linked.EventId}/correlation");

        Assert.NotNull(correlation);
        Assert.Equal(seed.CompanyId, correlation!.TenantId);
        Assert.Equal("audit-link-corr", correlation.CorrelationId);
        Assert.NotNull(correlation.SelectedActivityLinks);
        Assert.Equal(linked.EventId, correlation.SelectedActivityLinks!.ActivityEventId);
        var taskLink = Assert.Single(correlation.SelectedActivityLinks.LinkedEntities.Where(x => x.EntityType == "task"));
        Assert.Equal(seed.TaskId, taskLink.EntityId);
        Assert.True(taskLink.IsAvailable);
        Assert.Equal("Payroll export", taskLink.DisplayText);

        var unlinked = Assert.Single(feed.Items.Where(x => x.EventId == seed.CrossTenantActivityId));
        Assert.Null(unlinked.AuditLink);
        Assert.Equal(seed.TaskId, unlinked.TaskId);
        Assert.Equal("Other tenant audit reference", unlinked.RawPayload["targetName"].GetString());
        Assert.Equal(seed.OtherAuditEventId.ToString(), unlinked.RawPayload["auditEventId"].GetString());
    }

    [Fact]
    public async Task Activity_audit_deep_link_is_tenant_scoped_and_resolves_to_audit_detail()
    {
        var seed = await SeedAuditLinkTenantAsync();
        using var client = CreateAuthenticatedClient();

        var feed = await GetFeedAsync(client, seed.CompanyId, "pageSize=20");
        var linked = Assert.Single(feed.Items.Where(x => x.EventId == seed.LinkedActivityId));
        Assert.NotNull(linked.AuditLink);
        Assert.Equal(seed.AuditEventId, linked.AuditLink!.AuditEventId);
        Assert.Equal($"/audit/{seed.AuditEventId}?companyId={seed.CompanyId}", linked.AuditLink.Href);

        var detail = await client.GetFromJsonAsync<AuditDetailResponse>(
            $"/api/companies/{seed.CompanyId}/audit/{linked.AuditLink.AuditEventId}");

        Assert.NotNull(detail);
        Assert.Equal(seed.AuditEventId, detail!.Id);
        Assert.Equal(seed.CompanyId, detail.CompanyId);
        Assert.Equal(AuditEventActions.AgentToolExecutionExecuted, detail.Action);
        Assert.Equal(AuditTargetTypes.Agent, detail.TargetType);
        Assert.Equal(seed.AgentId.ToString(), detail.TargetId);

        var crossTenant = Assert.Single(feed.Items.Where(x => x.EventId == seed.CrossTenantActivityId));
        Assert.Null(crossTenant.AuditLink);
        var crossTenantDetail = await client.GetAsync($"/api/companies/{seed.CompanyId}/audit/{seed.OtherAuditEventId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDetail.StatusCode);
    }

    private async Task<FilterSeed> SeedFilterTenantAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var financeAgentId = Guid.NewGuid();
        var opsAgentId = Guid.NewGuid();
        var financeTaskId = Guid.NewGuid();
        var opsTaskId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);
        var financeStartedId = Guid.NewGuid();
        var financeCompletedId = Guid.NewGuid();
        var opsFailedId = Guid.NewGuid();
        var opsToolStartedId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "activity-filter-owner@example.com", "Activity Filter Owner", "dev-header", "activity-filter-owner"));
            dbContext.Companies.AddRange(new Company(companyId, "Activity Filter Tenant"), new Company(otherCompanyId, "Other Activity Filter Tenant"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(financeAgentId, companyId, "finance", "Finance Agent", "Finance Agent", "Finance", null, AgentSeniority.Mid),
                new Agent(opsAgentId, companyId, "ops", "Ops Agent", "Operations Agent", "Operations", null, AgentSeniority.Mid));
            dbContext.WorkTasks.AddRange(
                new WorkTask(financeTaskId, companyId, "finance", "Review invoice", null, WorkTaskPriority.Normal, financeAgentId, null, "user", userId),
                new WorkTask(opsTaskId, companyId, "ops", "Recover deployment", null, WorkTaskPriority.High, opsAgentId, null, "user", userId));
            dbContext.ActivityEvents.AddRange(
                new ActivityEvent(
                    financeStartedId,
                    companyId,
                    financeAgentId,
                    "task_started",
                    baseTime,
                    "running",
                    "Finance started invoice review",
                    "filter-corr-1",
                    Source(financeTaskId, "Finance"),
                    "Finance",
                    financeTaskId),
                new ActivityEvent(
                    financeCompletedId,
                    companyId,
                    financeAgentId,
                    "task_completed",
                    baseTime.AddMinutes(1),
                    "completed",
                    "Finance completed invoice review",
                    "filter-corr-1",
                    Source(financeTaskId, "Finance"),
                    "Finance",
                    financeTaskId),
                new ActivityEvent(
                    opsFailedId,
                    companyId,
                    opsAgentId,
                    "task_failed",
                    baseTime.AddMinutes(2),
                    "failed",
                    "Ops failed deployment recovery",
                    "filter-corr-2",
                    Source(opsTaskId, "Operations"),
                    "Operations",
                    opsTaskId),
                new ActivityEvent(
                    opsToolStartedId,
                    companyId,
                    opsAgentId,
                    "tool_execution_started",
                    baseTime.AddMinutes(3),
                    "running",
                    "Ops started rollback tool",
                    "filter-corr-2",
                    Source(opsTaskId, "Operations"),
                    "Operations",
                    opsTaskId),
                new ActivityEvent(
                    Guid.NewGuid(),
                    otherCompanyId,
                    null,
                    "task_completed",
                    baseTime.AddMinutes(4),
                    "completed",
                    "Other tenant event",
                    "filter-corr-other",
                    Source(Guid.NewGuid(), "Finance"),
                    "Finance",
                    Guid.NewGuid()));
            return Task.CompletedTask;
        });

        return new FilterSeed(companyId, financeAgentId, opsAgentId, financeTaskId, opsTaskId, baseTime, financeStartedId, financeCompletedId, opsFailedId, opsToolStartedId);
    }

    private async Task<AuditLinkSeed> SeedAuditLinkTenantAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var otherAuditEventId = Guid.NewGuid();
        var linkedActivityId = Guid.NewGuid();
        var crossTenantActivityId = Guid.NewGuid();
        var occurred = new DateTime(2026, 4, 15, 13, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "activity-filter-owner@example.com", "Activity Filter Owner", "dev-header", "activity-filter-owner"));
            dbContext.Companies.AddRange(new Company(companyId, "Activity Audit Tenant"), new Company(otherCompanyId, "Other Activity Audit Tenant"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Ops Agent", "Operations Agent", "Operations", null, AgentSeniority.Mid));
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                companyId,
                "ops",
                "Payroll export",
                "Task linked from activity detail.",
                WorkTaskPriority.High,
                agentId,
                null,
                "user",
                userId));
            dbContext.AuditEvents.AddRange(
                new AuditEvent(
                    auditEventId,
                    companyId,
                    AuditActorTypes.Agent,
                    agentId,
                    AuditEventActions.AgentToolExecutionExecuted,
                    AuditTargetTypes.Agent,
                    agentId.ToString(),
                    AuditEventOutcomes.Succeeded,
                    "Tool execution was approved.",
                    occurredUtc: occurred),
                new AuditEvent(
                    otherAuditEventId,
                    otherCompanyId,
                    AuditActorTypes.System,
                    null,
                    AuditEventActions.AgentToolExecutionExecuted,
                    AuditTargetTypes.Agent,
                    Guid.NewGuid().ToString(),
                    AuditEventOutcomes.Succeeded,
                    "Other tenant audit.",
                    occurredUtc: occurred));
            dbContext.ActivityEvents.AddRange(
                new ActivityEvent(
                    linkedActivityId,
                    companyId,
                    agentId,
                    "tool_execution_completed",
                    occurred,
                    "completed",
                    "Ops completed tool execution",
                    "audit-link-corr",
                    SourceWithAudit(auditEventId, taskId, "Operations Agent", "Payroll export"),
                    "Operations",
                    taskId,
                    auditEventId),
                new ActivityEvent(
                    crossTenantActivityId,
                    companyId,
                    agentId,
                    "tool_execution_completed",
                    occurred.AddMinutes(-1),
                    "completed",
                    "Ops completed unlinked tool execution",
                    "audit-link-corr",
                    SourceWithAudit(otherAuditEventId, taskId, "Operations Agent", "Other tenant audit reference"),
                    "Operations",
                    taskId,
                    otherAuditEventId));
            return Task.CompletedTask;
        });

        return new AuditLinkSeed(companyId, agentId, taskId, auditEventId, otherAuditEventId, linkedActivityId, crossTenantActivityId);
    }

    private async Task<ActivityFeedResponse> GetFeedAsync(HttpClient client, Guid companyId, string query)
    {
        var separator = string.IsNullOrWhiteSpace(query) ? string.Empty : $"&{query}";
        var result = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{companyId}/activity-feed?pageSize=20{separator}");
        Assert.NotNull(result);
        return result!;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "activity-filter-owner");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "activity-filter-owner@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Activity Filter Owner");
        return client;
    }

    private static Dictionary<string, JsonNode?> Source(Guid taskId, string department) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = JsonValue.Create("task"),
            ["sourceId"] = JsonValue.Create(taskId),
            ["taskId"] = JsonValue.Create(taskId),
            ["department"] = JsonValue.Create(department)
        };

    private static Dictionary<string, JsonNode?> SourceWithAudit(Guid auditEventId, Guid taskId, string actor, string targetName) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = JsonValue.Create("tool_execution"),
            ["auditEventId"] = JsonValue.Create(auditEventId),
            ["taskId"] = JsonValue.Create(taskId),
            ["actor"] = JsonValue.Create(actor),
            ["targetName"] = JsonValue.Create(targetName),
            ["linkedEntities"] = new JsonArray
            {
                new JsonObject
                {
                    ["entityType"] = JsonValue.Create("task"),
                    ["entityId"] = JsonValue.Create(taskId)
                }
            }
        };

    private static string Escape(DateTime value) =>
        Uri.EscapeDataString(value.ToString("O"));

    private sealed record FilterSeed(
        Guid CompanyId,
        Guid FinanceAgentId,
        Guid OpsAgentId,
        Guid FinanceTaskId,
        Guid OpsTaskId,
        DateTime BaseTime,
        Guid FinanceStartedId,
        Guid FinanceCompletedId,
        Guid OpsFailedId,
        Guid OpsToolStartedId);

    private sealed record AuditLinkSeed(
        Guid CompanyId,
        Guid AgentId,
        Guid TaskId,
        Guid AuditEventId,
        Guid OtherAuditEventId,
        Guid LinkedActivityId,
        Guid CrossTenantActivityId);

    private sealed record ActivityFeedResponse(IReadOnlyList<ActivityEventResponse> Items, string? NextCursor);

    private sealed record ActivityEventResponse(
        Guid EventId,
        Guid TenantId,
        Guid? AgentId,
        string EventType,
        DateTime OccurredAt,
        string Status,
        string Summary,
        string? CorrelationId,
        string? Department,
        Guid? TaskId,
        ActivityAuditLinkResponse? AuditLink,
        Dictionary<string, JsonElement> RawPayload,
        ActivitySummaryResponse NormalizedSummary);

    private sealed record ActivityAuditLinkResponse(Guid AuditEventId, string Href);

    private sealed record ActivitySummaryResponse(
        string EventType,
        string FormatterKey,
        string? Actor,
        string Action,
        string? Target,
        string? Outcome,
        string SummaryText,
        string Text);

    private sealed record ActivityCorrelationTimelineResponse(
        Guid TenantId,
        string CorrelationId,
        IReadOnlyList<ActivityTimelineItemResponse> Items,
        ActivityLinkedEntitiesResponse? SelectedActivityLinks);

    private sealed record ActivityTimelineItemResponse(ActivityEventResponse Activity);

    private sealed record ActivityLinkedEntitiesResponse(
        Guid TenantId,
        Guid ActivityEventId,
        IReadOnlyList<ActivityLinkedEntityResponse> LinkedEntities);

    private sealed record ActivityLinkedEntityResponse(
        string EntityType,
        Guid EntityId,
        string DisplayText,
        bool IsAvailable);

    private sealed record AuditDetailResponse(Guid Id, Guid CompanyId, string Action, string TargetType, string TargetId);
}