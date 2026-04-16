using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Authentication;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class ActivityCorrelationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActivityCorrelationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Correlation_timeline_returns_ordered_events_with_resolved_linked_entities()
    {
        var seed = await SeedAsync();
        using var client = CreateClient();

        await PersistActivityAsync(client, seed.CompanyId, "task_started", seed.CorrelationId, seed.TaskId, occurredAt: DateTime.UtcNow.AddMinutes(-3));
        var selected = await PersistActivityAsync(client, seed.CompanyId, "tool_executed", seed.CorrelationId, seed.TaskId, seed.WorkflowInstanceId, seed.ApprovalId, seed.ToolExecutionId, DateTime.UtcNow.AddMinutes(-2));
        await PersistActivityAsync(client, seed.CompanyId, "task_completed", seed.CorrelationId, seed.TaskId, occurredAt: DateTime.UtcNow.AddMinutes(-1));
        await PersistActivityAsync(client, seed.CompanyId, "other", "different-correlation", seed.TaskId, occurredAt: DateTime.UtcNow);

        var timeline = await client.GetFromJsonAsync<ActivityCorrelationTimelineDto>(
            $"/api/companies/{seed.CompanyId}/activity-feed/correlations/{seed.CorrelationId}?selectedActivityEventId={selected.EventId}");

        Assert.NotNull(timeline);
        Assert.Equal(seed.CorrelationId, timeline!.CorrelationId);
        Assert.Equal(3, timeline.Items.Count);
        Assert.Equal(["task_started", "tool_executed", "task_completed"], timeline.Items.Select(x => x.Activity.EventType).ToArray());
        Assert.True(timeline.Items[1].Activity.RawPayload.ContainsKey("taskId"));
        Assert.Equal("fallback", timeline.Items[1].Activity.NormalizedSummary.FormatterKey);
        Assert.False(string.IsNullOrWhiteSpace(timeline.Items[1].Activity.NormalizedSummary.SummaryText));
        Assert.DoesNotContain("null", timeline.Items[1].Activity.NormalizedSummary.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undefined", timeline.Items[1].Activity.NormalizedSummary.SummaryText, StringComparison.OrdinalIgnoreCase);

        var selectedLinks = Assert.NotNull(timeline.SelectedActivityLinks);
        Assert.Contains(selectedLinks.LinkedEntities, x => x.EntityType == ActivityEntityTypes.Task && x.CurrentStatus == WorkTaskStatus.InProgress.ToStorageValue());
        Assert.Contains(selectedLinks.LinkedEntities, x => x.EntityType == ActivityEntityTypes.WorkflowInstance && x.CurrentStatus == WorkflowInstanceStatus.Running.ToStorageValue());
        Assert.Contains(selectedLinks.LinkedEntities, x => x.EntityType == ActivityEntityTypes.Approval && x.CurrentStatus == ApprovalRequestStatus.Pending.ToStorageValue());
        Assert.Contains(selectedLinks.LinkedEntities, x => x.EntityType == ActivityEntityTypes.ToolExecution && x.CurrentStatus == ToolExecutionStatus.Started.ToStorageValue());
    }

    [Fact]
    public async Task Selecting_activity_item_resolves_correlation_timeline()
    {
        var seed = await SeedAsync();
        using var client = CreateClient();

        await PersistActivityAsync(client, seed.CompanyId, "task_started", seed.CorrelationId, seed.TaskId, occurredAt: DateTime.UtcNow.AddMinutes(-2));
        var selected = await PersistActivityAsync(client, seed.CompanyId, "tool_executed", seed.CorrelationId, seed.TaskId, seed.WorkflowInstanceId, seed.ApprovalId, seed.ToolExecutionId, DateTime.UtcNow.AddMinutes(-1));

        var timeline = await client.GetFromJsonAsync<ActivityCorrelationTimelineDto>(
            $"/api/companies/{seed.CompanyId}/activity-feed/{selected.EventId}/correlation");

        Assert.NotNull(timeline);
        Assert.Equal(seed.CorrelationId, timeline!.CorrelationId);
        Assert.Equal(2, timeline.Items.Count);
        Assert.NotNull(timeline.SelectedActivityLinks);
        Assert.Equal(selected.EventId, timeline.SelectedActivityLinks!.ActivityEventId);
        Assert.Contains(
            timeline.SelectedActivityLinks.LinkedEntities,
            x => x.EntityType == ActivityEntityTypes.ToolExecution &&
                 x.IsAvailable &&
                 x.UnavailableReason is null);
    }

    [Fact]
    public async Task Selecting_missing_activity_item_returns_not_found()
    {
        var seed = await SeedAsync();
        using var client = CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/activity-feed/{Guid.NewGuid()}/correlation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Linked_entities_endpoint_represents_missing_entities_explicitly()
    {
        var seed = await SeedAsync();
        using var client = CreateClient();
        var missingTaskId = Guid.NewGuid();
        var activity = await PersistActivityAsync(client, seed.CompanyId, "missing_task", seed.CorrelationId, missingTaskId, occurredAt: DateTime.UtcNow);

        var links = await client.GetFromJsonAsync<ActivityLinkedEntitiesDto>(
            $"/api/companies/{seed.CompanyId}/activity-feed/{activity.EventId}/links");

        var missing = Assert.Single(links!.LinkedEntities);
        Assert.Equal(ActivityEntityTypes.Task, missing.EntityType);
        Assert.Equal(missingTaskId, missing.EntityId);
        Assert.Equal(ActivityLinkedEntityAvailability.UnavailableMissing, missing.Availability);
        Assert.False(missing.IsAvailable);
        Assert.Equal("missing", missing.UnavailableReason);
        Assert.Null(missing.CurrentStatus);
    }

    [Fact]
    public async Task Correlation_timeline_is_tenant_scoped()
    {
        var seed = await SeedAsync(includeOtherMembership: false);
        using var client = CreateClient();

        await PersistActivityAsync(client, seed.OtherCompanyId, "other_tenant", seed.CorrelationId, seed.OtherTaskId, occurredAt: DateTime.UtcNow);

        var response = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/activity-feed/correlations/{seed.CorrelationId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Correlation_timeline_uses_bounded_query_shape_for_large_correlations()
    {
        var seed = await SeedAsync();
        var started = DateTime.UtcNow.AddMinutes(-20);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var events = Enumerable.Range(0, 10_000)
                .Select(i => new ActivityEvent(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.AgentId,
                    "bulk_event",
                    started.AddMilliseconds(i),
                    "running",
                    $"Bulk activity {i}",
                    seed.CorrelationId,
                    new Dictionary<string, JsonNode?>
                    {
                        ["taskId"] = JsonValue.Create(seed.TaskId)
                    }))
                .ToArray();

            dbContext.ActivityEvents.AddRange(events);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateClient();
        var stopwatch = Stopwatch.StartNew();
        var timeline = await client.GetFromJsonAsync<ActivityCorrelationTimelineDto>(
            $"/api/companies/{seed.CompanyId}/activity-feed/correlations/{seed.CorrelationId}");
        stopwatch.Stop();

        Assert.Equal(10_000, timeline!.Items.Count);
        Assert.All(timeline.Items, item => Assert.Single(item.LinkedEntities));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Correlation timeline took {stopwatch.Elapsed.TotalMilliseconds}ms.");
    }

    private async Task<SeedContext> SeedAsync(bool includeOtherMembership = true)
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var otherTaskId = Guid.NewGuid();
        var toolExecutionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        dbContext.Users.Add(new User(userId, "activity-correlation@example.com", "Activity Correlation", "dev-header", "activity-correlation"));
        dbContext.Companies.AddRange(new Company(companyId, "Activity Correlation Tenant"), new Company(otherCompanyId, "Other Activity Correlation Tenant"));
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        if (includeOtherMembership)
        {
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        }

        dbContext.Agents.Add(new Agent(agentId, companyId, "Ops Agent", "Operations", "Operations", AgentSeniority.Mid));
        dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
            definitionId,
            companyId,
            "CORRELATION_TEST",
            "Correlation workflow",
            "Operations",
            WorkflowTriggerType.Manual,
            1,
            new Dictionary<string, JsonNode?> { ["steps"] = new JsonArray() }));
        var workflow = new WorkflowInstance(workflowInstanceId, companyId, definitionId, null);
        workflow.UpdateState(WorkflowInstanceStatus.Running, "review");
        dbContext.WorkflowInstances.Add(workflow);

        var task = new WorkTask(
            taskId,
            companyId,
            "correlation_test",
            "Correlated task",
            "Task used by correlation tests.",
            WorkTaskPriority.Normal,
            agentId,
            null,
            "user",
            userId,
            workflowInstanceId: workflowInstanceId,
            correlationId: "corr-activity-correlation",
            status: WorkTaskStatus.InProgress);
        dbContext.WorkTasks.Add(task);

        dbContext.WorkTasks.Add(new WorkTask(
            otherTaskId,
            otherCompanyId,
            "correlation_test",
            "Other correlated task",
            null,
            WorkTaskPriority.Normal,
            null,
            null,
            "user",
            userId,
            correlationId: "corr-activity-correlation"));

        var execution = new ToolExecutionAttempt(
            toolExecutionId,
            companyId,
            agentId,
            "send_invoice",
            ToolActionType.Execute,
            "finance",
            taskId: taskId,
            workflowInstanceId: workflowInstanceId,
            correlationId: "corr-activity-correlation");
        dbContext.ToolExecutionAttempts.Add(execution);

        var approval = ApprovalRequest.CreateForTarget(
            approvalId,
            companyId,
            ApprovalTargetEntityType.Action,
            toolExecutionId,
            "user",
            userId,
            "tool_execution",
            new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("threshold") },
            "owner",
            null,
            []);
        dbContext.ApprovalRequests.Add(approval);

        await dbContext.SaveChangesAsync();
        return new SeedContext(companyId, otherCompanyId, userId, agentId, taskId, otherTaskId, workflowInstanceId, approvalId, toolExecutionId, "corr-activity-correlation");
    }

    private static async Task<ActivityEventDto> PersistActivityAsync(
        HttpClient client,
        Guid companyId,
        string eventType,
        string correlationId,
        Guid taskId,
        Guid? workflowInstanceId = null,
        Guid? approvalRequestId = null,
        Guid? toolExecutionAttemptId = null,
        DateTime? occurredAt = null)
    {
        var source = new Dictionary<string, JsonNode?>
        {
            ["taskId"] = JsonValue.Create(taskId),
            ["linkedEntities"] = new JsonArray()
        };

        var linkedEntities = (JsonArray)source["linkedEntities"]!;
        linkedEntities.Add(new JsonObject
        {
            ["entityType"] = ActivityEntityTypes.Task,
            ["entityId"] = taskId
        });

        if (workflowInstanceId is Guid workflowId)
        {
            source["workflowInstanceId"] = JsonValue.Create(workflowId);
            linkedEntities.Add(new JsonObject
            {
                ["entityType"] = ActivityEntityTypes.WorkflowInstance,
                ["entityId"] = workflowId
            });
        }

        if (approvalRequestId is Guid approvalId)
        {
            source["approvalRequestId"] = JsonValue.Create(approvalId);
            linkedEntities.Add(new JsonObject
            {
                ["entityType"] = ActivityEntityTypes.Approval,
                ["entityId"] = approvalId
            });
        }

        if (toolExecutionAttemptId is Guid toolExecutionId)
        {
            source["toolExecutionAttemptId"] = JsonValue.Create(toolExecutionId);
            linkedEntities.Add(new JsonObject
            {
                ["entityType"] = ActivityEntityTypes.ToolExecution,
                ["entityId"] = toolExecutionId
            });
        }

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{companyId}/activity-events",
            new PersistActivityEventCommand(
                companyId,
                null,
                eventType,
                occurredAt ?? DateTime.UtcNow,
                "running",
                eventType,
                correlationId,
                source));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ActivityEventDto>())!;
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "activity-correlation");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "activity-correlation@example.com");
        return client;
    }

    private sealed record SeedContext(
        Guid CompanyId,
        Guid OtherCompanyId,
        Guid UserId,
        Guid AgentId,
        Guid TaskId,
        Guid OtherTaskId,
        Guid WorkflowInstanceId,
        Guid ApprovalId,
        Guid ToolExecutionId,
        string CorrelationId);
}
