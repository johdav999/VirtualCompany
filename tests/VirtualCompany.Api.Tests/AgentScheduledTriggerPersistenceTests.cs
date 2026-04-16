using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class AgentScheduledTriggerPersistenceTests
{
    [Fact]
    public void Cron_validator_accepts_valid_expression_and_rejects_invalid_expression()
    {
        var validator = new CronosScheduleExpressionValidator();

        Assert.True(validator.ValidateCronExpression("0 9 * * *").IsValid);
        Assert.False(validator.ValidateCronExpression("not a cron").IsValid);
    }

    [Fact]
    public void Timezone_validator_accepts_supported_timezone_and_rejects_invalid_timezone()
    {
        var validator = new CronosScheduleExpressionValidator();

        Assert.True(validator.ValidateTimeZoneId("UTC").IsValid);
        Assert.False(validator.ValidateTimeZoneId("Mars/Olympus").IsValid);
    }

    [Fact]
    public void Next_run_calculator_returns_utc_occurrence_for_timezone()
    {
        var validator = new CronosScheduleExpressionValidator();
        var calculator = new CronosScheduledTriggerNextRunCalculator(validator);

        var nextRun = calculator.GetNextRunUtc(
            "0 9 * * *",
            "Europe/Stockholm",
            new DateTime(2026, 1, 1, 7, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), nextRun);
    }

    [Fact]
    public void Disable_sets_disabled_timestamp_and_clears_next_run()
    {
        var trigger = CreateTrigger(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc));
        var disabledAt = new DateTime(2026, 4, 13, 7, 45, 0, DateTimeKind.Utc);

        trigger.Disable(disabledAt);

        Assert.False(trigger.IsEnabled);
        Assert.Equal(disabledAt, trigger.DisabledUtc);
        Assert.Null(trigger.NextRunUtc);
        Assert.False(trigger.IsEligibleForEnqueue(new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Repository_persists_trigger_and_queries_enabled_due_triggers()
    {
        await using var connection = CreateOpenConnection();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var dueAt = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        await using var dbContext = await CreateDbContextAsync(connection);
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var repository = new EfAgentScheduledTriggerRepository(dbContext);
        var trigger = CreateTrigger(companyId, agentId, dueAt);

        await repository.AddAsync(trigger, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var due = await repository.ListDueAsync(companyId, dueAt, 10, CancellationToken.None);

        Assert.Single(due);
        Assert.Equal(trigger.Id, due[0].Id);
        Assert.Equal("DAILY-STANDUP", due[0].Code);
        Assert.Equal("UTC", due[0].TimeZoneId);
    }

    [Fact]
    public async Task Repository_does_not_return_disabled_triggers_for_due_scan()
    {
        await using var connection = CreateOpenConnection();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var dueAt = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        await using var dbContext = await CreateDbContextAsync(connection);
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var repository = new EfAgentScheduledTriggerRepository(dbContext);
        var trigger = CreateTrigger(companyId, agentId, dueAt);
        trigger.Disable(new DateTime(2026, 4, 13, 7, 55, 0, DateTimeKind.Utc));

        await repository.AddAsync(trigger, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var due = await repository.ListDueAsync(companyId, dueAt, 10, CancellationToken.None);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Enqueue_window_uniqueness_makes_recording_idempotent()
    {
        await using var connection = CreateOpenConnection();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var windowStart = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        var windowEnd = new DateTime(2026, 4, 13, 8, 1, 0, DateTimeKind.Utc);
        await using var dbContext = await CreateDbContextAsync(connection);
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        dbContext.AgentScheduledTriggers.Add(CreateTrigger(companyId, agentId, windowStart, triggerId));
        await dbContext.SaveChangesAsync();
        var repository = new EfAgentScheduledTriggerRepository(dbContext);

        var first = await repository.TryRecordEnqueueWindowAsync(
            new AgentScheduledTriggerEnqueueWindow(Guid.NewGuid(), companyId, triggerId, windowStart, windowEnd, windowStart, "exec-1"),
            CancellationToken.None);
        var second = await repository.TryRecordEnqueueWindowAsync(
            new AgentScheduledTriggerEnqueueWindow(Guid.NewGuid(), companyId, triggerId, windowStart, windowEnd, windowStart.AddSeconds(5), "exec-2"),
            CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await dbContext.AgentScheduledTriggerEnqueueWindows.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Trigger_persistence_rejects_agent_from_different_company()
    {
        await using var connection = CreateOpenConnection();
        var triggerCompanyId = Guid.NewGuid();
        var agentCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(triggerCompanyId, "Trigger Company"));
        await SeedCompanyAndAgentAsync(dbContext, agentCompanyId, agentId);

        dbContext.AgentScheduledTriggers.Add(CreateTrigger(
            triggerCompanyId,
            agentId,
            new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Enqueue_window_persistence_rejects_trigger_from_different_company()
    {
        await using var connection = CreateOpenConnection();
        var triggerCompanyId = Guid.NewGuid();
        var windowCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var windowStart = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        await using var dbContext = await CreateDbContextAsync(connection);
        await SeedCompanyAndAgentAsync(dbContext, triggerCompanyId, agentId);
        dbContext.Companies.Add(new Company(windowCompanyId, "Window Company"));
        dbContext.AgentScheduledTriggers.Add(CreateTrigger(triggerCompanyId, agentId, windowStart, triggerId));
        await dbContext.SaveChangesAsync();

        dbContext.AgentScheduledTriggerEnqueueWindows.Add(new AgentScheduledTriggerEnqueueWindow(
            Guid.NewGuid(),
            windowCompanyId,
            triggerId,
            windowStart,
            windowStart.AddMinutes(1),
            windowStart,
            "exec-tenant-mismatch"));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
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

    private static async Task SeedCompanyAndAgentAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid agentId)
    {
        dbContext.Companies.Add(new Company(companyId, "Schedule Test Company"));
        dbContext.Agents.Add(new Agent(
            agentId,
            companyId,
            "scheduler",
            "Scheduler",
            "Operations Agent",
            "Operations",
            null,
            AgentSeniority.Mid,
            AgentStatus.Active,
            AgentAutonomyLevel.Assisted));
        await dbContext.SaveChangesAsync();
    }

    private static AgentScheduledTrigger CreateTrigger(
        Guid companyId,
        Guid agentId,
        DateTime nextRunUtc,
        Guid? triggerId = null) =>
        new(
            triggerId ?? Guid.NewGuid(),
            companyId,
            agentId,
            "Daily standup",
            "daily-standup",
            "0 8 * * *",
            "UTC",
            nextRunUtc,
            true);
}
