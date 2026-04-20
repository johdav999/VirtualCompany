using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySimulationControllerIntegrationTests
{
    [Fact]
    public async Task Simulation_lifecycle_endpoints_preserve_progression_pause_resume_stop_and_restart_semantics()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var factory = new TestWebApplicationFactory(timeProvider);
        var seed = await SeedCompanyAsync(factory, "manager", "manager@example.com", "Manager", CompanyMembershipRole.Manager);
        using var client = CreateAuthenticatedClient(factory, seed, includeCompanyHeader: true);

        var startSimulatedUtc = new DateTime(2026, 4, 1, 8, 30, 0, DateTimeKind.Utc);
        var startResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation/start",
            new StartCompanySimulationRequest(
                startSimulatedUtc,
                GenerationEnabled: true,
                Seed: 42,
                DeterministicConfigurationJson: """{"profile":"baseline","speed":"1d/10s"}"""));

        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(started);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, started!.Status);
        Assert.Equal(startSimulatedUtc, started.CurrentSimulatedDateTime);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, started.LastProgressionTimestamp);
        Assert.True(started.GenerationEnabled);
        Assert.Equal(42, started.Seed);
        Assert.NotNull(started.ActiveSessionId);
        Assert.False(started.CanStart);
        Assert.True(started.CanPause);
        Assert.True(started.CanStop);
        Assert.False(started.CanToggleGeneration);
        Assert.False(started.SupportsStepForwardOneDay);
        Assert.True(started.SupportsRefresh);

        var firstSessionId = started.ActiveSessionId;

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var afterTenSeconds = await client.GetFromJsonAsync<CompanySimulationStateDto>(
            $"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(afterTenSeconds);
        Assert.Equal(startSimulatedUtc.AddDays(1), afterTenSeconds!.CurrentSimulatedDateTime);

        timeProvider.Advance(TimeSpan.FromSeconds(9));
        var beforeNextBucket = await client.GetFromJsonAsync<CompanySimulationStateDto>(
            $"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(beforeNextBucket);
        Assert.Equal(startSimulatedUtc.AddDays(1), beforeNextBucket!.CurrentSimulatedDateTime);

        var updateResponse = await client.PatchAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation",
            new UpdateCompanySimulationSettingsRequest(
                GenerationEnabled: false,
                DeterministicConfigurationJson: """{"profile":"paused-ready"}"""));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(updated);
        Assert.False(updated!.GenerationEnabled);
        Assert.Equal("""{"profile":"paused-ready"}""", updated.DeterministicConfigurationJson);

        var pauseResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/pause", content: null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        var paused = await pauseResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(paused);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Paused, paused!.Status);
        Assert.Equal(startSimulatedUtc.AddDays(1), paused.CurrentSimulatedDateTime);
        Assert.True(paused.CanStart);
        Assert.True(paused.CanToggleGeneration);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var stillPaused = await client.GetFromJsonAsync<CompanySimulationStateDto>(
            $"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(stillPaused);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Paused, stillPaused!.Status);
        Assert.Equal(startSimulatedUtc.AddDays(1), stillPaused.CurrentSimulatedDateTime);

        var stepForwardResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/step-forward", content: null);
        Assert.Equal(HttpStatusCode.OK, stepForwardResponse.StatusCode);
        var steppedForward = await stepForwardResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(steppedForward);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Paused, steppedForward!.Status);
        Assert.Equal(startSimulatedUtc.AddDays(2), steppedForward.CurrentSimulatedDateTime);
        Assert.True(steppedForward.GenerationEnabled);

        var resumeResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/resume", content: null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumed = await resumeResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(resumed);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, resumed!.Status);
        Assert.Equal(firstSessionId, resumed.ActiveSessionId);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, resumed.LastProgressionTimestamp);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var afterResume = await client.GetFromJsonAsync<CompanySimulationStateDto>(
            $"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(afterResume);
        Assert.Equal(startSimulatedUtc.AddDays(3), afterResume!.CurrentSimulatedDateTime);

        var stopResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/stop", content: null);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        var stopped = await stopResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(stopped);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, stopped!.Status);
        Assert.Null(stopped.ActiveSessionId);
        Assert.Equal(startSimulatedUtc.AddDays(3), stopped.CurrentSimulatedDateTime);
        Assert.True(stopped.CanStart);
        Assert.True(stopped.CanToggleGeneration);

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var stillStopped = await client.GetFromJsonAsync<CompanySimulationStateDto>(
            $"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(stillStopped);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Stopped, stillStopped!.Status);
        Assert.Equal(startSimulatedUtc.AddDays(3), stillStopped.CurrentSimulatedDateTime);

        var restartResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation/start",
            new StartCompanySimulationRequest(
                new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                GenerationEnabled: true,
                Seed: 42,
                DeterministicConfigurationJson: """{"profile":"baseline","speed":"1d/10s"}"""));

        Assert.Equal(HttpStatusCode.Created, restartResponse.StatusCode);
        var restarted = await restartResponse.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
        Assert.NotNull(restarted);
        Assert.Equal(CompanySimulationLifecycleStatusValues.Running, restarted!.Status);
        Assert.NotEqual(firstSessionId, restarted.ActiveSessionId);
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), restarted.CurrentSimulatedDateTime);
        Assert.Equal(42, restarted.Seed);
    }

    [Fact]
    public async Task Simulation_state_is_isolated_per_company()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var factory = new TestWebApplicationFactory(timeProvider);
        var userId = Guid.NewGuid();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "multi@example.com", "Multi Manager", "dev-header", "multi-manager"));
            dbContext.Companies.AddRange(
                new Company(companyAId, "Company A"),
                new Company(companyBId, "Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, new SeededCompany(userId, companyAId, "multi-manager", "multi@example.com", "Multi Manager"), includeCompanyHeader: false);

        client.DefaultRequestHeaders.Remove(CompanyContextResolutionMiddleware.CompanyHeaderName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyAId.ToString());
        await client.PostAsJsonAsync(
            $"/internal/companies/{companyAId}/simulation/start",
            new StartCompanySimulationRequest(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 1));

        client.DefaultRequestHeaders.Remove(CompanyContextResolutionMiddleware.CompanyHeaderName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyBId.ToString());
        await client.PostAsJsonAsync(
            $"/internal/companies/{companyBId}/simulation/start",
            new StartCompanySimulationRequest(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), false, 2));

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        client.DefaultRequestHeaders.Remove(CompanyContextResolutionMiddleware.CompanyHeaderName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyAId.ToString());
        var companyAState = await client.GetFromJsonAsync<CompanySimulationStateDto>($"/internal/companies/{companyAId}/simulation");

        client.DefaultRequestHeaders.Remove(CompanyContextResolutionMiddleware.CompanyHeaderName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyBId.ToString());
        var companyBState = await client.GetFromJsonAsync<CompanySimulationStateDto>($"/internal/companies/{companyBId}/simulation");

        Assert.NotNull(companyAState);
        Assert.NotNull(companyBState);
        Assert.Equal(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), companyAState!.CurrentSimulatedDateTime);
        Assert.Equal(new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc), companyBState!.CurrentSimulatedDateTime);
        Assert.True(companyAState.GenerationEnabled);
        Assert.False(companyBState.GenerationEnabled);
        Assert.NotEqual(companyAState.ActiveSessionId, companyBState.ActiveSessionId);
    }

    [Fact]
    public async Task Invalid_simulation_transitions_return_conflict()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var factory = new TestWebApplicationFactory(timeProvider);
        var seed = await SeedCompanyAsync(factory, "manager-invalid", "manager.invalid@example.com", "Manager Invalid", CompanyMembershipRole.Manager);
        using var client = CreateAuthenticatedClient(factory, seed, includeCompanyHeader: true);

        var resumeBeforeStart = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/resume", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resumeBeforeStart.StatusCode);

        await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation/start",
            new StartCompanySimulationRequest(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 7));

        var secondStart = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation/start",
            new StartCompanySimulationRequest(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), true, 7));
        Assert.Equal(HttpStatusCode.Conflict, secondStart.StatusCode);

        var toggleWhileRunning = await client.PatchAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation",
            new UpdateCompanySimulationSettingsRequest(GenerationEnabled: false));
        Assert.Equal(HttpStatusCode.Conflict, toggleWhileRunning.StatusCode);

        var resumeWhileRunning = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/resume", content: null);
        Assert.Equal(HttpStatusCode.Conflict, resumeWhileRunning.StatusCode);

        var pauseResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/pause", content: null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);

        var stepWhilePaused = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/step-forward", content: null);
        Assert.Equal(HttpStatusCode.OK, stepWhilePaused.StatusCode);

        var toggleWhilePaused = await client.PatchAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation",
            new UpdateCompanySimulationSettingsRequest(GenerationEnabled: false));
        Assert.Equal(HttpStatusCode.OK, toggleWhilePaused.StatusCode);

        var stopResponse = await client.PostAsync($"/internal/companies/{seed.CompanyId}/simulation/stop", content: null);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);

        var toggleWhileStopped = await client.PatchAsJsonAsync($"/internal/companies/{seed.CompanyId}/simulation", new UpdateCompanySimulationSettingsRequest(GenerationEnabled: true));
        Assert.Equal(HttpStatusCode.OK, toggleWhileStopped.StatusCode);
    }

    [Fact]
    public async Task Backend_disabled_returns_safe_state_and_blocks_mutations()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var factory = new TestWebApplicationFactory(
            timeProvider,
            new Dictionary<string, string?>
            {
                ["SimulationFeatures:UiVisible"] = "true",
                ["SimulationFeatures:BackendExecutionEnabled"] = "false",
                ["SimulationFeatures:BackgroundJobsEnabled"] = "true",
                ["SimulationFeatures:DisabledMessage"] = "Simulation is disabled by test configuration."
            });
        var seed = await SeedCompanyAsync(factory, "manager-disabled", "manager.disabled@example.com", "Manager Disabled", CompanyMembershipRole.Manager);
        using var client = CreateAuthenticatedClient(factory, seed, includeCompanyHeader: true);

        var state = await client.GetFromJsonAsync<CompanySimulationStateDto>($"/internal/companies/{seed.CompanyId}/simulation");
        Assert.NotNull(state);
        Assert.False(state!.BackendExecutionEnabled);
        Assert.False(state.CanStart);
        Assert.Equal("Simulation is disabled by test configuration.", state.DisabledReason);

        var startResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/simulation/start",
            new StartCompanySimulationRequest(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true, 7));

        Assert.Equal(HttpStatusCode.Conflict, startResponse.StatusCode);
    }

    [Fact]
    public async Task Disabled_execution_blocks_finance_simulation_manual_trigger_but_standard_finance_reads_still_work()
    {
        using var factory = new TestWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["SimulationFeatures:UiVisible"] = "true",
                ["SimulationFeatures:BackendExecutionEnabled"] = "false",
                ["SimulationFeatures:BackgroundJobsEnabled"] = "true",
                ["SimulationFeatures:DisabledMessage"] = "Simulation is disabled by test configuration."
            });
        var seed = await SeedCompanyAsync(factory, "owner-disabled", "owner.disabled@example.com", "Owner Disabled", CompanyMembershipRole.Owner);
        await factory.SeedAsync(dbContext =>
        {
            FinanceSeedData.AddMockFinanceData(dbContext, seed.CompanyId);
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, seed, includeCompanyHeader: true);

        var advanceResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/simulation/advance",
            new AdvanceCompanySimulationTimeRequest(
                TotalHours: 24,
                ExecutionStepHours: 24,
                Accelerated: true));

        Assert.Equal(HttpStatusCode.Conflict, advanceResponse.StatusCode);

        var cashBalanceResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-balance");
        Assert.Equal(HttpStatusCode.OK, cashBalanceResponse.StatusCode);

        var cashBalance = await cashBalanceResponse.Content.ReadFromJsonAsync<FinanceCashBalanceDto>();
        Assert.NotNull(cashBalance);
        Assert.Equal(seed.CompanyId, cashBalance!.CompanyId);
    }


    private static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory, SeededCompany seed, bool includeCompanyHeader)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);

        if (includeCompanyHeader)
        {
            client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString());
        }

        return client;
    }

    private static async Task<SeededCompany> SeedCompanyAsync(
        TestWebApplicationFactory factory,
        string subject,
        string email,
        string displayName,
        CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new SeededCompany(userId, companyId, subject, email, displayName);
    }

    private sealed record SeededCompany(
        Guid UserId,
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);

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
}