using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApprovalActionsApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceApprovalActionsApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Approve_endpoint_updates_pending_task_status()
    {
        var seed = await SeedAsync(ApprovalTaskStatus.Pending);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.PostAsJsonAsync($"/api/finance/approvals/{seed.TaskId}/approve", new { comment = "Approved for payment." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovalTaskResponse>();
        Assert.NotNull(payload);
        Assert.Equal("approved", payload!.Status);

        await AssertPersistedStatusAsync(seed.TaskId, ApprovalTaskStatus.Approved);
    }

    [Fact]
    public async Task Reject_endpoint_updates_escalated_task_status()
    {
        var seed = await SeedAsync(ApprovalTaskStatus.Escalated);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.PostAsJsonAsync($"/api/finance/approvals/{seed.TaskId}/reject", new { comment = "Rejected after review." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovalTaskResponse>();
        Assert.NotNull(payload);
        Assert.Equal("rejected", payload!.Status);

        await AssertPersistedStatusAsync(seed.TaskId, ApprovalTaskStatus.Rejected);
    }

    [Fact]
    public async Task Escalate_endpoint_updates_pending_task_status()
    {
        var seed = await SeedAsync(ApprovalTaskStatus.Pending);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.PostAsJsonAsync($"/api/finance/approvals/{seed.TaskId}/escalate", new { comment = "Escalated to finance leadership." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovalTaskResponse>();
        Assert.NotNull(payload);
        Assert.Equal("escalated", payload!.Status);

        await AssertPersistedStatusAsync(seed.TaskId, ApprovalTaskStatus.Escalated);
    }

    [Fact]
    public async Task Action_endpoints_do_not_expose_cross_tenant_tasks()
    {
        var seed = await SeedAsync(ApprovalTaskStatus.Pending, includeOtherCompanyTask: true);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.PostAsJsonAsync($"/api/finance/approvals/{seed.OtherCompanyTaskId}/approve", new { comment = "Should not resolve." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertPersistedStatusAsync(seed.OtherCompanyTaskId, ApprovalTaskStatus.Pending);
    }

    [Fact]
    public async Task Action_endpoints_return_conflict_for_terminal_tasks()
    {
        var seed = await SeedAsync(ApprovalTaskStatus.Approved);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.PostAsJsonAsync($"/api/finance/approvals/{seed.TaskId}/reject", new { comment = "Too late." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertPersistedStatusAsync(seed.TaskId, ApprovalTaskStatus.Approved);
    }

    private async Task AssertPersistedStatusAsync(Guid taskId, ApprovalTaskStatus expectedStatus)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var status = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == taskId)
            .Select(x => x.Status)
            .SingleAsync();

        Assert.Equal(expectedStatus, status);
    }

    private async Task<ApprovalActionSeed> SeedAsync(
        ApprovalTaskStatus status,
        bool includeOtherCompanyTask = false)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var otherCompanyTaskId = Guid.NewGuid();
        var subject = $"finance-approval-actions-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Approval Reviewer";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Finance approval action company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.FinanceApprover,
                CompanyMembershipStatus.Active));

            dbContext.ApprovalTasks.Add(new ApprovalTask(
                taskId,
                companyId,
                ApprovalTargetType.Bill,
                Guid.NewGuid(),
                userId,
                new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
                status));

            if (includeOtherCompanyTask)
            {
                dbContext.Companies.Add(new Company(otherCompanyId, "Other finance approval action company"));
                dbContext.ApprovalTasks.Add(new ApprovalTask(
                    otherCompanyTaskId,
                    otherCompanyId,
                    ApprovalTargetType.Payment,
                    Guid.NewGuid(),
                    userId,
                    new DateTime(2026, 4, 29, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Pending));
            }

            return Task.CompletedTask;
        });

        return new ApprovalActionSeed(
            companyId,
            taskId,
            includeOtherCompanyTask ? otherCompanyTaskId : null,
            subject,
            email,
            displayName);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyId.ToString());
        return client;
    }

    private sealed record ApprovalActionSeed(
        Guid CompanyId,
        Guid TaskId,
        Guid? OtherCompanyTaskId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed class ApprovalTaskResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
    }
}