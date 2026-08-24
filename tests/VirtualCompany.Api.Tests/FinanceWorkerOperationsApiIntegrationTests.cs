using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceWorkerOperationsApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Recovery_surface_is_tenant_scoped_permission_guarded_and_audits_safe_actions()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);

        using var read = await owner.GetAsync(Route(seed.CompanyId));
        using var crossCompanyRead = await owner.GetAsync(Route(seed.UnownedCompanyId));
        using var unauthorizedStop = await employee.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.StopExecutionId:D}/stop",
            new { expectedVersion = seed.StopVersion, reason = "Employee should not be able to stop Finance work." });

        Assert.True(read.StatusCode == HttpStatusCode.OK,
            $"Expected Finance worker read to succeed, but received {(int)read.StatusCode}: {await read.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedStop.StatusCode);
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(seed.CompanyId, readJson.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(17, readJson.RootElement.GetProperty("workers").GetArrayLength());
        Assert.DoesNotContain(readJson.RootElement.GetProperty("workItems").EnumerateArray(),
            item => item.GetProperty("companyId").GetGuid() != seed.CompanyId);

        using var retried = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.RetryExecutionId:D}/retry",
            new { expectedVersion = seed.RetryVersion, reason = "Provider connectivity has recovered.", correlationId = "operator-retry" });
        using var stopped = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.StopExecutionId:D}/stop",
            new { expectedVersion = seed.StopVersion, reason = "The reporting period was reopened." });
        using var acknowledged = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.AcknowledgeExecutionId:D}/acknowledge",
            new { expectedVersion = seed.AcknowledgeVersion, reason = "The invalid request was removed at its source." });
        using var ambiguousRetry = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.AmbiguousExecutionId:D}/retry",
            new { expectedVersion = seed.AmbiguousVersion, reason = "Do not replay a possible provider success." });
        using var staleRetry = await owner.PostAsJsonAsync(
            $"{Route(seed.CompanyId)}/background-executions/{seed.RetryExecutionId:D}/retry",
            new { expectedVersion = seed.RetryVersion, reason = "This browser state is stale." });

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ambiguousRetry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleRetry.StatusCode);
        using var retriedJson = JsonDocument.Parse(await retried.Content.ReadAsStringAsync());
        using var stoppedJson = JsonDocument.Parse(await stopped.Content.ReadAsStringAsync());
        Assert.Equal("queued", retriedJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("stopped", stoppedJson.RootElement.GetProperty("status").GetString());

        var persisted = await _factory.ExecuteDbContextAsync(async db => new
        {
            Retry = await db.BackgroundExecutions.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.RetryExecutionId),
            Stop = await db.BackgroundExecutions.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.StopExecutionId),
            Acknowledge = await db.BackgroundExecutions.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.AcknowledgeExecutionId),
            Audits = await db.AuditEvents.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId &&
                (x.Action == AuditEventActions.FinanceWorkerRetryRequested ||
                 x.Action == AuditEventActions.FinanceWorkerStopped ||
                 x.Action == AuditEventActions.FinanceWorkerFailureAcknowledged)).Select(x => x.Action).ToListAsync()
        });

        Assert.Equal(BackgroundExecutionStatus.Pending, persisted.Retry.Status);
        Assert.Equal(0, persisted.Retry.AttemptCount);
        Assert.Equal(BackgroundExecutionStatus.Cancelled, persisted.Stop.Status);
        Assert.NotNull(persisted.Acknowledge.AcknowledgedUtc);
        Assert.Contains(AuditEventActions.FinanceWorkerRetryRequested, persisted.Audits);
        Assert.Contains(AuditEventActions.FinanceWorkerStopped, persisted.Audits);
        Assert.Contains(AuditEventActions.FinanceWorkerFailureAcknowledged, persisted.Audits);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string ownerSubject = "finance-worker-owner";
        const string ownerEmail = "finance-worker-owner@example.com";
        const string employeeSubject = "finance-worker-employee";
        const string employeeEmail = "finance-worker-employee@example.com";

        var retry = CreateFailed(companyId, BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionFailureCategory.ExternalDependencyTimeout, blocked: false);
        var stop = CreateExecution(companyId, BackgroundExecutionType.FinanceReportRegeneration);
        var acknowledge = CreateFailed(companyId, BackgroundExecutionType.FinanceInsightRefresh,
            BackgroundExecutionFailureCategory.PoisonPayload, blocked: false);
        var ambiguous = CreateFailed(companyId, BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionFailureCategory.AmbiguousExternalResult, blocked: true);
        var unowned = CreateFailed(unownedCompanyId, BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionFailureCategory.ExternalDependencyTimeout, blocked: false);

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Finance Worker Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Finance Worker Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "Finance worker company"),
                new Company(unownedCompanyId, "Unowned Finance worker company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.BackgroundExecutions.AddRange(retry, stop, acknowledge, ambiguous, unowned);
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownerSubject, ownerEmail, employeeSubject, employeeEmail,
            retry.Id, retry.Version, stop.Id, stop.Version, acknowledge.Id, acknowledge.Version, ambiguous.Id, ambiguous.Version);
    }

    private static BackgroundExecution CreateFailed(Guid companyId, BackgroundExecutionType type,
        BackgroundExecutionFailureCategory category, bool blocked)
    {
        var execution = CreateExecution(companyId, type);
        execution.StartAttempt(Guid.NewGuid().ToString("N"), 3, 3);
        if (blocked) execution.MarkBlocked(category, "safe_failure", "Operator-safe failure summary.");
        else execution.MarkFailed(category, "safe_failure", "Operator-safe failure summary.");
        return execution;
    }

    private static BackgroundExecution CreateExecution(Guid companyId, BackgroundExecutionType type) =>
        new(Guid.NewGuid(), companyId, type, BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 3);

    private static string Route(Guid companyId) => $"/api/companies/{companyId:D}/finance/worker-operations";

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, string OwnerSubject, string OwnerEmail,
        string EmployeeSubject, string EmployeeEmail, Guid RetryExecutionId, long RetryVersion,
        Guid StopExecutionId, long StopVersion, Guid AcknowledgeExecutionId, long AcknowledgeVersion,
        Guid AmbiguousExecutionId, long AmbiguousVersion);
}
