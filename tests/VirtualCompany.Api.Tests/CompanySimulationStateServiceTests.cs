using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySimulationStateServiceTests
{
    [Fact]
    public async Task Get_state_returns_not_started_projection_with_empty_session_fields()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        var state = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        Assert.Equal(CompanySimulationLifecycleStatusValues.NotStarted, state.Status);
        Assert.Null(state.CurrentSimulatedDateTime);
        Assert.Null(state.LastProgressionTimestamp);
        Assert.True(state.GenerationEnabled);
        Assert.Null(state.Seed);
        Assert.Null(state.ActiveSessionId);
        Assert.True(state.CanStart);
        Assert.False(state.CanPause);
        Assert.False(state.CanStop);
        Assert.True(state.CanToggleGeneration);
        Assert.True(state.SupportsStepForwardOneDay);
        Assert.True(state.SupportsRefresh);
    }

    [Fact]
    public async Task Generation_setting_changes_are_blocked_while_running_but_persist_when_paused_or_stopped()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 73),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateSettingsAsync(
            new UpdateCompanySimulationSettingsCommand(companyId, GenerationEnabled: false),
            CancellationToken.None));

        await service.PauseAsync(new PauseCompanySimulationCommand(companyId), CancellationToken.None);
        var paused = await service.UpdateSettingsAsync(
            new UpdateCompanySimulationSettingsCommand(companyId, GenerationEnabled: false),
            CancellationToken.None);

        await service.StopAsync(new StopCompanySimulationCommand(companyId), CancellationToken.None);
        var stopped = await service.UpdateSettingsAsync(
            new UpdateCompanySimulationSettingsCommand(companyId, GenerationEnabled: true),
            CancellationToken.None);

        Assert.False(paused.GenerationEnabled);
        Assert.True(paused.CanStart);
        Assert.True(paused.CanToggleGeneration);
        Assert.True(stopped.GenerationEnabled);
        Assert.True(stopped.CanStart);
        Assert.True(stopped.CanToggleGeneration);
    }

    [Fact]
    public async Task Update_settings_before_first_start_creates_a_stopped_draft_that_persists_generation_preferences()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Draft Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        var updated = await service.UpdateSettingsAsync(
            new UpdateCompanySimulationSettingsCommand(companyId, GenerationEnabled: false, DeterministicConfigurationJson: """{"profile":"prestart"}"""),
            CancellationToken.None);
        var reloaded = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, updated.Status);
        Assert.False(updated.GenerationEnabled);
        Assert.True(updated.CanStart);
        Assert.True(updated.CanToggleGeneration);
        Assert.True(updated.SupportsStepForwardOneDay);
        Assert.Null(updated.ActiveSessionId);
        Assert.False(reloaded.GenerationEnabled);
        Assert.Equal("""{"profile":"prestart"}""", reloaded.DeterministicConfigurationJson);
    }

    [Fact]
    public async Task Step_forward_advances_exactly_one_day_and_only_generates_when_generation_is_enabled()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Step Forward Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var generationPolicy = new RecordingGenerationPolicy();
        var service = new CompanySimulationStateService(
            repository,
            timeProvider,
            companyContextAccessor: null,
            distributedLockProvider: null,
            financeGenerationPolicy: generationPolicy,
            logger: NullLogger<CompanySimulationStateService>.Instance);

        await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 73),
            CancellationToken.None);
        await service.PauseAsync(new PauseCompanySimulationCommand(companyId), CancellationToken.None);

        var afterGeneratedStep = await service.StepForwardOneDayAsync(
            new StepForwardCompanySimulationOneDayCommand(companyId),
            CancellationToken.None);

        await service.UpdateSettingsAsync(
            new UpdateCompanySimulationSettingsCommand(companyId, GenerationEnabled: false),
            CancellationToken.None);
        var afterNonGeneratedStep = await service.StepForwardOneDayAsync(
            new StepForwardCompanySimulationOneDayCommand(companyId),
            CancellationToken.None);

        Assert.Equal(CompanySimulationLifecycleStatusValues.Paused, afterGeneratedStep.Status);
        Assert.Equal(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), afterGeneratedStep.CurrentSimulatedDateTime);
        Assert.Single(generationPolicy.Commands);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), generationPolicy.Commands[0].PreviousSimulatedUtc);
        Assert.Equal(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), generationPolicy.Commands[0].CurrentSimulatedUtc);
        Assert.Equal(new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), afterNonGeneratedStep.CurrentSimulatedDateTime);
        Assert.Single(generationPolicy.Commands);
    }

    [Fact]
    public async Task Get_state_advances_only_complete_ten_second_buckets_and_resume_resets_the_baseline()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        var start = await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 73, """{"profile":"replayable"}"""),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(9));
        var beforeBucket = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(16));
        var afterTwentyFiveSeconds = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var paused = await service.PauseAsync(new PauseCompanySimulationCommand(companyId), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(40));
        var stillPaused = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var resumed = await service.ResumeAsync(new ResumeCompanySimulationCommand(companyId), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var afterResume = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, start.Status);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), beforeBucket.CurrentSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), afterTwentyFiveSeconds.CurrentSimulatedDateTime);
        Assert.Equal(start.ActiveSessionId, afterTwentyFiveSeconds.ActiveSessionId);
        Assert.Equal(start.StartSimulatedDateTime, afterTwentyFiveSeconds.StartSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 0, 20, DateTimeKind.Utc), afterTwentyFiveSeconds.LastProgressionTimestamp);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Paused, paused.Status);
        Assert.Equal(afterTwentyFiveSeconds.CurrentSimulatedDateTime, stillPaused.CurrentSimulatedDateTime);
        Assert.Equal(afterTwentyFiveSeconds.LastProgressionTimestamp, stillPaused.LastProgressionTimestamp);
        Assert.Equal(start.ActiveSessionId, resumed.ActiveSessionId);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, resumed.Status);
        Assert.Equal(new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), afterResume.CurrentSimulatedDateTime);
        Assert.Equal(73, afterResume.Seed);
        Assert.True(afterResume.GenerationEnabled);
        Assert.Equal(start.StartSimulatedDateTime, afterResume.StartSimulatedDateTime);
        Assert.Equal("""{"profile":"replayable"}""", afterResume.DeterministicConfigurationJson);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 1, 15, DateTimeKind.Utc), afterResume.LastProgressionTimestamp);
    }

    [Fact]
    public async Task Progression_runner_is_idempotent_and_stopped_sessions_do_not_advance()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Runner Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var accessor = new RequestCompanyContextAccessor();
        var service = new CompanySimulationStateService(repository, timeProvider, accessor, NullLogger<CompanySimulationStateService>.Instance);
        var runner = new CompanySimulationProgressionRunner(dbContext, service, new CompanyExecutionScopeFactory(accessor));

        await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 21),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await runner.ProgressDueAsync(CancellationToken.None));
        Assert.Equal(0, await runner.ProgressDueAsync(CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, await runner.ProgressDueAsync(CancellationToken.None));

        var afterFirstRun = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        Assert.Equal(new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), afterFirstRun.CurrentSimulatedDateTime);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, afterFirstRun.Status);
        Assert.NotNull(afterFirstRun.ActiveSessionId);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await runner.ProgressDueAsync(CancellationToken.None));

        var afterFourthBucket = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        Assert.Equal(new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), afterFourthBucket.CurrentSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 0, 40, DateTimeKind.Utc), afterFourthBucket.LastProgressionTimestamp);

        await service.StopAsync(new StopCompanySimulationCommand(companyId), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, await runner.ProgressDueAsync(CancellationToken.None));

        var afterStop = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, afterStop.Status);
        Assert.Null(afterStop.ActiveSessionId);
        Assert.Equal(new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), afterStop.CurrentSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 0, 40, DateTimeKind.Utc), afterStop.LastProgressionTimestamp);
    }

    [Fact]
    public async Task Get_state_includes_recent_history_with_day_logs_for_operator_review()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "History Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        var started = await repository.StartAsync(
            new StartCompanySimulationStateCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                GenerationEnabled: true,
                Seed: 42,
                DeterministicConfigurationJson: """{"profile":"baseline"}""",
                TransitionedUtc: new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        await repository.RecordFinanceGenerationAsync(
            companyId,
            started.ActiveSessionId!.Value,
            new CompanySimulationFinanceGenerationResultDto(
                companyId,
                started.ActiveSessionId.Value,
                DaysProcessed: 1,
                InvoicesCreated: 1,
                BillsCreated: 1,
                TransactionsCreated: 2,
                BalancesCreated: 0,
                RecurringExpenseInstancesCreated: 1,
                AssetPurchasesCreated: 2,
                WorkflowTasksCreated: 0,
                ApprovalRequestsCreated: 0,
                AuditEventsCreated: 0,
                ActivityEventsCreated: 0,
                AlertsCreated: 1,
                DailyLogs:
                [
                    new CompanySimulationFinanceGenerationDayLogDto(
                        new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                        TransactionsCreated: 2,
                        InvoicesCreated: 1,
                        BillsCreated: 1,
                        AssetPurchasesCreated: 2,
                        RecurringExpenseInstancesCreated: 1,
                        AlertsCreated: 1,
                        InjectedAnomalies: ["duplicate_vendor_charge"],
                        Warnings: ["Manual review recommended."],
                        Errors: ["Synthetic generation failure was captured safely."])
                ]),
            new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        await repository.StopAsync(
            new StopCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 19, 12, 5, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var state = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var run = Assert.Single(state.RecentHistory!);
        var dayLog = Assert.Single(run.DayLogs);

        Assert.Equal(started.ActiveSessionId, run.SessionId);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, run.Status);
        Assert.Equal(42, run.Seed);
        Assert.Contains("duplicate_vendor_charge", run.InjectedAnomalies);
        Assert.Contains("Manual review recommended.", run.Warnings);
        Assert.Contains("Synthetic generation failure was captured safely.", run.Errors);
        Assert.Equal(2, dayLog.AssetPurchasesGenerated);
        Assert.Equal(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), dayLog.SimulatedDateUtc);
        Assert.Equal(7, dayLog.GeneratedRecordCount);
        Assert.Contains("duplicate_vendor_charge", dayLog.InjectedAnomalies);
        Assert.Contains("Manual review recommended.", dayLog.Warnings);
        Assert.Contains("Synthetic generation failure was captured safely.", dayLog.Errors);
    }

    [Fact]
    public async Task Disabled_state_still_returns_recent_history_for_operator_review()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Disabled History Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var started = await repository.StartAsync(
            new StartCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 17),
            CancellationToken.None);
        await repository.StopAsync(new StopCompanySimulationStateCommand(companyId, new DateTime(2026, 4, 19, 12, 2, 0, DateTimeKind.Utc)), CancellationToken.None);

        var gate = new TestSimulationFeatureGate(uiVisible: true, backendExecutionEnabled: false, backgroundJobsEnabled: false);
        var service = new CompanySimulationStateService(
            repository,
            timeProvider,
            companyContextAccessor: null,
            distributedLockProvider: null,
            financeGenerationPolicy: null,
            featureGate: gate,
            logger: NullLogger<CompanySimulationStateService>.Instance);

        var state = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        Assert.False(state.BackendExecutionEnabled);
        Assert.Equal("Simulation is disabled by configuration.", state.DisabledReason);
        Assert.Single(state.RecentHistory!);
        Assert.Equal(started.ActiveSessionId, state.RecentHistory[0].SessionId);
    }

    [Fact]
    public async Task Stop_and_restart_create_a_clean_session_without_carrying_elapsed_time_forward()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Lifecycle Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var service = new CompanySimulationStateService(repository, timeProvider, NullLogger<CompanySimulationStateService>.Instance);

        var started = await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 17, """{"profile":"baseline"}"""),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var beforeStop = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        var stopped = await service.StopAsync(new StopCompanySimulationCommand(companyId), CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(70));
        var stillStopped = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        var restarted = await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), false, 18, """{"profile":"restart"}"""),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var afterRestart = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        Assert.Equal(new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), beforeStop.CurrentSimulatedDateTime);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, stopped.Status);
        Assert.Null(stopped.ActiveSessionId);
        Assert.Equal(beforeStop.CurrentSimulatedDateTime, stillStopped.CurrentSimulatedDateTime);
        Assert.Equal(beforeStop.LastProgressionTimestamp, stillStopped.LastProgressionTimestamp);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, restarted.Status);
        Assert.NotNull(restarted.ActiveSessionId);
        Assert.NotEqual(started.ActiveSessionId, restarted.ActiveSessionId);
        Assert.Equal(new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), restarted.StartSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), afterRestart.CurrentSimulatedDateTime);
        Assert.False(afterRestart.GenerationEnabled);
        Assert.Equal(18, afterRestart.Seed);
        Assert.Equal(new DateTime(2026, 4, 19, 12, 1, 40, DateTimeKind.Utc), afterRestart.LastProgressionTimestamp);
    }

    [Fact]
    public async Task Progression_runner_advances_each_running_company_independently()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.AddRange(
            new Company(companyAId, "Company A"),
            new Company(companyBId, "Company B"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var accessor = new RequestCompanyContextAccessor();
        var service = new CompanySimulationStateService(repository, timeProvider, accessor, NullLogger<CompanySimulationStateService>.Instance);
        var runner = new CompanySimulationProgressionRunner(
            dbContext,
            service,
            new CompanyExecutionScopeFactory(accessor));

        await service.StartAsync(
            new StartCompanySimulationCommand(companyAId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 1),
            CancellationToken.None);
        await service.StartAsync(
            new StartCompanySimulationCommand(companyBId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), false, 2),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(21));
        var progressedCompanies = await runner.ProgressDueAsync(CancellationToken.None);

        var companyAState = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyAId), CancellationToken.None);
        var companyBState = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyBId), CancellationToken.None);

        Assert.Equal(2, progressedCompanies);
        Assert.Equal(new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), companyAState.CurrentSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc), companyBState.CurrentSimulatedDateTime);
        Assert.True(companyAState.GenerationEnabled);
        Assert.False(companyBState.GenerationEnabled);
    }

    [Fact]
    public async Task Backend_disabled_returns_safe_state_and_prevents_start()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Disabled Simulation Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var gate = new TestSimulationFeatureGate(uiVisible: true, backendExecutionEnabled: false, backgroundJobsEnabled: true);
        var service = new CompanySimulationStateService(
            repository,
            timeProvider,
            companyContextAccessor: null,
            distributedLockProvider: null,
            financeGenerationPolicy: null,
            featureGate: gate,
            logger: NullLogger<CompanySimulationStateService>.Instance);

        var state = await service.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);
        Assert.False(state.BackendExecutionEnabled);
        Assert.False(state.CanStart);

        await Assert.ThrowsAsync<SimulationBackendDisabledException>(() => service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 73),
            CancellationToken.None));
    }

    [Fact]
    public async Task Progression_runner_skips_when_background_jobs_are_disabled()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Runner Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var accessor = new RequestCompanyContextAccessor();
        var gate = new TestSimulationFeatureGate(uiVisible: true, backendExecutionEnabled: true, backgroundJobsEnabled: false);
        var service = new CompanySimulationStateService(repository, timeProvider, accessor, NullLogger<CompanySimulationStateService>.Instance);
        var runner = new CompanySimulationProgressionRunner(
            dbContext,
            service,
            new CompanyExecutionScopeFactory(accessor),
            Microsoft.Extensions.Options.Options.Create(new CompanySimulationProgressionWorkerOptions()),
            NullLogger<CompanySimulationProgressionRunner>.Instance,
            gate);

        await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 21),
            CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, await runner.ProgressDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Progression_runner_skips_when_backend_execution_is_disabled_even_if_background_jobs_remain_enabled()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Backend Disabled Runner Company"));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var accessor = new RequestCompanyContextAccessor();
        var gate = new TestSimulationFeatureGate(uiVisible: true, backendExecutionEnabled: true, backgroundJobsEnabled: true);
        var service = new CompanySimulationStateService(
            repository,
            timeProvider,
            companyContextAccessor: accessor,
            distributedLockProvider: null,
            financeGenerationPolicy: null,
            featureGate: gate,
            logger: NullLogger<CompanySimulationStateService>.Instance);
        var runner = new CompanySimulationProgressionRunner(
            dbContext,
            service,
            new CompanyExecutionScopeFactory(accessor),
            Microsoft.Extensions.Options.Options.Create(new CompanySimulationProgressionWorkerOptions()),
            NullLogger<CompanySimulationProgressionRunner>.Instance,
            gate);

        await service.StartAsync(
            new StartCompanySimulationCommand(companyId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 21),
            CancellationToken.None);

        gate.SetExecution(backendExecutionEnabled: false, backgroundJobsEnabled: true);
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, await runner.ProgressDueAsync(CancellationToken.None));
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), (await repository.GetCurrentAsync(companyId, CancellationToken.None))!.CurrentSimulatedUtc);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }

    private sealed class RecordingGenerationPolicy : IFinanceGenerationPolicy
    {
        public List<GenerateCompanySimulationFinanceCommand> Commands { get; } = [];

        public Task<CompanySimulationFinanceGenerationResultDto> GenerateAsync(
            GenerateCompanySimulationFinanceCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new CompanySimulationFinanceGenerationResultDto(
                command.CompanyId,
                command.ActiveSessionId,
                DaysProcessed: 1,
                InvoicesCreated: 0,
                BillsCreated: 0,
                TransactionsCreated: 0,
                BalancesCreated: 0,
                RecurringExpenseInstancesCreated: 0,
                AssetPurchasesCreated: 0,
                WorkflowTasksCreated: 0,
                ApprovalRequestsCreated: 0,
                AuditEventsCreated: 0,
                ActivityEventsCreated: 0,
                AlertsCreated: 0));
        }
    }

    private sealed class TestSimulationFeatureGate : ISimulationFeatureGate
    {
        private SimulationFeatureStateDto _state;

        public TestSimulationFeatureGate(bool uiVisible, bool backendExecutionEnabled, bool backgroundJobsEnabled)
        {
            _state = new SimulationFeatureStateDto(uiVisible, backendExecutionEnabled, backgroundJobsEnabled, "Simulation is disabled by configuration.");
        }

        public void SetExecution(bool backendExecutionEnabled, bool backgroundJobsEnabled) => _state = _state with { BackendExecutionEnabled = backendExecutionEnabled, BackgroundJobsEnabled = backgroundJobsEnabled };

        public SimulationFeatureStateDto GetState() => _state;

        public bool IsUiVisible() => _state.UiVisible;

        public bool IsBackendExecutionEnabled() => _state.BackendExecutionEnabled;

        public bool AreBackgroundJobsEnabled() => _state.BackgroundJobsEnabled;

        public bool IsBackgroundExecutionAllowed() =>
            _state.BackendExecutionEnabled &&
            _state.BackgroundJobsEnabled;

        public bool IsFullyDisabled() => !_state.UiVisible && !_state.BackendExecutionEnabled && !_state.BackgroundJobsEnabled;

        public void EnsureBackendExecutionEnabled()
        {
            if (!_state.BackendExecutionEnabled)
            {
                throw new SimulationBackendDisabledException(_state.DisabledMessage);
            }
        }

        public void EnsureBackgroundExecutionEnabled()
        {
            if (!IsBackgroundExecutionAllowed())
            {
                throw new SimulationBackendDisabledException(_state.DisabledMessage, isBackgroundExecution: true);
            }
        }
    }
}