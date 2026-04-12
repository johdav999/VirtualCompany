using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class WorkflowDefinitionIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WorkflowDefinitionIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Creating_initial_definition_stores_version_one()
    {
        var seed = await SeedMembershipAsync("workflow-owner", "workflow-owner@example.com", "Workflow Owner");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "invoice-approval",
            name = "Invoice approval",
            department = "Finance",
            triggerType = "event",
            active = true,
            definitionJson = ValidDefinition("capture")
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal("INVOICE-APPROVAL", payload.Code);
        Assert.Equal(1, payload.Version);
        Assert.Equal("event", payload.TriggerType);
        Assert.True(payload.Active);
        Assert.True(payload.DefinitionJson.ContainsKey("steps"));
    }

    [Fact]
    public async Task Creating_new_version_increments_version_and_preserves_prior_definition()
    {
        var seed = await SeedMembershipAsync("workflow-versioner", "workflow-versioner@example.com", "Workflow Versioner");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var initialResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "employee-onboarding",
            name = "Employee onboarding",
            department = "People",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("collect-documents")
        });

        var initial = await initialResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(initial);

        var versionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial!.Id}/versions", new
        {
            name = "Employee onboarding",
            department = "People",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("create-accounts")
        });

        Assert.Equal(HttpStatusCode.Created, versionResponse.StatusCode);
        var version = await versionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(version);
        Assert.Equal(2, version!.Version);
        Assert.Equal(initial.Code, version.Code);

        var priorResponse = await client.GetFromJsonAsync<WorkflowDefinitionDto>($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial.Id}");
        Assert.NotNull(priorResponse);
        Assert.Equal(1, priorResponse!.Version);
        Assert.False(priorResponse.Active);
        Assert.Contains("collect-documents", priorResponse.DefinitionJson["steps"]!.ToJsonString());
        Assert.Contains("create-accounts", version.DefinitionJson["steps"]!.ToJsonString());
    }

    [Fact]
    public async Task Manual_start_by_code_uses_latest_active_version_without_mutating_in_flight_instance()
    {
        var seed = await SeedMembershipAsync("workflow-code-start", "workflow-code-start@example.com", "Workflow Code Start");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var initialResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "versioned-manual",
            name = "Versioned manual",
            department = "Operations",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("v1-step")
        });
        var initial = await initialResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(initial);

        var firstStart = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/by-code/versioned-manual/start", new
        {
            code = "",
            triggerRef = "manual-version-1",
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        Assert.Equal(HttpStatusCode.Created, firstStart.StatusCode);
        var firstInstance = await firstStart.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(firstInstance);
        Assert.Equal(initial!.Id, firstInstance!.DefinitionId);
        Assert.Equal(1, firstInstance.DefinitionVersion);
        Assert.Equal("v1-step", firstInstance.CurrentStep);

        var versionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial.Id}/versions", new
        {
            name = "Versioned manual",
            department = "Operations",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("v2-step")
        });
        Assert.Equal(HttpStatusCode.Created, versionResponse.StatusCode);
        var version = await versionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(version);

        var persistedFirst = await client.GetFromJsonAsync<WorkflowInstanceDto>($"/api/companies/{seed.CompanyId}/workflows/instances/{firstInstance.Id}");
        Assert.NotNull(persistedFirst);
        Assert.Equal(initial.Id, persistedFirst!.DefinitionId);
        Assert.Equal(1, persistedFirst.DefinitionVersion);
        Assert.Equal("v1-step", persistedFirst.CurrentStep);

        var secondStart = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/by-code/versioned-manual/start", new
        {
            code = "",
            triggerRef = "manual-version-2",
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        Assert.Equal(HttpStatusCode.Created, secondStart.StatusCode);
        var secondInstance = await secondStart.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(secondInstance);
        Assert.Equal(version!.Id, secondInstance!.DefinitionId);
        Assert.Equal(2, secondInstance.DefinitionVersion);
        Assert.Equal("v2-step", secondInstance.CurrentStep);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var definitionIds = await dbContext.WorkflowInstances
            .Where(x => x.Id == firstInstance.Id || x.Id == secondInstance.Id)
            .OrderBy(x => x.TriggerRef)
            .Select(x => x.DefinitionId)
            .ToListAsync();
        Assert.Equal([initial.Id, version.Id], definitionIds);
    }

    [Fact]
    public async Task Latest_only_returns_newest_definition_per_code()
    {
        var seed = await SeedMembershipAsync("workflow-lister", "workflow-lister@example.com", "Workflow Lister");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var initialResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "contract-review",
            name = "Contract review",
            department = "Legal",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("first-pass")
        });
        var initial = await initialResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(initial);

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial!.Id}/versions", new
        {
            name = "Contract review",
            department = "Legal",
            triggerType = "manual",
            active = true,
            definitionJson = ValidDefinition("counsel-review")
        });

        var latest = await client.GetFromJsonAsync<List<WorkflowDefinitionDto>>($"/api/companies/{seed.CompanyId}/workflows/definitions?latestOnly=true");

        Assert.NotNull(latest);
        var contractReview = Assert.Single(latest!, x => x.Code == "CONTRACT-REVIEW");
        Assert.Equal(2, contractReview.Version);
    }

    [Fact]
    public async Task Invalid_definition_json_is_rejected()
    {
        var seed = await SeedMembershipAsync("workflow-validator", "workflow-validator@example.com", "Workflow Validator");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "invalid-workflow",
            name = "Invalid workflow",
            department = "Operations",
            triggerType = "manual",
            definitionJson = new Dictionary<string, JsonNode?>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var missingStepsResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "missing-steps",
            name = "Missing steps",
            department = "Operations",
            triggerType = "manual",
            definitionJson = new Dictionary<string, JsonNode?>
            {
                ["name"] = JsonValue.Create("Missing steps")
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, missingStepsResponse.StatusCode);
    }

    [Fact]
    public async Task Tenant_cannot_read_or_version_another_tenant_definition()
    {
        var seed = await SeedTwoMembershipsAsync();

        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await ownerClient.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "tenant-private",
            name = "Tenant private",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("private-step")
        });
        var definition = await response.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        using var otherClient = CreateAuthenticatedClient(seed.OtherSubject, seed.OtherEmail, seed.OtherDisplayName);
        var readResponse = await otherClient.GetAsync($"/api/companies/{seed.OtherCompanyId}/workflows/definitions/{definition!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);

        var versionResponse = await otherClient.PostAsJsonAsync($"/api/companies/{seed.OtherCompanyId}/workflows/definitions/{definition.Id}/versions", new
        {
            name = "Tenant private",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("cross-tenant-step")
        });
        Assert.Equal(HttpStatusCode.NotFound, versionResponse.StatusCode);
    }

    [Fact]
    public async Task Manual_start_creates_tenant_scoped_instance_with_trigger_metadata()
    {
        var seed = await SeedMembershipAsync("workflow-manual", "workflow-manual@example.com", "Workflow Manual");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definitionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "manual-start",
            name = "Manual start",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("first-manual-step")
        });

        var definition = await definitionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        var startResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition!.Id}/start", new
        {
            definitionId = Guid.Empty,
            triggerRef = "manual-request-1",
            inputPayload = new Dictionary<string, JsonNode?>
            {
                ["requestedBy"] = JsonValue.Create("founder")
            }
        });

        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var instance = await startResponse.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(instance);
        Assert.Equal(seed.CompanyId, instance!.CompanyId);
        Assert.Equal(definition.Id, instance.DefinitionId);
        Assert.Equal("manual", instance.TriggerSource);
        Assert.Equal("manual-request-1", instance.TriggerRef);
        Assert.Equal("started", instance.State);
        Assert.Equal(instance.State, instance.Status);
        Assert.Equal("first-manual-step", instance.CurrentStep);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var persisted = await dbContext.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
        Assert.Equal(seed.CompanyId, persisted.CompanyId);
        Assert.Equal(WorkflowTriggerType.Manual, persisted.TriggerSource);
        Assert.Equal("manual-request-1", persisted.TriggerRef);
        Assert.Equal(WorkflowInstanceStatus.Started, persisted.State);
        Assert.Equal("first-manual-step", persisted.CurrentStep);
    }

    [Fact]
    public async Task Instance_state_update_is_persisted_queryable_and_tenant_scoped()
    {
        var seed = await SeedTwoMembershipsAsync();

        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definitionResponse = await ownerClient.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "state-update",
            name = "State update",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("intake")
        });

        var definition = await definitionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        var startResponse = await ownerClient.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition!.Id}/start", new
        {
            definitionId = Guid.Empty,
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        var instance = await startResponse.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(instance);

        var updateResponse = await ownerClient.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/instances/{instance!.Id}/state", new
        {
            state = "blocked",
            currentStep = "manager-review",
            outputPayload = new Dictionary<string, JsonNode?>
            {
                ["reason"] = JsonValue.Create("awaiting approval")
            }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(updated);
        Assert.Equal("blocked", updated!.State);
        Assert.Equal("manager-review", updated.CurrentStep);

        var queried = await ownerClient.GetFromJsonAsync<WorkflowInstanceDto>($"/api/companies/{seed.CompanyId}/workflows/instances/{instance.Id}");
        Assert.Equal("blocked", queried!.State);
        Assert.Equal("manager-review", queried.CurrentStep);

        using var otherClient = CreateAuthenticatedClient(seed.OtherSubject, seed.OtherEmail, seed.OtherDisplayName);
        var crossTenantRead = await otherClient.GetAsync($"/api/companies/{seed.OtherCompanyId}/workflows/instances/{instance.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);
    }

    [Fact]
    public async Task Failed_or_blocked_instance_state_creates_open_review_exception_once()
    {
        var seed = await SeedMembershipAsync("workflow-exception-owner", "workflow-exception-owner@example.com", "Workflow Exception Owner");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definitionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "exception-review",
            name = "Exception review",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("triage")
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        var startResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition!.Id}/start", new
        {
            definitionId = Guid.Empty,
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        var instance = await startResponse.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(instance);

        var failedUpdate = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/instances/{instance!.Id}/state", new
        {
            state = "failed",
            currentStep = "triage",
            outputPayload = new Dictionary<string, JsonNode?>
            {
                ["reason"] = JsonValue.Create("Vendor API rejected the request"),
                ["errorCode"] = JsonValue.Create("VENDOR_REJECTED")
            }
        });
        Assert.Equal(HttpStatusCode.OK, failedUpdate.StatusCode);

        var duplicateFailedUpdate = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/instances/{instance.Id}/state", new
        {
            state = "failed",
            currentStep = "triage",
            outputPayload = new Dictionary<string, JsonNode?>
            {
                ["reason"] = JsonValue.Create("Vendor API rejected the request"),
                ["errorCode"] = JsonValue.Create("VENDOR_REJECTED")
            }
        });
        Assert.Equal(HttpStatusCode.OK, duplicateFailedUpdate.StatusCode);

        var exceptions = await client.GetFromJsonAsync<List<WorkflowExceptionDto>>($"/api/companies/{seed.CompanyId}/workflows/exceptions?workflowInstanceId={instance.Id}");
        var workflowException = Assert.Single(exceptions!);
        Assert.Equal(seed.CompanyId, workflowException.CompanyId);
        Assert.Equal(instance.Id, workflowException.WorkflowInstanceId);
        Assert.Equal(definition.Id, workflowException.WorkflowDefinitionId);
        Assert.Equal("triage", workflowException.StepKey);
        Assert.Equal("failed", workflowException.ExceptionType);
        Assert.Equal("open", workflowException.Status);
        Assert.Equal("Vendor API rejected the request", workflowException.Details);
        Assert.Equal("VENDOR_REJECTED", workflowException.ErrorCode);

        var detail = await client.GetFromJsonAsync<WorkflowExceptionDto>($"/api/companies/{seed.CompanyId}/workflows/exceptions/{workflowException.Id}");
        Assert.NotNull(detail);
        Assert.Equal(workflowException.Id, detail!.Id);

        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/exceptions/{workflowException.Id}/review", new
        {
            resolutionNotes = "Reviewed with operations."
        });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<WorkflowExceptionDto>();
        Assert.NotNull(reviewed);
        Assert.Equal("reviewed", reviewed!.Status);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.NotNull(reviewed.ReviewedByUserId);
        Assert.Equal("Reviewed with operations.", reviewed.ResolutionNotes);
    }

    [Fact]
    public async Task Workflow_exception_queries_are_tenant_scoped()
    {
        var seed = await SeedTwoMembershipsAsync();

        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definitionResponse = await ownerClient.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "tenant-exception",
            name = "Tenant exception",
            department = "Operations",
            triggerType = "manual",
            definitionJson = ValidDefinition("approval")
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        var startResponse = await ownerClient.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition!.Id}/start", new
        {
            definitionId = Guid.Empty,
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        var instance = await startResponse.Content.ReadFromJsonAsync<WorkflowInstanceDto>();
        Assert.NotNull(instance);

        await ownerClient.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/instances/{instance!.Id}/state", new
        {
            state = "blocked",
            currentStep = "approval",
            outputPayload = new Dictionary<string, JsonNode?>
            {
                ["reason"] = JsonValue.Create("Waiting for approval threshold owner")
            }
        });

        var ownerExceptions = await ownerClient.GetFromJsonAsync<List<WorkflowExceptionDto>>($"/api/companies/{seed.CompanyId}/workflows/exceptions");
        var workflowException = Assert.Single(ownerExceptions!);
        Assert.Equal("blocked", workflowException.ExceptionType);

        using var otherClient = CreateAuthenticatedClient(seed.OtherSubject, seed.OtherEmail, seed.OtherDisplayName);
        var crossTenantList = await otherClient.GetFromJsonAsync<List<WorkflowExceptionDto>>($"/api/companies/{seed.OtherCompanyId}/workflows/exceptions");
        Assert.Empty(crossTenantList!);
        var crossTenantRead = await otherClient.GetAsync($"/api/companies/{seed.OtherCompanyId}/workflows/exceptions/{workflowException.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);
    }

    [Fact]
    public async Task Scheduled_start_service_creates_instance_and_skips_duplicate_tick()
    {
        var seed = await SeedMembershipAsync("workflow-schedule", "workflow-schedule@example.com", "Workflow Schedule");
        var definitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "daily-close",
                "Daily close",
                "Finance",
                WorkflowTriggerType.Schedule,
                1,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["schedule"] = new JsonObject { ["key"] = "daily-close" },
                    ["steps"] = new JsonArray(new JsonObject { ["id"] = "close-books" })
                }));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IWorkflowScheduleTriggerService>();
        var first = await scheduler.StartDueScheduledWorkflowsAsync(
            seed.CompanyId,
            new TriggerScheduledWorkflowsCommand(DateTime.Parse("2026-04-12T08:00:00Z").ToUniversalTime(), "daily-close"),
            CancellationToken.None);

        var second = await scheduler.StartDueScheduledWorkflowsAsync(
            seed.CompanyId,
            new TriggerScheduledWorkflowsCommand(DateTime.Parse("2026-04-12T08:00:00Z").ToUniversalTime(), "daily-close"),
            CancellationToken.None);

        var instance = Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal(definitionId, instance.DefinitionId);
        Assert.Equal("schedule", instance.TriggerSource);
        Assert.Equal("daily-close:202604120800", instance.TriggerRef);
        Assert.Equal("started", instance.State);
        Assert.Equal("close-books", instance.CurrentStep);
    }

    [Fact]
    public async Task Scheduled_polling_service_starts_due_workflows_once_per_occurrence()
    {
        var seed = await SeedMembershipAsync("workflow-scheduler-poll", "workflow-scheduler-poll@example.com", "Workflow Scheduler Poll");
        var definitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "daily-briefing",
                "Daily briefing",
                "Executive",
                WorkflowTriggerType.Schedule,
                1,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["schedule"] = new JsonObject { ["scheduleKey"] = "daily-briefing" },
                    ["steps"] = new JsonArray(new JsonObject { ["id"] = "collect-signals" })
                }));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulePollingService>();
        var first = await scheduler.RunDueSchedulesAsync(
            DateTime.Parse("2026-04-12T08:00:00Z").ToUniversalTime(),
            10,
            CancellationToken.None);
        var second = await scheduler.RunDueSchedulesAsync(
            DateTime.Parse("2026-04-12T08:00:00Z").ToUniversalTime(),
            10,
            CancellationToken.None);

        Assert.True(first.LockAcquired);
        Assert.Equal(1, first.WorkflowsStarted);
        Assert.Equal(0, first.Failures);
        Assert.True(second.LockAcquired);
        Assert.Equal(0, second.WorkflowsStarted);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persistedCount = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.DefinitionId == definitionId && x.TriggerRef == "daily-briefing:202604120800");
        Assert.Equal(1, persistedCount);
    }

    [Fact]
    public async Task Scheduled_trigger_uses_latest_active_definition_version()
    {
        var seed = await SeedMembershipAsync("workflow-schedule-version", "workflow-schedule-version@example.com", "Workflow Schedule Version");
        var oldDefinitionId = Guid.NewGuid();
        var newDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                oldDefinitionId,
                seed.CompanyId,
                "scheduled-versioned",
                "Scheduled versioned",
                "Operations",
                WorkflowTriggerType.Schedule,
                1,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["schedule"] = new JsonObject { ["scheduleKey"] = "scheduled-versioned" },
                    ["steps"] = new JsonArray(new JsonObject { ["id"] = "old-scheduled-step" })
                },
                active: false));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                newDefinitionId,
                seed.CompanyId,
                "scheduled-versioned",
                "Scheduled versioned",
                "Operations",
                WorkflowTriggerType.Schedule,
                2,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["schedule"] = new JsonObject { ["scheduleKey"] = "scheduled-versioned" },
                    ["steps"] = new JsonArray(new JsonObject { ["id"] = "new-scheduled-step" })
                },
                active: true));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IWorkflowScheduleTriggerService>();
        var started = await scheduler.StartDueScheduledWorkflowsAsync(
            seed.CompanyId,
            new TriggerScheduledWorkflowsCommand(DateTime.Parse("2026-04-12T08:00:00Z").ToUniversalTime(), "scheduled-versioned"),
            CancellationToken.None);

        var instance = Assert.Single(started);
        Assert.Equal(newDefinitionId, instance.DefinitionId);
        Assert.Equal(2, instance.DefinitionVersion);
        Assert.Equal("new-scheduled-step", instance.CurrentStep);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var oldStarted = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.DefinitionId == oldDefinitionId);
        Assert.False(oldStarted);
    }

    [Fact]
    public async Task Internal_event_trigger_starts_matching_workflow_only_for_event_company()
    {
        var seed = await SeedTwoMembershipsAsync();
        var definitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "document-review",
                "Document review",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("review-document")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                seed.CompanyId,
                definitionId,
                "company.document.processed"));

            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                Guid.NewGuid(),
                seed.OtherCompanyId,
                "document-review",
                "Document review other tenant",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("other-review-document")));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
        var result = await eventTriggers.HandleAsync(
            new InternalWorkflowEvent(
                seed.CompanyId,
                "company.document.processed",
                "doc-1",
                new Dictionary<string, JsonNode?>
                {
                    ["documentId"] = JsonValue.Create("doc-1")
                }),
            CancellationToken.None);

        var instance = Assert.Single(result.StartedInstances);
        Assert.Equal("company.document.processed", result.EventName);
        Assert.Equal(seed.CompanyId, instance.CompanyId);
        Assert.Equal(definitionId, instance.DefinitionId);
        Assert.Equal("event", instance.TriggerSource);
        Assert.Equal("doc-1", instance.TriggerRef);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var persistedCount = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .CountAsync(x => x.TriggerRef == "doc-1");
        Assert.Equal(1, persistedCount);
    }

    [Fact]
    public async Task Creating_active_event_version_copies_trigger_and_event_start_uses_new_version()
    {
        var seed = await SeedMembershipAsync("workflow-event-version", "workflow-event-version@example.com", "Workflow Event Version");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var initialResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "event-versioned",
            name = "Event versioned",
            department = "Operations",
            triggerType = "event",
            active = true,
            definitionJson = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["event"] = new JsonObject { ["eventName"] = "company.event.versioned" },
                ["steps"] = new JsonArray(new JsonObject { ["id"] = "old-event-step" })
            }
        });
        var initial = await initialResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(initial);

        var triggerResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial!.Id}/triggers", new
        {
            eventName = "company.event.versioned",
            criteriaJson = new Dictionary<string, JsonNode?>(),
            isEnabled = true
        });
        Assert.Equal(HttpStatusCode.Created, triggerResponse.StatusCode);

        var versionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{initial.Id}/versions", new
        {
            name = "Event versioned",
            department = "Operations",
            triggerType = "event",
            active = true,
            definitionJson = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["event"] = new JsonObject { ["eventName"] = "company.event.versioned" },
                ["steps"] = new JsonArray(new JsonObject { ["id"] = "new-event-step" })
            }
        });
        Assert.Equal(HttpStatusCode.Created, versionResponse.StatusCode);
        var version = await versionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(version);

        await using var scope = _factory.Services.CreateAsyncScope();
        var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
        var result = await eventTriggers.HandleAsync(
            new InternalWorkflowEvent(
                seed.CompanyId,
                "company.event.versioned",
                "event-version-1",
                new Dictionary<string, JsonNode?>()),
            CancellationToken.None);

        var instance = Assert.Single(result.StartedInstances);
        Assert.Equal(version!.Id, instance.DefinitionId);
        Assert.Equal(2, instance.DefinitionVersion);
        Assert.Equal("new-event-step", instance.CurrentStep);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var oldStarted = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.DefinitionId == initial.Id);
        Assert.False(oldStarted);
        var copiedTriggerExists = await dbContext.WorkflowTriggers
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.DefinitionId == version.Id &&
                x.EventName == "company.event.versioned" &&
                x.IsEnabled);
        Assert.True(copiedTriggerExists);
    }

    [Fact]
    public async Task Disallowed_trigger_source_and_inactive_definition_are_rejected()
    {
        var seed = await SeedMembershipAsync("workflow-negative", "workflow-negative@example.com", "Workflow Negative");

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definitionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions", new
        {
            code = "event-only",
            name = "Event only",
            department = "Operations",
            triggerType = "event",
            active = true,
            definitionJson = ValidDefinition("event-step")
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        Assert.NotNull(definition);

        var manualStart = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition!.Id}/start", new
        {
            definitionId = Guid.Empty,
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, manualStart.StatusCode);

        var inactiveId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                inactiveId,
                seed.CompanyId,
                "inactive-manual",
                "Inactive manual",
                "Operations",
                WorkflowTriggerType.Manual,
                1,
                ValidDefinition("inactive-step"),
                active: false));
            return Task.CompletedTask;
        });

        var inactiveStart = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{inactiveId}/start", new
        {
            definitionId = Guid.Empty,
            inputPayload = new Dictionary<string, JsonNode?>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, inactiveStart.StatusCode);
    }

    private static Dictionary<string, JsonNode?> ValidDefinition(string stepId) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["steps"] = new JsonArray(new JsonObject
            {
                ["id"] = stepId,
                ["type"] = "task"
            })
        };

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<SeededWorkflowTenant> SeedMembershipAsync(string subject, string email, string displayName)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Workflow Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new SeededWorkflowTenant(companyId, subject, email, displayName);
    }

    private async Task<CrossTenantWorkflowSeed> SeedTwoMembershipsAsync()
    {
        var first = await SeedMembershipAsync("workflow-owner-a", "workflow-owner-a@example.com", "Workflow Owner A");
        var second = await SeedMembershipAsync("workflow-owner-b", "workflow-owner-b@example.com", "Workflow Owner B");
        return new CrossTenantWorkflowSeed(first.CompanyId, first.Subject, first.Email, first.DisplayName, second.CompanyId, second.Subject, second.Email, second.DisplayName);
    }

    private sealed record SeededWorkflowTenant(Guid CompanyId, string Subject, string Email, string DisplayName);

    private sealed record CrossTenantWorkflowSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid OtherCompanyId,
        string OtherSubject,
        string OtherEmail,
        string OtherDisplayName);
}
