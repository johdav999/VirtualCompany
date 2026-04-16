using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ProactiveTaskCreationServiceTests
{
    [Fact]
    public void Mapper_creates_normalized_agent_task_request_from_trigger_payload()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var mapper = new DefaultTriggerToTaskMappingService();

        var mapped = mapper.Map(new ProactiveTaskTrigger(
            companyId,
            agentId,
            "condition",
            "trigger-event-1",
            "correlation-1",
            "Inventory drift crossed the threshold.",
            new Dictionary<string, JsonNode?>
            {
                ["title"] = JsonValue.Create("Review inventory drift"),
                ["priority"] = JsonValue.Create("high")
            }));

        Assert.Equal(companyId, mapped.CompanyId);
        Assert.Equal(agentId, mapped.AgentId);
        Assert.Equal(agentId, mapped.AssignedAgentId);
        Assert.Equal("condition", mapped.TriggerSource);
        Assert.Equal("trigger-event-1", mapped.TriggerEventId);
        Assert.Equal("correlation-1", mapped.CorrelationId);
        Assert.Equal("Review inventory drift", mapped.Title);
        Assert.Equal("high", mapped.Priority);
        Assert.Equal(WorkTaskStatus.New.ToStorageValue(), mapped.Status);
        Assert.Equal(WorkTaskSourceTypes.Agent, mapped.InputPayload["sourceType"]!.GetValue<string>());
        Assert.Equal("trigger-event-1", mapped.InputPayload["triggerEventKey"]!.GetValue<string>());
        Assert.Equal(agentId.ToString("N"), mapped.InputPayload["originatingAgentId"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateAsync_persists_agent_metadata_and_audit_event()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var service = CreateService(dbContext);

        var result = await service.CreateAsync(
            new CreateAgentInitiatedTaskCommand(CreateTrigger(companyId, agentId)),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.False(result.Duplicate);

        var task = await dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == result.TaskId);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal("Review inventory drift", task.Title);
        Assert.Equal("Inventory drift crossed the threshold.", task.Description);
        Assert.Equal(WorkTaskPriority.High, task.Priority);
        Assert.Equal(WorkTaskStatus.New, task.Status);
        Assert.Equal(agentId, task.AssignedAgentId);
        Assert.Equal(WorkTaskSourceTypes.Agent, task.SourceType);
        Assert.Equal(agentId, task.OriginatingAgentId);
        Assert.Equal("condition", task.TriggerSource);
        Assert.Equal("trigger-event-1", task.TriggerEventId);
        Assert.Equal("Inventory drift crossed the threshold.", task.CreationReason);
        Assert.Equal("correlation-1", task.CorrelationId);
        Assert.Equal(AuditActorTypes.Agent, task.CreatedByActorType);
        Assert.Equal(agentId, task.CreatedByActorId);

        var audit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Action == AuditEventActions.AgentInitiatedTaskCreated);
        Assert.Equal(companyId, audit.CompanyId);
        Assert.Equal(AuditActorTypes.Agent, audit.ActorType);
        Assert.Equal(agentId, audit.ActorId);
        Assert.Equal(result.TaskId.ToString("N"), audit.TargetId);
        Assert.Equal(AuditEventOutcomes.Succeeded, audit.Outcome);
        Assert.Equal("correlation-1", audit.CorrelationId);
        Assert.Equal(agentId.ToString("N"), audit.Metadata["agentId"]);
        Assert.Equal(companyId.ToString("N"), audit.Metadata["companyId"]);
        Assert.Equal("condition", audit.Metadata["triggerSource"]);
        Assert.NotNull(audit.PayloadDiffJson);
        Assert.Contains("Review inventory drift", audit.PayloadDiffJson);
        Assert.True(audit.OccurredUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task CreateAsync_returns_duplicate_without_second_task_or_audit_inside_deduplication_window()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var service = CreateService(dbContext);
        var command = new CreateAgentInitiatedTaskCommand(CreateTrigger(companyId, agentId));

        var first = await service.CreateAsync(command, CancellationToken.None);
        var second = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(first.Duplicate);
        Assert.False(second.Created);
        Assert.True(second.Duplicate);
        Assert.Equal(first.TaskId, second.TaskId);
        Assert.Equal(1, await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await dbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.Action == AuditEventActions.AgentInitiatedTaskCreated));

        var dedupeReceipt = await dbContext.AgentTaskCreationDedupeRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(first.TaskId, dedupeReceipt.TaskId);
        Assert.Equal("condition", dedupeReceipt.TriggerSource);
        Assert.Equal("trigger-event-1", dedupeReceipt.TriggerEventId);
        Assert.Equal("correlation-1", dedupeReceipt.CorrelationId);
        Assert.True(dedupeReceipt.ExpiresUtc > dedupeReceipt.CreatedUtc);
    }

    [Fact]
    public async Task CreateAsync_validates_required_mapped_fields()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var service = CreateService(
            dbContext,
            mapper: new StubMapper(new MappedTaskCreationRequest(
                companyId,
                agentId,
                "",
                "",
                "",
                "",
                "",
                "",
                null,
                "",
                "",
                null,
                null,
                [])));

        var ex = await Assert.ThrowsAsync<TaskValidationException>(() =>
            service.CreateAsync(
                new CreateAgentInitiatedTaskCommand(CreateTrigger(companyId, agentId)),
                CancellationToken.None));

        Assert.Contains(nameof(MappedTaskCreationRequest.TriggerSource), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.TriggerEventId), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.CorrelationId), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.CreationReason), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.Type), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.Title), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.Description), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.Priority), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.Status), ex.Errors.Keys);
        Assert.Contains(nameof(MappedTaskCreationRequest.AssignedAgentId), ex.Errors.Keys);
    }

    [Fact]
    public async Task Duplicate_detector_scopes_by_company_trigger_event_correlation_and_window()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var service = CreateService(dbContext, options: new ProactiveTaskCreationOptions { DeduplicationWindowSeconds = 1 });
        var command = new CreateAgentInitiatedTaskCommand(CreateTrigger(companyId, agentId));

        var first = await service.CreateAsync(command, CancellationToken.None);
        await Task.Delay(1100);
        var second = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.TaskId, second.TaskId);
        Assert.Equal(2, await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await dbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.Action == AuditEventActions.AgentInitiatedTaskCreated));
    }

    [Fact]
    public async Task Duplicate_detection_is_tenant_scoped()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyOneId = Guid.NewGuid();
        var companyTwoId = Guid.NewGuid();
        var agentOneId = Guid.NewGuid();
        var agentTwoId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyOneId, agentOneId);
        await SeedCompanyAndAgentAsync(dbContext, companyTwoId, agentTwoId);
        var service = CreateService(dbContext);

        var first = await service.CreateAsync(
            new CreateAgentInitiatedTaskCommand(CreateTrigger(companyOneId, agentOneId)),
            CancellationToken.None);
        var second = await service.CreateAsync(
            new CreateAgentInitiatedTaskCommand(CreateTrigger(companyTwoId, agentTwoId)),
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.False(first.Duplicate);
        Assert.False(second.Duplicate);
        Assert.Equal(2, await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            1,
            await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyOneId && x.CorrelationId == "correlation-1"));
        Assert.Equal(
            1,
            await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyTwoId && x.CorrelationId == "correlation-1"));
    }

    private static ProactiveTaskCreationService CreateService(
        VirtualCompanyDbContext dbContext,
        ITriggerToTaskMappingService? mapper = null,
        ProactiveTaskCreationOptions? options = null) =>
        new(
            dbContext,
            mapper ?? new DefaultTriggerToTaskMappingService(),
            new EfProactiveTaskDuplicateDetector(dbContext),
            new CompanyAgentAssignmentGuard(dbContext),
            new AuditEventWriter(dbContext),
            new NoOpOutboxEnqueuer(),
            new NoOpDashboardCache(),
            TimeProvider.System,
            Options.Create(options ?? new ProactiveTaskCreationOptions()));

    private static ProactiveTaskTrigger CreateTrigger(Guid companyId, Guid agentId) =>
        new(
            companyId,
            agentId,
            "condition",
            "trigger-event-1",
            "correlation-1",
            "Inventory drift crossed the threshold.",
            new Dictionary<string, JsonNode?>
            {
                ["title"] = JsonValue.Create("Review inventory drift"),
                ["description"] = JsonValue.Create("Inventory drift crossed the threshold."),
                ["priority"] = JsonValue.Create("high")
            });

    private static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new VirtualCompanyDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static async Task SeedCompanyAndAgentAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid agentId)
    {
        dbContext.Companies.Add(new Company(companyId, "Proactive Task Test Company"));
        dbContext.Agents.Add(new Agent(
            agentId,
            companyId,
            "proactive-agent",
            "Proactive Agent",
            "Operations Agent",
            "Operations",
            null,
            AgentSeniority.Mid,
            AgentStatus.Active,
            AgentAutonomyLevel.Assisted));
        await dbContext.SaveChangesAsync();
    }

    private sealed class StubMapper : ITriggerToTaskMappingService
    {
        private readonly MappedTaskCreationRequest _request;

        public StubMapper(MappedTaskCreationRequest request)
        {
            _request = request;
        }

        public MappedTaskCreationRequest Map(ProactiveTaskTrigger trigger) => _request;
    }

    private sealed class NoOpDashboardCache : IExecutiveCockpitDashboardCache
    {
        public Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpOutboxEnqueuer : ICompanyOutboxEnqueuer
    {
        public void Enqueue(
            Guid companyId,
            string topic,
            PlatformEventEnvelope payload,
            string? correlationId,
            string? idempotencyKey = null,
            string? causationId = null)
        {
        }
    }
}