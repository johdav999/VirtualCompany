using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySimulationStateRepositoryTests
{
    [Fact]
    public async Task Start_and_get_current_state_persists_company_scoped_session()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var startSimulatedUtc = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc);

        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var state = await repository.StartAsync(
            new StartCompanySimulationStateCommand(
                companyId,
                startSimulatedUtc,
                GenerationEnabled: true,
                Seed: 42,
                DeterministicConfigurationJson: """{"profile":"baseline","speed":"1d/10s"}""",
                TransitionedUtc: new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var stored = await repository.GetCurrentAsync(companyId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(state.Id, stored!.Id);
        Assert.Equal(companyId, stored.CompanyId);
        Assert.Equal(CompanySimulationStatus.Running, stored.Status);
        Assert.Equal(startSimulatedUtc, stored.StartSimulatedUtc);
        Assert.Equal(startSimulatedUtc, stored.CurrentSimulatedUtc);
        Assert.True(stored.GenerationEnabled);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc), stored.LastProgressedUtc);
        Assert.Equal(42, stored.Seed);
        Assert.NotNull(stored.ActiveSessionId);
    }

    [Fact]
    public async Task Update_pause_resume_stop_and_restart_preserve_expected_session_semantics()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();

        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Lifecycle Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var initial = await repository.StartAsync(
            new StartCompanySimulationStateCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                11,
                TransitionedUtc: new DateTime(2026, 4, 19, 8, 55, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        var initialSessionId = initial.ActiveSessionId;

        var updated = await repository.UpdateAsync(
            new UpdateCompanySimulationStateCommand(
                companyId,
                new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc),
                GenerationEnabled: false,
                DeterministicConfigurationJson: """{"profile":"paused-ready"}"""),
            CancellationToken.None);
        var paused = await repository.PauseAsync(
            new PauseCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 19, 9, 5, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        var resumed = await repository.ResumeAsync(
            new ResumeCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 19, 9, 10, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        var stopped = await repository.StopAsync(
            new StopCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 19, 9, 15, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        var restarted = await repository.StartAsync(
            new StartCompanySimulationStateCommand(
                companyId,
                new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                GenerationEnabled: true,
                Seed: 22,
                DeterministicConfigurationJson: """{"profile":"restart"}""",
                TransitionedUtc: new DateTime(2026, 4, 19, 9, 20, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Equal(new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), updated.CurrentSimulatedUtc);
        Assert.Equal(new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc), updated.LastProgressedUtc);
        Assert.False(updated.GenerationEnabled);
        Assert.Equal(CompanySimulationStatus.Paused, paused.Status);
        Assert.Equal(new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), paused.CurrentSimulatedUtc);
        Assert.Equal(CompanySimulationStatus.Running, resumed.Status);
        Assert.Equal(new DateTime(2026, 4, 19, 9, 10, 0, DateTimeKind.Utc), resumed.LastProgressedUtc);
        Assert.Equal(initialSessionId, resumed.ActiveSessionId);
        Assert.Equal(CompanySimulationStatus.Stopped, stopped.Status);
        Assert.Null(stopped.ActiveSessionId);
        Assert.Equal(new DateTime(2026, 4, 19, 9, 15, 0, DateTimeKind.Utc), stopped.StoppedUtc);
        Assert.Null(await repository.GetByActiveSessionAsync(companyId, initialSessionId!.Value, CancellationToken.None));
        Assert.Equal(CompanySimulationStatus.Running, restarted.Status);
        Assert.NotNull(restarted.ActiveSessionId);
        Assert.NotEqual(initialSessionId, restarted.ActiveSessionId);
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), restarted.StartSimulatedUtc);
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), restarted.CurrentSimulatedUtc);
        Assert.Equal(new DateTime(2026, 4, 19, 9, 20, 0, DateTimeKind.Utc), restarted.LastProgressedUtc);
        Assert.Null(restarted.StoppedUtc);
        Assert.True(restarted.GenerationEnabled);
        Assert.Equal(22, restarted.Seed);
    }

    [Fact]
    public async Task Save_stopped_draft_creates_and_updates_persisted_prestart_preferences()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();

        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Draft Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var created = await repository.SaveStoppedDraftAsync(
            new SaveCompanySimulationStoppedDraftCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                GenerationEnabled: false,
                Seed: 9,
                DeterministicConfigurationJson: """{"profile":"draft"}""",
                UpdatedUtc: new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var updated = await repository.SaveStoppedDraftAsync(
            new SaveCompanySimulationStoppedDraftCommand(
                companyId,
                created.CurrentSimulatedUtc,
                GenerationEnabled: true,
                Seed: 9,
                DeterministicConfigurationJson: """{"profile":"draft-updated"}""",
                UpdatedUtc: new DateTime(2026, 4, 19, 8, 5, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Equal(CompanySimulationStatus.Stopped, created.Status);
        Assert.Null(created.ActiveSessionId);
        Assert.False(created.GenerationEnabled);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), created.CurrentSimulatedUtc);
        Assert.Equal(CompanySimulationStatus.Stopped, updated.Status);
        Assert.True(updated.GenerationEnabled);
        Assert.Equal("""{"profile":"draft-updated"}""", updated.DeterministicConfigurationJson);
        Assert.Null(updated.ActiveSessionId);
    }

    [Fact]
    public async Task Repository_and_query_filter_enforce_company_isolation()
    {
        await using var connection = await OpenConnectionAsync();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await using (var seedContext = await CreateDbContextAsync(connection))
        {
            seedContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            await seedContext.SaveChangesAsync();

            var repository = new EfCompanySimulationStateRepository(seedContext);
            await repository.StartAsync(new StartCompanySimulationStateCommand(companyAId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 1), CancellationToken.None);
            await repository.StartAsync(new StartCompanySimulationStateCommand(companyBId, new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), false, 2), CancellationToken.None);
        }

        Guid sessionAId;
        await using (var companyAContext = await CreateDbContextAsync(connection, companyAId))
        {
            var visible = await companyAContext.CompanySimulationStates.AsNoTracking().ToListAsync();
            var state = Assert.Single(visible);
            sessionAId = state.ActiveSessionId!.Value;
            Assert.Equal(companyAId, state.CompanyId);
        }

        await using var companyBContext = await CreateDbContextAsync(connection, companyBId);
        var repositoryB = new EfCompanySimulationStateRepository(companyBContext);
        var visibleToB = await companyBContext.CompanySimulationStates.AsNoTracking().ToListAsync();

        Assert.Single(visibleToB);
        Assert.Equal(companyBId, visibleToB[0].CompanyId);
        Assert.Null(await repositoryB.GetByActiveSessionAsync(companyBId, sessionAId, CancellationToken.None));
    }

    [Fact]
    public async Task Repository_persists_run_history_transitions_and_daily_logs()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();

        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "History Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var started = await repository.StartAsync(
            new StartCompanySimulationStateCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                11,
                TransitionedUtc: new DateTime(2026, 4, 19, 8, 55, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        await repository.RecordFinanceGenerationAsync(
            companyId,
            started.ActiveSessionId!.Value,
            new CompanySimulationFinanceGenerationResultDto(
                companyId,
                started.ActiveSessionId.Value,
                1,
                1,
                1,
                2,
                0,
                1,
                0,
                0,
                0,
                0,
                1,
                [
                    new CompanySimulationFinanceGenerationDayLogDto(
                        new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                        2,
                        1,
                        1,
                        1,
                        1,
                        ["duplicate_vendor_charge"],
                        ["The simulated day completed without generating finance records."],
                        [])
                ]),
            new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var history = await repository.GetRecentHistoryAsync(companyId, 10, CancellationToken.None);
        var run = Assert.Single(history);
        Assert.Single(run.StatusTransitions);
        Assert.Single(run.DayLogs);
        Assert.Contains("duplicate_vendor_charge", run.InjectedAnomalies);
    }

    [Fact]
    public async Task Deterministic_inputs_round_trip_unchanged()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        const string deterministicConfiguration = """{"startDate":"2026-04-01T00:00:00Z","profile":"replayable","generationEnabled":true}""";

        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Deterministic Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        await repository.StartAsync(
            new StartCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 8675309, deterministicConfiguration),
            CancellationToken.None);

        var stored = await repository.GetCurrentAsync(companyId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(8675309, stored!.Seed);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), stored.StartSimulatedUtc);
        Assert.Equal(deterministicConfiguration, stored.DeterministicConfigurationJson);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection, Guid? companyId = null)
    {
        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(companyId);
        var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            accessor);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }
}
