using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BriefingAggregationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BriefingAggregationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Aggregate_returns_empty_sections_for_empty_company_data()
    {
        var seed = await SeedEmptyCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aggregate = await response.Content.ReadFromJsonAsync<BriefingAggregateResponse>();

        Assert.NotNull(aggregate);
        Assert.Equal(seed.CompanyId, aggregate!.CompanyId);
        Assert.Empty(aggregate.Alerts);
        Assert.Empty(aggregate.PendingApprovals);
        Assert.Empty(aggregate.KpiHighlights);
        Assert.Empty(aggregate.Anomalies);
        Assert.Empty(aggregate.NotableAgentUpdates);
    }

    [Fact]
    public async Task Aggregate_includes_alerts_approvals_kpis_anomalies_and_agent_updates()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aggregate = await response.Content.ReadFromJsonAsync<BriefingAggregateResponse>();

        Assert.NotNull(aggregate);
        Assert.Contains(aggregate!.PendingApprovals, x => x.SourceEntityId == seed.ApprovalId && x.SourceEntityType == "approval_request");
        Assert.Contains(aggregate.Alerts, x => x.SourceEntityId == seed.WorkflowExceptionId && x.SourceEntityType == "workflow_exception");
        Assert.Contains(aggregate.KpiHighlights, x => x.Status == "completed");
        Assert.Contains(aggregate.Anomalies, x => x.SourceEntityId == seed.BlockedTaskId && x.SourceEntityType == "task");
        Assert.Contains(aggregate.NotableAgentUpdates, x => x.SourceEntityId == seed.AgentId && x.SourceEntityType == "agent");
        Assert.DoesNotContain(aggregate.PendingApprovals, x => x.SourceEntityId == seed.OtherCompanyApprovalId);
        Assert.DoesNotContain(aggregate.NotableAgentUpdates, x => x.SourceEntityId == seed.OtherCompanyAgentId);
    }

    [Fact]
    public async Task Generate_persists_structured_briefing_payload_and_message_for_company()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.NotNull(result!.Briefing.MessageId);
        Assert.Contains("Alerts", result.Briefing.SummaryBody);
        Assert.Contains("Approvals", result.Briefing.SummaryBody);
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("alerts"));
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("pendingApprovals"));
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("kpiHighlights"));
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("anomalies"));
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("notableAgentUpdates"));
        Assert.True(result.Briefing.StructuredPayload.ContainsKey("summaryItems"));
        Assert.Contains(result.Briefing.SourceReferences, reference =>
            reference.EntityType == "approval" &&
            reference.EntityId == seed.ApprovalId &&
            reference.Status == "pending");
        Assert.Contains(result.Briefing.SourceReferences, reference =>
            reference.EntityType == "task" &&
            reference.EntityId == seed.CompletedTaskId &&
            reference.Status == "completed");

        var summaryItems = result.Briefing.StructuredPayload["summaryItems"].EnumerateArray().ToList();
        Assert.Contains(summaryItems, item =>
            item.GetProperty("references").EnumerateArray().Any(reference =>
                reference.GetProperty("entityType").GetString() == "task" &&
                reference.GetProperty("entityId").GetGuid() == seed.CompletedTaskId));
        Assert.DoesNotContain(result.Briefing.SourceReferences, reference => reference.EntityId == seed.OtherCompanyApprovalId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var message = await dbContext.Messages
            .AsNoTracking()
            .SingleAsync(x => x.Id == result.Briefing.MessageId);

        Assert.Equal(seed.CompanyId, message.CompanyId);
        Assert.Equal("daily_briefing", message.MessageType);
        Assert.True(message.StructuredPayload.ContainsKey("sourceReferences"));
        Assert.True(message.StructuredPayload.ContainsKey("summaryItems"));
        Assert.True(result.Briefing.StructuredPayload["alerts"].GetArrayLength() > 0);
        Assert.True(result.Briefing.StructuredPayload["kpiHighlights"].GetArrayLength() > 0);
        Assert.True(result.Briefing.StructuredPayload["anomalies"].GetArrayLength() > 0);
    }

    [Fact]
    public async Task Generate_uses_fresh_cached_dashboard_aggregate_when_available()
    {
        var seed = await SeedBriefingCompanyAsync();
        var nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12);

        using var client = CreateAuthenticatedClient();
        var aggregateResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc
        });
        Assert.Equal(HttpStatusCode.OK, aggregateResponse.StatusCode);

        var uncachedTaskId = await AddCompletedTaskAsync(seed, "Completed after dashboard cache");
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.Contains(result!.Briefing.SourceReferences, reference => reference.EntityId == seed.CompletedTaskId);
        Assert.DoesNotContain(result.Briefing.SourceReferences, reference => reference.EntityId == uncachedTaskId);
    }

    [Fact]
    public async Task Generate_falls_back_when_cached_dashboard_aggregate_is_stale()
    {
        var seed = await SeedBriefingCompanyAsync();
        var nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12);

        using var client = CreateAuthenticatedClient();
        var aggregateResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc
        });
        Assert.Equal(HttpStatusCode.OK, aggregateResponse.StatusCode);
        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<BriefingAggregateResultDto>();
        Assert.NotNull(aggregate);

        using (var scope = _factory.Services.CreateScope())
        {
            var aggregateCache = scope.ServiceProvider.GetRequiredService<IExecutiveDashboardAggregateCache>();
            await aggregateCache.SetAsync(
                new CachedExecutiveDashboardAggregateDto(
                    seed.CompanyId,
                    "daily_briefing",
                    aggregate!.PeriodStartUtc,
                    aggregate.PeriodEndUtc,
                    DateTime.UtcNow.AddHours(-3),
                    aggregate),
                CancellationToken.None);
        }

        var fallbackTaskId = await AddCompletedTaskAsync(seed, "Completed after stale dashboard cache");
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.Contains(result!.Briefing.SourceReferences, reference => reference.EntityId == fallbackTaskId);
    }

    [Fact]
    public async Task Cached_dashboard_aggregate_payloads_are_tenant_scoped()
    {
        var seed = await SeedBriefingCompanyAsync();
        var nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aggregate = await response.Content.ReadFromJsonAsync<BriefingAggregateResultDto>();
        Assert.NotNull(aggregate);

        using var scope = _factory.Services.CreateScope();
        var aggregateCache = scope.ServiceProvider.GetRequiredService<IExecutiveDashboardAggregateCache>();
        var otherCompanySnapshot = await aggregateCache.TryGetAsync(seed.OtherCompanyId, "daily_briefing", aggregate!.PeriodStartUtc, aggregate.PeriodEndUtc, CancellationToken.None);
        Assert.Null(otherCompanySnapshot);
    }

    [Fact]
    public async Task Generate_reuses_briefing_conversation_and_latest_endpoint_returns_dashboard_summary()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var dailyResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });
        var weeklyResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "weekly_summary",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, dailyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, weeklyResponse.StatusCode);

        var latestResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/briefings/latest");
        Assert.Equal(HttpStatusCode.OK, latestResponse.StatusCode);
        var latest = await latestResponse.Content.ReadFromJsonAsync<DashboardBriefingCardResponse>();

        Assert.NotNull(latest?.Daily);
        Assert.NotNull(latest?.Weekly);
        Assert.Equal(seed.CompanyId, latest!.Daily!.CompanyId);
        Assert.Equal(seed.CompanyId, latest.Weekly!.CompanyId);
        Assert.NotNull(latest.Daily.MessageId);
        Assert.NotNull(latest.Weekly.MessageId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var conversationCount = await dbContext.Conversations
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.ChannelType == "executive_briefing");
        var messageCount = await dbContext.Messages
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.Conversation.ChannelType == "executive_briefing");

        Assert.Equal(1, conversationCount);
        Assert.Equal(2, messageCount);
    }

    [Fact]
    public async Task Latest_dashboard_briefing_is_tenant_scoped()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var otherCompanyResponse = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/briefings/latest");
        Assert.Equal(HttpStatusCode.Forbidden, otherCompanyResponse.StatusCode);
    }

    [Fact]
    public async Task Aggregate_is_tenant_scoped()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/aggregate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aggregate = await response.Content.ReadFromJsonAsync<BriefingAggregateResponse>();

        Assert.NotNull(aggregate);
        Assert.DoesNotContain(aggregate!.PendingApprovals, x => x.SourceEntityId == seed.OtherCompanyApprovalId);
        Assert.DoesNotContain(aggregate.Alerts, x => x.SourceEntityId == seed.OtherCompanyWorkflowExceptionId);
        Assert.DoesNotContain(aggregate.NotableAgentUpdates, x => x.SourceEntityId == seed.OtherCompanyAgentId);
    }

    [Fact]
    public async Task Get_preferences_returns_deterministic_defaults_when_no_record_exists()
    {
        var seed = await SeedEmptyCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/briefings/preferences");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preferences = await response.Content.ReadFromJsonAsync<BriefingPreferenceResponse>();

        Assert.NotNull(preferences);
        Assert.Equal(seed.CompanyId, preferences!.CompanyId);
        Assert.True(preferences.InAppEnabled);
        Assert.False(preferences.MobileEnabled);
        Assert.True(preferences.DailyEnabled);
        Assert.True(preferences.WeeklyEnabled);
        Assert.Null(preferences.UpdatedUtc);
    }

    [Fact]
    public async Task Update_preferences_persists_for_current_user_only()
    {
        var seed = await SeedEmptyCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences", new
        {
            inAppEnabled = false,
            mobileEnabled = true,
            dailyEnabled = true,
            weeklyEnabled = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preferences = await response.Content.ReadFromJsonAsync<BriefingPreferenceResponse>();

        Assert.NotNull(preferences);
        Assert.False(preferences!.InAppEnabled);
        Assert.True(preferences.MobileEnabled);
        Assert.True(preferences.DailyEnabled);
        Assert.False(preferences.WeeklyEnabled);
        Assert.NotNull(preferences.UpdatedUtc);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persisted = await dbContext.CompanyBriefingDeliveryPreferences
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == seed.CompanyId && x.UserId == preferences.UserId);

        Assert.False(persisted.InAppEnabled);
        Assert.True(persisted.MobileEnabled);
        Assert.True(persisted.DailyEnabled);
        Assert.False(persisted.WeeklyEnabled);
    }

    [Fact]
    public async Task Preferences_are_tenant_scoped_for_same_user_memberships()
    {
        var seed = await SeedUserWithTwoCompanyMembershipsAsync();

        using var client = CreateAuthenticatedClient();
        var firstUpdate = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyAId}/briefings/preferences", new
        {
            inAppEnabled = false,
            mobileEnabled = true,
            dailyEnabled = true,
            weeklyEnabled = true
        });
        var secondUpdate = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyBId}/briefings/preferences", new
        {
            inAppEnabled = true,
            mobileEnabled = false,
            dailyEnabled = false,
            weeklyEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);

        var firstRead = await client.GetFromJsonAsync<BriefingPreferenceResponse>($"/api/companies/{seed.CompanyAId}/briefings/preferences");
        var secondRead = await client.GetFromJsonAsync<BriefingPreferenceResponse>($"/api/companies/{seed.CompanyBId}/briefings/preferences");

        Assert.NotNull(firstRead);
        Assert.NotNull(secondRead);
        Assert.Equal(seed.CompanyAId, firstRead!.CompanyId);
        Assert.False(firstRead.InAppEnabled);
        Assert.True(firstRead.MobileEnabled);
        Assert.True(firstRead.DailyEnabled);
        Assert.Equal(seed.CompanyBId, secondRead!.CompanyId);
        Assert.True(secondRead.InAppEnabled);
        Assert.False(secondRead.MobileEnabled);
        Assert.False(secondRead.DailyEnabled);
    }

    [Fact]
    public async Task Generate_creates_in_app_notification_when_in_app_delivery_is_enabled()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var preferenceResponse = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences", new
        {
            inAppEnabled = true,
            mobileEnabled = false,
            dailyEnabled = true,
            weeklyEnabled = true
        });
        Assert.Equal(HttpStatusCode.OK, preferenceResponse.StatusCode);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.Equal(1, result!.NotificationsCreated);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var notifications = await dbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.BriefingId == result.Briefing.Id)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Equal(CompanyNotificationChannel.InApp, notifications[0].Channel);
    }

    [Fact]
    public async Task Generate_does_not_create_mobile_notification_when_mobile_preference_is_set()
    {
        var seed = await SeedBriefingCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var preferenceResponse = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences", new
        {
            inAppEnabled = true,
            mobileEnabled = true,
            dailyEnabled = true,
            weeklyEnabled = true
        });
        Assert.Equal(HttpStatusCode.OK, preferenceResponse.StatusCode);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.Equal(1, result!.NotificationsCreated);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var channels = await dbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.BriefingId == result.Briefing.Id)
            .Select(x => x.Channel)
            .ToListAsync();

        Assert.Single(channels);
        Assert.Contains(CompanyNotificationChannel.InApp, channels);
        Assert.DoesNotContain(CompanyNotificationChannel.Mobile, channels);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private async Task<EmptyBriefingSeed> SeedEmptyCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Briefing Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new EmptyBriefingSeed(companyId);
    }

    private async Task<Guid> AddCompletedTaskAsync(BriefingSeed seed, string title)
    {
        var taskId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            var task = new WorkTask(taskId, seed.CompanyId, "briefing", title, null, WorkTaskPriority.Normal, seed.AgentId, null, "user", seed.UserId);
            task.UpdateStatus(WorkTaskStatus.Completed);
            dbContext.WorkTasks.Add(task);
            return Task.CompletedTask;
        });

        return taskId;
    }

    private async Task<BriefingSeed> SeedBriefingCompanyAsync()
    {
        var now = DateTime.UtcNow;
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var completedTaskId = Guid.NewGuid();
        var blockedTaskId = Guid.NewGuid();
        var otherCompanyTaskId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var workflowExceptionId = Guid.NewGuid();
        var otherCompanyWorkflowDefinitionId = Guid.NewGuid();
        var otherCompanyWorkflowInstanceId = Guid.NewGuid();
        var otherCompanyWorkflowExceptionId = Guid.NewGuid();
        var toolExecutionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var otherCompanyToolExecutionId = Guid.NewGuid();
        var otherCompanyApprovalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Briefing Company"), new Company(otherCompanyId, "Other Briefing Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active),
                new Agent(otherCompanyAgentId, otherCompanyId, "ops", "Other Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            dbContext.WorkflowDefinitions.AddRange(
                new WorkflowDefinition(workflowDefinitionId, companyId, "daily-briefing-test", "Daily briefing test", "Operations", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))),
                new WorkflowDefinition(otherCompanyWorkflowDefinitionId, otherCompanyId, "other-daily-briefing-test", "Other daily briefing test", "Operations", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))));
            dbContext.WorkflowInstances.AddRange(
                new WorkflowInstance(workflowInstanceId, companyId, workflowDefinitionId, null),
                new WorkflowInstance(otherCompanyWorkflowInstanceId, otherCompanyId, otherCompanyWorkflowDefinitionId, null));

            var completedTask = new WorkTask(completedTaskId, companyId, "briefing", "Completed customer follow-up", null, WorkTaskPriority.Normal, agentId, null, "user", userId, workflowInstanceId: workflowInstanceId);
            completedTask.UpdateStatus(WorkTaskStatus.Completed);
            var blockedTask = new WorkTask(blockedTaskId, companyId, "briefing", "Blocked vendor renewal", null, WorkTaskPriority.High, agentId, null, "user", userId, workflowInstanceId: workflowInstanceId);
            blockedTask.UpdateStatus(WorkTaskStatus.Blocked);
            var otherCompanyTask = new WorkTask(otherCompanyTaskId, otherCompanyId, "briefing", "Other company task", null, WorkTaskPriority.High, otherCompanyAgentId, null, "user", userId, workflowInstanceId: otherCompanyWorkflowInstanceId);
            otherCompanyTask.UpdateStatus(WorkTaskStatus.Blocked);
            dbContext.WorkTasks.AddRange(completedTask, blockedTask, otherCompanyTask);

            dbContext.WorkflowExceptions.AddRange(
                new WorkflowException(workflowExceptionId, companyId, workflowInstanceId, workflowDefinitionId, "collect", WorkflowExceptionType.Blocked, "Workflow needs review", "A workflow exception needs attention."),
                new WorkflowException(otherCompanyWorkflowExceptionId, otherCompanyId, otherCompanyWorkflowInstanceId, otherCompanyWorkflowDefinitionId, "collect", WorkflowExceptionType.Blocked, "Other workflow needs review", "Other workflow exception."));

            var toolExecution = new ToolExecutionAttempt(toolExecutionId, companyId, agentId, "erp", ToolActionType.Execute, "finance.approval", taskId: blockedTaskId, workflowInstanceId: workflowInstanceId, startedAtUtc: now);
            var approval = new ApprovalRequest(approvalId, companyId, agentId, toolExecutionId, userId, "erp", ToolActionType.Execute, "expense", Payload(("expenseUsd", JsonValue.Create(2000))));
            toolExecution.MarkAwaitingApproval(approvalId, Payload(("outcome", JsonValue.Create("require_approval"))), completedAtUtc: now);

            var otherCompanyToolExecution = new ToolExecutionAttempt(otherCompanyToolExecutionId, otherCompanyId, otherCompanyAgentId, "erp", ToolActionType.Execute, "finance.approval", taskId: otherCompanyTaskId, workflowInstanceId: otherCompanyWorkflowInstanceId, startedAtUtc: now);
            var otherCompanyApproval = new ApprovalRequest(otherCompanyApprovalId, otherCompanyId, otherCompanyAgentId, otherCompanyToolExecutionId, userId, "erp", ToolActionType.Execute, "expense", Payload(("expenseUsd", JsonValue.Create(9000))));
            otherCompanyToolExecution.MarkAwaitingApproval(otherCompanyApprovalId, Payload(("outcome", JsonValue.Create("require_approval"))), completedAtUtc: now);

            dbContext.ToolExecutionAttempts.AddRange(toolExecution, otherCompanyToolExecution);
            dbContext.ApprovalRequests.AddRange(approval, otherCompanyApproval);
            return Task.CompletedTask;
        });

        return new BriefingSeed(
            companyId,
            userId,
            otherCompanyId,
            agentId,
            completedTaskId,
            blockedTaskId,
            workflowExceptionId,
            approvalId,
            otherCompanyAgentId,
            otherCompanyWorkflowExceptionId,
            otherCompanyApprovalId);
    }

    private async Task<TwoCompanyPreferenceSeed> SeedUserWithTwoCompanyMembershipsAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyAId, "Preference Company A"), new Company(companyBId, "Preference Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new TwoCompanyPreferenceSeed(companyAId, companyBId);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record EmptyBriefingSeed(Guid CompanyId);

    private sealed record TwoCompanyPreferenceSeed(Guid CompanyAId, Guid CompanyBId);

    private sealed record BriefingSeed(
        Guid CompanyId,
        Guid UserId,
        Guid OtherCompanyId,
        Guid AgentId,
        Guid CompletedTaskId,
        Guid BlockedTaskId,
        Guid WorkflowExceptionId,
        Guid ApprovalId,
        Guid OtherCompanyAgentId,
        Guid OtherCompanyWorkflowExceptionId,
        Guid OtherCompanyApprovalId);

    private sealed class BriefingAggregateResponse
    {
        public Guid CompanyId { get; set; }
        public List<BriefingAggregateItemResponse> Alerts { get; set; } = [];
        public List<BriefingAggregateItemResponse> PendingApprovals { get; set; } = [];
        public List<BriefingAggregateItemResponse> KpiHighlights { get; set; } = [];
        public List<BriefingAggregateItemResponse> Anomalies { get; set; } = [];
        public List<BriefingAggregateItemResponse> NotableAgentUpdates { get; set; } = [];
    }

    private sealed class BriefingAggregateItemResponse
    {
        public string Title { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? SourceEntityType { get; set; }
        public Guid? SourceEntityId { get; set; }
    }

    private sealed class BriefingGenerationResponse
    {
        public CompanyBriefingResponse Briefing { get; set; } = new();
        public int NotificationsCreated { get; set; }
    }

    private sealed class DashboardBriefingCardResponse
    {
        public CompanyBriefingResponse? Daily { get; set; }
        public CompanyBriefingResponse? Weekly { get; set; }
    }

    private sealed class BriefingPreferenceResponse
    {
        public Guid CompanyId { get; set; }
        public Guid UserId { get; set; }
        public bool InAppEnabled { get; set; }
        public bool MobileEnabled { get; set; }
        public bool DailyEnabled { get; set; }
        public bool WeeklyEnabled { get; set; }
        public TimeOnly PreferredDeliveryTime { get; set; }
        public string? PreferredTimezone { get; set; }
        public DateTime? UpdatedUtc { get; set; }
    }

    private sealed class CompanyBriefingResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid? MessageId { get; set; }
        public string SummaryBody { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> StructuredPayload { get; set; } = [];
        public List<BriefingSourceReferenceResponse> SourceReferences { get; set; } = [];
    }

    private sealed class BriefingSourceReferenceResponse
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Route { get; set; }
    }
}