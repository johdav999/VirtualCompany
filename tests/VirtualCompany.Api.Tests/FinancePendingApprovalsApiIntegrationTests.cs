using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePendingApprovalsApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePendingApprovalsApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Pending_endpoint_returns_only_current_tenant_pending_and_escalated_tasks()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var response = await client.GetAsync("/api/finance/approvals/pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<PendingApprovalResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
        Assert.Contains(payload, x => x.TargetType == "bill" && x.TargetId == seed.PendingBillTargetId && x.Status == "pending");
        Assert.Contains(payload, x => x.TargetType == "exception" && x.TargetId == seed.EscalatedExceptionTargetId && x.Status == "escalated");
        Assert.All(payload, x => Assert.NotEqual(seed.OtherCompanyTargetId, x.TargetId));
        Assert.All(payload, x => Assert.Contains(x.Status, new[] { "pending", "escalated" }));
        Assert.All(payload, x => Assert.False(string.IsNullOrWhiteSpace(x.TargetType)));
        Assert.Contains(payload, x => x.Assignee is not null && x.Assignee.DisplayName == "Finance Approval Owner");
    }

    private async Task<PendingApprovalSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var assigneeUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-pending-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Approval Owner";
        var pendingBillTargetId = Guid.NewGuid();
        var escalatedExceptionTargetId = Guid.NewGuid();
        var otherCompanyTargetId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, email, displayName, "dev-header", subject),
                new User(assigneeUserId, "approver@example.com", "Finance Approval Owner", "dev-header", "finance-pending-assignee"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Pending approvals company"),
                new Company(otherCompanyId, "Other approvals company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, assigneeUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));

            dbContext.ApprovalTasks.AddRange(
                new ApprovalTask(
                    Guid.NewGuid(),
                    companyId,
                    ApprovalTargetType.Bill,
                    pendingBillTargetId,
                    assigneeUserId,
                    new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Pending),
                new ApprovalTask(
                    Guid.NewGuid(),
                    companyId,
                    ApprovalTargetType.Exception,
                    escalatedExceptionTargetId,
                    assigneeUserId,
                    new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Escalated),
                new ApprovalTask(
                    Guid.NewGuid(),
                    companyId,
                    ApprovalTargetType.Payment,
                    Guid.NewGuid(),
                    assigneeUserId,
                    new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Approved),
                new ApprovalTask(
                    Guid.NewGuid(),
                    companyId,
                    ApprovalTargetType.Payment,
                    Guid.NewGuid(),
                    assigneeUserId,
                    new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Rejected),
                new ApprovalTask(
                    Guid.NewGuid(),
                    otherCompanyId,
                    ApprovalTargetType.Bill,
                    otherCompanyTargetId,
                    assigneeUserId,
                    new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc),
                    ApprovalTaskStatus.Pending));

            return Task.CompletedTask;
        });

        return new PendingApprovalSeed(companyId, subject, email, displayName, pendingBillTargetId, escalatedExceptionTargetId, otherCompanyTargetId);
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

    private sealed record PendingApprovalSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid PendingBillTargetId,
        Guid EscalatedExceptionTargetId,
        Guid OtherCompanyTargetId);

    private sealed class PendingApprovalResponse
    {
        public Guid Id { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public Guid TargetId { get; set; }
        public AssigneeResponse? Assignee { get; set; }
        public DateTime? DueDateUtc { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class AssigneeResponse
    {
        public Guid? UserId { get; set; }
        public string? DisplayName { get; set; }
    }
}
