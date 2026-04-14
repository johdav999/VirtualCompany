using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class ConditionTriggerEvaluationPersistenceTests
{
    [Fact]
    public async Task Repository_persists_evaluation_inputs_and_outcome()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        await SeedCompanyAsync(dbContext, companyId);
        var repository = new EfConditionTriggerEvaluationRepository(dbContext);
        var evaluatedUtc = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        var evaluation = CreateEvaluation(companyId, "task-backlog", evaluatedUtc, currentValue: 11, previousOutcome: false, currentOutcome: true, fired: true);

        await repository.AddAsync(evaluation, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var persisted = await repository.GetLatestAsync(companyId, "task-backlog", null, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(evaluatedUtc, persisted.EvaluatedUtc);
        Assert.True(persisted.CurrentOutcome);
        Assert.True(persisted.Fired);
        Assert.Equal(11, persisted.InputValues["currentValue"]!.GetValue<int>());
    }

    [Fact]
    public async Task Repository_returns_latest_prior_evaluation_for_same_tenant_and_definition()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedCompanyAsync(dbContext, companyId);
        await SeedCompanyAsync(dbContext, otherCompanyId);
        var repository = new EfConditionTriggerEvaluationRepository(dbContext);

        await repository.AddAsync(CreateEvaluation(companyId, "task-backlog", new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc), 9, false, false, false), CancellationToken.None);
        await repository.AddAsync(CreateEvaluation(companyId, "task-backlog", new DateTime(2026, 4, 13, 8, 5, 0, DateTimeKind.Utc), 11, false, true, true), CancellationToken.None);
        await repository.AddAsync(CreateEvaluation(otherCompanyId, "task-backlog", new DateTime(2026, 4, 13, 8, 10, 0, DateTimeKind.Utc), 99, false, true, true), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var latest = await repository.GetLatestAsync(companyId, "task-backlog", null, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(companyId, latest.CompanyId);
        Assert.Equal(new DateTime(2026, 4, 13, 8, 5, 0, DateTimeKind.Utc), latest.EvaluatedUtc);
        Assert.Equal(11, latest.InputValues["currentValue"]!.GetValue<int>());
    }

    [Fact]
    public async Task Repository_returns_latest_prior_evaluation_for_same_workflow_trigger()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var firstTriggerId = Guid.NewGuid();
        var secondTriggerId = Guid.NewGuid();
        await SeedCompanyAsync(dbContext, companyId);
        dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
            definitionId,
            companyId,
            "condition-scope-test",
            "Condition scope test",
            "Operations",
            WorkflowTriggerType.Event,
            1,
            new Dictionary<string, JsonNode?> { ["steps"] = new JsonArray() }));
        dbContext.WorkflowTriggers.AddRange(
            new WorkflowTrigger(firstTriggerId, companyId, definitionId, "condition.updated"),
            new WorkflowTrigger(secondTriggerId, companyId, definitionId, "condition.updated"));
        await dbContext.SaveChangesAsync();

        var repository = new EfConditionTriggerEvaluationRepository(dbContext);
        await repository.AddAsync(CreateEvaluation(companyId, "task-backlog", new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc), 9, false, false, false, firstTriggerId), CancellationToken.None);
        await repository.AddAsync(CreateEvaluation(companyId, "task-backlog", new DateTime(2026, 4, 13, 8, 5, 0, DateTimeKind.Utc), 11, false, true, true, firstTriggerId), CancellationToken.None);
        await repository.AddAsync(CreateEvaluation(companyId, "task-backlog", new DateTime(2026, 4, 13, 8, 10, 0, DateTimeKind.Utc), 99, false, true, true, secondTriggerId), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var latest = await repository.GetLatestAsync(companyId, "task-backlog", firstTriggerId, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(companyId, latest.CompanyId);
        Assert.Equal(firstTriggerId, latest.WorkflowTriggerId);
        Assert.Equal(new DateTime(2026, 4, 13, 8, 5, 0, DateTimeKind.Utc), latest.EvaluatedUtc);
        Assert.Equal(11, latest.InputValues["currentValue"]!.GetValue<int>());
    }

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

    private static async Task SeedCompanyAsync(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        dbContext.Companies.Add(new Company(companyId, "Condition Trigger Company"));
        await dbContext.SaveChangesAsync();
    }

    private static ConditionTriggerEvaluation CreateEvaluation(
        Guid companyId,
        string conditionDefinitionId,
        DateTime evaluatedUtc,
        int currentValue,
        bool? previousOutcome,
        bool currentOutcome,
        bool fired,
        Guid? workflowTriggerId = null) =>
        new(
            Guid.NewGuid(),
            companyId,
            conditionDefinitionId,
            workflowTriggerId,
            evaluatedUtc,
            new ConditionTargetReference(ConditionOperandSourceType.Metric, "task.backlog.count", null, null),
            ConditionOperator.GreaterThan,
            ConditionValueType.Number,
            RepeatFiringMode.FalseToTrueTransition,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["currentValue"] = JsonValue.Create(currentValue),
                ["previousValue"] = JsonValue.Create(currentValue - 1),
                ["comparisonValue"] = JsonValue.Create(10)
            },
            previousOutcome,
            currentOutcome,
            fired);
}
