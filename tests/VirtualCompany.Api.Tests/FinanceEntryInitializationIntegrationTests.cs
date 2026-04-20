using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceEntryInitializationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceEntryInitializationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task First_finance_entry_status_for_not_seeded_company_returns_not_seeded_without_enqueuing_a_job()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Initializing, payload!.InitializationStatus);
        Assert.Equal(FinanceEntryProgressStateContractValues.NotSeeded, payload.ProgressState);
        Assert.Equal(FinanceSeedingStateContractValues.NotSeeded, payload.SeedingState);
        Assert.False(payload.SeedJobEnqueued);
        Assert.False(payload.SeedJobActive);
        Assert.False(payload.CanRetry);
        Assert.Equal(FinanceRecommendedActionContractValues.Generate, payload.RecommendedAction);
        Assert.Equal([FinanceManualSeedModes.Replace], payload.SupportedModes);

        var jobCount = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.BackgroundExecutions.Count(x =>
                x.CompanyId == seed.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.FinanceSeed)));

        Assert.Equal(0, jobCount);
    }

    [Fact]
    public async Task Request_finance_entry_initialization_for_not_seeded_company_enqueues_one_job_and_returns_requested_state()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state/request", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Initializing, payload!.InitializationStatus);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, payload.ProgressState);
        Assert.Equal(FinanceSeedingStateContractValues.Seeding, payload.SeedingState);
        Assert.True(payload.SeedJobEnqueued);
        Assert.True(payload.SeedJobActive);
        Assert.False(payload.CanRetry);

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var company = dbContext.Companies.Single(x => x.Id == seed.CompanyId);
            var jobs = dbContext.BackgroundExecutions
                .Where(x => x.CompanyId == seed.CompanyId && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
                .ToList();
            var audits = dbContext.AuditEvents
                .Where(x => x.CompanyId == seed.CompanyId && x.Action == "finance.seed.job.requested")
                .ToList();
            return new { Company = company, Jobs = jobs, Audits = audits };
        });

        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal(FinanceSeedingState.Seeding, snapshot.Company.FinanceSeedStatus);
        Assert.Equal(BackgroundExecutionStatus.Pending, job.Status);
        Assert.Equal(BackgroundExecutionRelatedEntityTypes.FinanceSeed, job.RelatedEntityType);
        Assert.Single(snapshot.Audits);

        var telemetryEvent = Assert.Single(_factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));
        Assert.Equal(FinanceEntrySources.FinanceEntry, telemetryEvent.Context.TriggerSource);
        Assert.Equal(FinanceSeedingState.NotSeeded, telemetryEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Seeding, telemetryEvent.Context.SeedStateAfter);
        Assert.Equal(job.Id, telemetryEvent.Context.JobId);
        Assert.Equal(job.CorrelationId, telemetryEvent.Context.CorrelationId);
        Assert.Equal(job.IdempotencyKey, telemetryEvent.Context.IdempotencyKey);
        Assert.False(telemetryEvent.Context.JobAlreadyRunning);
        Assert.NotNull(telemetryEvent.Context.UserId);
    }

    [Fact]
    public async Task Repeated_finance_entry_request_does_not_enqueue_duplicate_seed_jobs_while_a_job_is_active()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var firstResponse = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state/request", new { });
        var secondResponse = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state/request", new { });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondPayload = await secondResponse.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(secondPayload);
        Assert.False(secondPayload!.SeedJobEnqueued);
        Assert.True(secondPayload.SeedJobActive);

        var jobCount = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.BackgroundExecutions.Count(x =>
                x.CompanyId == seed.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.FinanceSeed)));

        Assert.Equal(1, jobCount);
        Assert.Equal(
            1,
            _factory.FinanceSeedTelemetry.Events.Count(x =>
                x.EventName == FinanceSeedTelemetryEventNames.Requested &&
                x.Context.CompanyId == seed.CompanyId));
    }

    [Fact]
    public async Task Concurrent_finance_entry_requests_enqueue_a_single_seed_job_for_the_company()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state/request", new { }))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(new
            {
                JobCount = dbContext.BackgroundExecutions.Count(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.ExecutionType == BackgroundExecutionType.FinanceSeed),
                RequestedAuditCount = dbContext.AuditEvents.Count(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Action == "finance.seed.job.requested"),
                CompanyState = dbContext.Companies.Single(x => x.Id == seed.CompanyId).FinanceSeedStatus
            }));

        Assert.Equal(1, snapshot.JobCount);
        Assert.Equal(1, snapshot.RequestedAuditCount);
        Assert.Equal(FinanceSeedingState.Seeding, snapshot.CompanyState);
        Assert.Equal(
            1,
            _factory.FinanceSeedTelemetry.Events.Count(x =>
                x.EventName == FinanceSeedTelemetryEventNames.Requested &&
                x.Context.CompanyId == seed.CompanyId));
    }

    [Fact]
    public async Task Failed_finance_seed_job_returns_recoverable_error_state()
    {
        var seed = await SeedCompanyAsync(configureCompany: (dbContext, companyId) =>
        {
            var execution = new BackgroundExecution(
                Guid.NewGuid(),
                companyId,
                BackgroundExecutionType.FinanceSeed,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                companyId.ToString("D"),
                "finance-seed-failed",
                $"finance-seed:{companyId:N}",
                maxAttempts: 1);
            execution.StartAttempt("finance-seed-failed", 1, 1);
            execution.MarkFailed(
                BackgroundExecutionFailureCategory.TransientInfrastructure,
                "timeout",
                "The finance seed worker timed out.");
            dbContext.BackgroundExecutions.Add(execution);
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Failed, payload!.InitializationStatus);
        Assert.True(payload.CanRetry);
        Assert.Equal("timeout", payload.LastErrorCode);
        Assert.Equal(FinanceEntryProgressStateContractValues.Failed, payload.ProgressState);
        Assert.Contains("timed out", payload.Message, StringComparison.OrdinalIgnoreCase);

        var retryResponse = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state/retry", new { });
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var retryPayload = await retryResponse.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(retryPayload);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, retryPayload!.ProgressState);
        Assert.True(retryPayload.SeedJobEnqueued);

        var executionState = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.BackgroundExecutions.Single(x =>
                x.CompanyId == seed.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.FinanceSeed)));

        Assert.Equal(BackgroundExecutionStatus.Pending, executionState.Status);
        Assert.Equal(0, executionState.AttemptCount);
    }

    [Fact]
    public async Task Manual_finance_seed_request_requeues_seeded_company_in_replace_mode()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedCompanyAsync(configureCompany: (dbContext, companyId) =>
        {
            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/manual-seed",
            new FinanceManualSeedRequest
            {
                Mode = FinanceManualSeedModes.Replace,
                ConfirmReplace = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Initializing, payload!.InitializationStatus);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, payload.ProgressState);
        Assert.Equal(FinanceSeedingStateContractValues.Seeding, payload.SeedingState);
        Assert.True(payload.SeedJobEnqueued);
        Assert.True(payload.SeedJobActive);
        Assert.True(payload.DataAlreadyExists);
        Assert.Equal(FinanceManualSeedModes.Replace, payload.SeedMode);
        Assert.Equal(FinanceRecommendedActionContractValues.Regenerate, payload.RecommendedAction);
        Assert.Equal([FinanceManualSeedModes.Replace], payload.SupportedModes);
        Assert.Equal(FinanceSeedOperationContractValues.Started, payload.SeedOperation);
        Assert.False(payload.ConfirmationRequired);

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var company = dbContext.Companies.Single(x => x.Id == seed.CompanyId);
            var jobs = dbContext.BackgroundExecutions
                .Where(x => x.CompanyId == seed.CompanyId && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
                .ToList();
            var audit = dbContext.AuditEvents.Single(x =>
                x.CompanyId == seed.CompanyId &&
                x.Action == "finance.seed.job.requested");
            return new { Company = company, Jobs = jobs, Audit = audit };
        });

        var job = Assert.Single(snapshot.Jobs);
        Assert.Equal(FinanceSeedingState.Seeding, snapshot.Company.FinanceSeedStatus);
        Assert.Equal(BackgroundExecutionStatus.Pending, job.Status);
        Assert.Equal(AuditActorTypes.User, snapshot.Audit.ActorType);
        Assert.NotNull(snapshot.Audit.ActorId);

        var telemetryEvent = Assert.Single(_factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));
        Assert.Equal(FinanceEntrySources.ManualSeed, telemetryEvent.Context.TriggerSource);
        Assert.Equal(job.Id, telemetryEvent.Context.JobId);
        Assert.Equal(AuditActorTypes.User, telemetryEvent.Context.ActorType);
    }

    [Fact]
    public async Task Manual_finance_seed_replace_requires_explicit_confirmation_when_data_exists()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedCompanyAsync(configureCompany: (dbContext, companyId) =>
        {
            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/manual-seed",
            new FinanceManualSeedRequest
            {
                Mode = FinanceManualSeedModes.Replace
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Initializing, payload!.InitializationStatus);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, payload.ProgressState);
        Assert.Equal(FinanceSeedingStateContractValues.Seeding, payload.SeedingState);
        Assert.True(payload.DataAlreadyExists);
        Assert.Equal(FinanceManualSeedModes.Replace, payload.SeedMode);
        Assert.Equal(FinanceSeedOperationContractValues.Rejected, payload.SeedOperation);
        Assert.True(payload.ConfirmationRequired);
        Assert.True(payload.SeedJobEnqueued);
        Assert.True(payload.SeedJobActive);

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var company = dbContext.Companies.Single(x => x.Id == seed.CompanyId);
            var jobs = dbContext.BackgroundExecutions
                .Where(x => x.CompanyId == seed.CompanyId && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
                .ToList();
            return new { Company = company, Jobs = jobs };
        });

        Assert.Equal(FinanceSeedingState.Seeded, snapshot.Company.FinanceSeedStatus);
        Assert.Empty(snapshot.Jobs);

        Assert.Empty(_factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));

        var rejectedAuditCount = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.AuditEvents.Count(x =>
                x.CompanyId == seed.CompanyId &&
                x.Action == "finance.seed.job.rejected")));

        Assert.Equal(1, rejectedAuditCount);
    }

    [Fact]
    public async Task Seeded_finance_entry_returns_ready_state_without_enqueuing_a_job()
    {
        var seed = await SeedCompanyAsync(configureCompany: (dbContext, companyId) =>
        {
            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/entry-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceEntryInitializationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(FinanceEntryInitializationContractValues.Ready, payload!.InitializationStatus);
        Assert.Equal(FinanceEntryProgressStateContractValues.Seeded, payload.ProgressState);
        Assert.False(payload.SeedJobActive);
        Assert.Equal(FinanceRecommendedActionContractValues.Regenerate, payload.RecommendedAction);
        Assert.False(payload.SeedJobEnqueued);

        var jobCount = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.BackgroundExecutions.Count(x =>
                x.CompanyId == seed.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.FinanceSeed)));

        Assert.Equal(0, jobCount);
    }

    private async Task<FinanceEntrySeed> SeedCompanyAsync(Action<VirtualCompanyDbContext, Guid>? configureCompany = null)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var subject = $"finance-entry-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Entry User";

        await _factory.SeedAsync(dbContext =>
        {
            var company = new Company(companyId, "Finance Entry Company");
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(company);
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            configureCompany?.Invoke(dbContext, companyId);
            return Task.CompletedTask;
        });

        return new FinanceEntrySeed(companyId, subject, email, displayName);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record FinanceEntrySeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);
}