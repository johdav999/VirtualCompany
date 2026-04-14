using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class WorkflowEventTriggerFoundationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly TestWebApplicationFactory _factory;

    public WorkflowEventTriggerFoundationTests(TestWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public void Supported_event_registry_contains_canonical_platform_events()
    {
        var registry = SupportedPlatformEventTypeRegistry.Instance;

        Assert.Contains(SupportedPlatformEventTypeRegistry.TaskCreated, registry.SupportedEventTypes);
        Assert.Contains(SupportedPlatformEventTypeRegistry.TaskUpdated, registry.SupportedEventTypes);
        Assert.Contains(SupportedPlatformEventTypeRegistry.DocumentUploaded, registry.SupportedEventTypes);
        Assert.Contains(SupportedPlatformEventTypeRegistry.WorkflowStateChanged, registry.SupportedEventTypes);
        Assert.True(registry.IsSupported("TASK_CREATED"));
        Assert.False(registry.IsSupported("task.deleted"));
    }

    [Fact]
    public async Task Trigger_creation_rejects_unsupported_event_type()
    {
        var seed = await SeedMembershipAsync("event-trigger-invalid", "event-trigger-invalid@example.com", "Event Trigger Invalid");
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var definition = await CreateEventDefinitionAsync(client, seed.CompanyId, "unsupported-event-definition");

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition.Id}/triggers", new
        {
            eventName = "task.deleted",
            criteriaJson = new Dictionary<string, JsonNode?>(),
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Event_envelope_matches_enabled_same_tenant_triggers_only()
    {
        var seed = await SeedTwoMembershipsAsync();
        var enabledDefinitionId = Guid.NewGuid();
        var disabledDefinitionId = Guid.NewGuid();
        var otherDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                enabledDefinitionId,
                seed.CompanyId,
                "task-created-enabled",
                "Task created enabled",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("enabled-step")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                disabledDefinitionId,
                seed.CompanyId,
                "task-created-disabled",
                "Task created disabled",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("disabled-step")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                otherDefinitionId,
                seed.OtherCompanyId,
                "task-created-other",
                "Task created other",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("other-step")));

            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                seed.CompanyId,
                enabledDefinitionId,
                SupportedPlatformEventTypeRegistry.TaskCreated));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                seed.CompanyId,
                disabledDefinitionId,
                SupportedPlatformEventTypeRegistry.TaskCreated,
                isEnabled: false));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                seed.OtherCompanyId,
                otherDefinitionId,
                SupportedPlatformEventTypeRegistry.TaskCreated));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
        var result = await eventTriggers.HandleAsync(
            BuildEnvelope(seed.CompanyId, "evt-task-created-1", SupportedPlatformEventTypeRegistry.TaskCreated),
            CancellationToken.None);

        var instance = Assert.Single(result.StartedInstances);
        Assert.Equal(enabledDefinitionId, instance.DefinitionId);
        Assert.Equal(seed.CompanyId, instance.CompanyId);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var allStarted = await dbContext.WorkflowInstances.IgnoreQueryFilters().Where(x => x.TriggerRef == "evt-task-created-1").ToListAsync();
        Assert.Single(allStarted);
    }

    [Fact]
    public async Task Trigger_creation_accepts_supported_event_type()
    {
        var seed = await SeedMembershipAsync("event-trigger-valid", "event-trigger-valid@example.com", "Event Trigger Valid");
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var definition = await CreateEventDefinitionAsync(client, seed.CompanyId, "supported-event-definition");

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/workflows/definitions/{definition.Id}/triggers", new
        {
            eventName = SupportedPlatformEventTypeRegistry.TaskUpdated,
            criteriaJson = new Dictionary<string, JsonNode?>(),
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_event_delivery_creates_one_execution_per_trigger_and_event_id()
    {
        var seed = await SeedMembershipAsync("event-trigger-idempotent", "event-trigger-idempotent@example.com", "Event Trigger Idempotent");
        var definitionId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "document-uploaded",
                "Document uploaded",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("index-document")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                triggerId,
                seed.CompanyId,
                definitionId,
                SupportedPlatformEventTypeRegistry.DocumentUploaded));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
        var envelope = BuildEnvelope(seed.CompanyId, "evt-document-uploaded-1", SupportedPlatformEventTypeRegistry.DocumentUploaded);

        var first = await eventTriggers.HandleAsync(envelope, CancellationToken.None);
        var second = await eventTriggers.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(first.StartedInstances);
        Assert.Empty(second.StartedInstances);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var instance = await dbContext.WorkflowInstances.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.DefinitionId == definitionId &&
            x.TriggerRef == envelope.EventId);
        Assert.Equal(envelope.EventId, instance.InputPayload["eventId"]!.GetValue<string>());
        Assert.Equal(envelope.EventType, instance.InputPayload["eventType"]!.GetValue<string>());
        Assert.Equal(seed.CompanyId, instance.InputPayload["companyId"]!.GetValue<Guid>());
        Assert.Equal(envelope.CorrelationId, instance.InputPayload["correlationId"]!.GetValue<string>());
        Assert.Equal(envelope.SourceEntityType, instance.InputPayload["sourceEntityType"]!.GetValue<string>());
        Assert.Equal(envelope.SourceEntityId, instance.InputPayload["sourceEntityId"]!.GetValue<string>());
        var metadata = Assert.IsType<JsonObject>(instance.InputPayload["metadata"]);
        Assert.Equal("test", metadata["source"]!.GetValue<string>());

        Assert.Equal(1, await dbContext.ProcessedWorkflowTriggerEvents.IgnoreQueryFilters().CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.WorkflowTriggerId == triggerId &&
            x.EventId == envelope.EventId));
        Assert.Equal(1, await dbContext.WorkflowInstances.IgnoreQueryFilters().CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.DefinitionId == definitionId &&
            x.TriggerRef == envelope.EventId));
    }

    [Fact]
    public async Task Duplicate_event_delivery_under_concurrency_creates_one_execution()
    {
        var seed = await SeedMembershipAsync("event-trigger-concurrent", "event-trigger-concurrent@example.com", "Event Trigger Concurrent");
        var definitionId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "task-updated-concurrent",
                "Task updated concurrent",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("sync-task")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                triggerId,
                seed.CompanyId,
                definitionId,
                SupportedPlatformEventTypeRegistry.TaskUpdated));
            return Task.CompletedTask;
        });

        var envelope = BuildEnvelope(seed.CompanyId, "evt-task-updated-concurrent-1", SupportedPlatformEventTypeRegistry.TaskUpdated);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
            return await eventTriggers.HandleAsync(envelope, CancellationToken.None);
        }));

        Assert.Equal(1, results.Sum(x => x.StartedInstances.Count));

        await using var assertionScope = _factory.Services.CreateAsyncScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal(1, await dbContext.ProcessedWorkflowTriggerEvents.IgnoreQueryFilters().CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.WorkflowTriggerId == triggerId &&
            x.EventId == envelope.EventId));
        Assert.Equal(1, await dbContext.WorkflowInstances.IgnoreQueryFilters().CountAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.DefinitionId == definitionId &&
            x.TriggerRef == envelope.EventId));
    }

    [Fact]
    public async Task Same_event_id_can_start_one_execution_per_trigger_and_tenant()
    {
        var seed = await SeedTwoMembershipsAsync();
        var firstDefinitionId = Guid.NewGuid();
        var secondDefinitionId = Guid.NewGuid();
        var otherDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(firstDefinitionId, seed.CompanyId, "document-one", "Document one", "Operations", WorkflowTriggerType.Event, 1, ValidDefinition("document-one")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(secondDefinitionId, seed.CompanyId, "document-two", "Document two", "Operations", WorkflowTriggerType.Event, 1, ValidDefinition("document-two")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(otherDefinitionId, seed.OtherCompanyId, "document-other", "Document other", "Operations", WorkflowTriggerType.Event, 1, ValidDefinition("document-other")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(Guid.NewGuid(), seed.CompanyId, firstDefinitionId, SupportedPlatformEventTypeRegistry.DocumentUploaded));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(Guid.NewGuid(), seed.CompanyId, secondDefinitionId, SupportedPlatformEventTypeRegistry.DocumentUploaded));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(Guid.NewGuid(), seed.OtherCompanyId, otherDefinitionId, SupportedPlatformEventTypeRegistry.DocumentUploaded));
            return Task.CompletedTask;
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var eventTriggers = scope.ServiceProvider.GetRequiredService<IInternalWorkflowEventTriggerService>();
        var eventId = "evt-document-shared-1";

        var firstTenantResult = await eventTriggers.HandleAsync(BuildEnvelope(seed.CompanyId, eventId, SupportedPlatformEventTypeRegistry.DocumentUploaded), CancellationToken.None);
        var otherTenantResult = await eventTriggers.HandleAsync(BuildEnvelope(seed.OtherCompanyId, eventId, SupportedPlatformEventTypeRegistry.DocumentUploaded), CancellationToken.None);

        Assert.Equal(2, firstTenantResult.StartedInstances.Count);
        Assert.Single(otherTenantResult.StartedInstances);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal(2, await dbContext.WorkflowInstances.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.TriggerRef == eventId));
        Assert.Equal(1, await dbContext.WorkflowInstances.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.OtherCompanyId && x.TriggerRef == eventId));
    }

    [Fact]
    public async Task Outbox_platform_event_consumer_matches_workflow_triggers()
    {
        var seed = await SeedMembershipAsync("event-trigger-outbox", "event-trigger-outbox@example.com", "Event Trigger Outbox");
        var definitionId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var outboxMessageId = Guid.NewGuid();
        var envelope = BuildEnvelope(seed.CompanyId, "evt-task-created-outbox-1", SupportedPlatformEventTypeRegistry.TaskCreated);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                definitionId,
                seed.CompanyId,
                "task-created-outbox",
                "Task created outbox",
                "Operations",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("outbox-step")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                triggerId,
                seed.CompanyId,
                definitionId,
                SupportedPlatformEventTypeRegistry.TaskCreated));
            dbContext.CompanyOutboxMessages.Add(new CompanyOutboxMessage(
                outboxMessageId,
                seed.CompanyId,
                CompanyOutboxTopics.TaskCreated,
                JsonSerializer.Serialize(envelope, SerializerOptions),
                correlationId: envelope.CorrelationId,
                messageType: typeof(PlatformEventEnvelope).FullName,
                idempotencyKey: $"platform-event:{seed.CompanyId:N}:{envelope.EventId}",
                causationId: envelope.SourceEntityId));
            return Task.CompletedTask;
        });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            var handledCount = await processor.DispatchPendingAsync(CancellationToken.None);

            Assert.Equal(1, handledCount);
        }

        await using var assertionScope = _factory.Services.CreateAsyncScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var instance = await dbContext.WorkflowInstances.IgnoreQueryFilters().SingleAsync(x => x.TriggerRef == envelope.EventId);
        var outboxMessage = await dbContext.CompanyOutboxMessages.IgnoreQueryFilters().SingleAsync(x => x.Id == outboxMessageId);

        Assert.Equal(seed.CompanyId, instance.CompanyId);
        Assert.Equal(definitionId, instance.DefinitionId);
        Assert.Equal(triggerId, instance.TriggerId);
        Assert.NotNull(outboxMessage.ProcessedUtc);
        Assert.Equal(CompanyOutboxMessageStatus.Dispatched, outboxMessage.Status);
    }

    [Fact]
    public void Platform_event_envelope_carries_required_metadata_contract()
    {
        var companyId = Guid.NewGuid();
        var envelope = BuildEnvelope(companyId, "evt-contract-1", SupportedPlatformEventTypeRegistry.WorkflowStateChanged);

        Assert.Equal("evt-contract-1", envelope.EventId);
        Assert.Equal(SupportedPlatformEventTypeRegistry.WorkflowStateChanged, envelope.EventType);
        Assert.Equal(companyId, envelope.CompanyId);
        Assert.Equal("corr-evt-contract-1", envelope.CorrelationId);
        Assert.Equal("work_task", envelope.SourceEntityType);
        Assert.Equal("source-evt-contract-1", envelope.SourceEntityId);
        Assert.True(envelope.Metadata.ContainsKey("source"));
    }

    private static PlatformEventEnvelope BuildEnvelope(Guid companyId, string eventId, string eventType) =>
        new(
            eventId,
            eventType,
            DateTime.Parse("2026-04-13T08:00:00Z").ToUniversalTime(),
            companyId,
            $"corr-{eventId}",
            "work_task",
            $"source-{eventId}",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = JsonValue.Create("test")
            });

    private static Dictionary<string, JsonNode?> ValidDefinition(string stepId) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["steps"] = new JsonArray(new JsonObject { ["id"] = stepId, ["type"] = "task" })
        };

    private async Task<WorkflowDefinitionDto> CreateEventDefinitionAsync(HttpClient client, Guid companyId, string code)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/workflows/definitions", new
        {
            code,
            name = code,
            department = "Operations",
            triggerType = "event",
            definitionJson = ValidDefinition("event-step")
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkflowDefinitionDto>())!;
    }

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
            dbContext.Companies.Add(new Company(companyId, "Workflow Event Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new SeededWorkflowTenant(companyId, subject, email, displayName);
    }

    private async Task<CrossTenantWorkflowSeed> SeedTwoMembershipsAsync()
    {
        var first = await SeedMembershipAsync("workflow-event-owner-a", "workflow-event-owner-a@example.com", "Workflow Event Owner A");
        var second = await SeedMembershipAsync("workflow-event-owner-b", "workflow-event-owner-b@example.com", "Workflow Event Owner B");
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