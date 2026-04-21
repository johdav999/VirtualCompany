using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentApprovalTaskIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePaymentApprovalTaskIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Payment_create_above_threshold_creates_pending_approval_task()
    {
        var seed = await SeedAsync(500m);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments",
            new CreateFinancePaymentRequest
            {
                PaymentType = "outgoing",
                Amount = 640.10m,
                Currency = "USD",
                PaymentDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                Method = "ach",
                Status = "pending",
                CounterpartyReference = "AP-640"
            });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var payment = await createResponse.Content.ReadFromJsonAsync<FinancePaymentResponse>();
        Assert.NotNull(payment);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var approvalTask = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == seed.CompanyId && x.TargetType == ApprovalTargetType.Payment && x.TargetId == payment!.Id);

        Assert.Equal(ApprovalTaskStatus.Pending, approvalTask.Status);
        Assert.Equal(seed.ApproverUserId, approvalTask.AssigneeId);
    }

    [Fact]
    public async Task Payment_create_below_threshold_does_not_create_approval_task()
    {
        var seed = await SeedAsync(5000m);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments",
            new CreateFinancePaymentRequest
            {
                PaymentType = "outgoing",
                Amount = 640.10m,
                Currency = "USD",
                PaymentDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                Method = "ach",
                Status = "pending",
                CounterpartyReference = "AP-640"
            });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Empty(await dbContext.ApprovalTasks.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId && x.TargetType == ApprovalTargetType.Payment).ToListAsync());
    }

    private async Task<PaymentApprovalSeed> SeedAsync(decimal billApprovalThreshold)
    {
        var userId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var subject = $"finance-payment-approval-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Payment Owner";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, email, displayName, "dev-header", subject),
                new User(approverUserId, "payment-approver@example.com", "Payment Approver", "dev-header", "payment-approver"));
            dbContext.Companies.Add(new Company(companyId, "Finance payment approval company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, approverUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
            dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), companyId, "USD", 10000m, billApprovalThreshold, true));
            return Task.CompletedTask;
        });

        return new PaymentApprovalSeed(companyId, subject, email, displayName, approverUserId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record PaymentApprovalSeed(Guid CompanyId, string Subject, string Email, string DisplayName, Guid ApproverUserId);

    private sealed class CreateFinancePaymentRequest
    {
        public string PaymentType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CounterpartyReference { get; set; } = string.Empty;
    }

    private sealed class FinancePaymentResponse
    {
        public Guid Id { get; set; }
    }
}
