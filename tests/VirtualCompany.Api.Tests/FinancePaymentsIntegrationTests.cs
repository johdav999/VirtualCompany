using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePaymentsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Payment_endpoints_create_list_both_directions_and_isolate_records_by_company()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments",
            new CreateFinancePaymentRequest
            {
                PaymentType = "incoming",
                Amount = 1825.50m,
                Currency = "usd",
                PaymentDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                Method = "bank-transfer",
                Status = "completed",
                CounterpartyReference = "ACME RECEIPT-204"
            });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var incomingPayment = await createResponse.Content.ReadFromJsonAsync<FinancePaymentResponse>();
        Assert.NotNull(incomingPayment);
        Assert.Equal(seed.CompanyId, incomingPayment!.CompanyId);
        Assert.Equal("incoming", incomingPayment.PaymentType);
        Assert.Equal("USD", incomingPayment.Currency);
        Assert.Equal("bank_transfer", incomingPayment.Method);

        var outgoingCreateResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments",
            new CreateFinancePaymentRequest
            {
                PaymentType = "outgoing",
                Amount = 640.10m,
                Currency = "eur",
                PaymentDate = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc),
                Method = "ach",
                Status = "pending",
                CounterpartyReference = "VENDOR-PAYOUT-12"
            });

        Assert.Equal(HttpStatusCode.OK, outgoingCreateResponse.StatusCode);
        var outgoingPayment = await outgoingCreateResponse.Content.ReadFromJsonAsync<FinancePaymentResponse>();
        Assert.NotNull(outgoingPayment);
        Assert.Equal("outgoing", outgoingPayment!.PaymentType);
        Assert.Equal("EUR", outgoingPayment.Currency);

        var listResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/payments?limit=50");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var payments = await listResponse.Content.ReadFromJsonAsync<List<FinancePaymentResponse>>();
        Assert.NotNull(payments);
        Assert.Contains(payments!, payment => payment.Id == incomingPayment.Id);
        Assert.Contains(payments!, payment => payment.Id == outgoingPayment.Id);
        Assert.Contains(payments!, payment => payment.PaymentType == "incoming");
        Assert.Contains(payments!, payment => payment.PaymentType == "outgoing");

        var outgoingOnlyResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/payments?type=outgoing&limit=50");
        Assert.Equal(HttpStatusCode.OK, outgoingOnlyResponse.StatusCode);
        var outgoingPayments = await outgoingOnlyResponse.Content.ReadFromJsonAsync<List<FinancePaymentResponse>>();
        Assert.NotNull(outgoingPayments);
        Assert.Contains(outgoingPayments!, payment => payment.Id == outgoingPayment.Id);
        Assert.All(outgoingPayments!, payment => Assert.Equal("outgoing", payment.PaymentType));

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/payments/{outgoingPayment.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<FinancePaymentResponse>();
        Assert.NotNull(detail);
        Assert.Equal(outgoingPayment.Id, detail!.Id);
        Assert.Equal(outgoingPayment.CounterpartyReference, detail.CounterpartyReference);

        var crossTenantResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/payments/{seed.OtherCompanyPaymentId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Payment_create_endpoint_rejects_invalid_amount_and_type()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/payments",
            new CreateFinancePaymentRequest
            {
                PaymentType = "sideways",
                Amount = 0m,
                Currency = "US1",
                PaymentDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                Method = "bank_transfer",
                Status = "completed",
                CounterpartyReference = "Invalid payment"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateFinancePaymentRequest.PaymentType), problem!.Errors.Keys);
        Assert.Contains(nameof(CreateFinancePaymentRequest.Amount), problem.Errors.Keys);
        Assert.Contains(nameof(CreateFinancePaymentRequest.Currency), problem.Errors.Keys);
    }

    private async Task<PaymentEndpointSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-payments-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Payments Owner";
        var otherCompanyPaymentId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Payments Company"),
                new Company(otherCompanyId, "Other Payments Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);

            var otherCompanyPayment = new Payment(
                Guid.NewGuid(),
                otherCompanyId,
                PaymentTypes.Outgoing,
                945.20m,
                "USD",
                new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                "ach",
                "completed",
                "OTHER-CO-PAYMENT");
            dbContext.Payments.Add(otherCompanyPayment);
            otherCompanyPaymentId = otherCompanyPayment.Id;

            return Task.CompletedTask;
        });

        return new PaymentEndpointSeed(companyId, subject, email, displayName, otherCompanyPaymentId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record PaymentEndpointSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid OtherCompanyPaymentId);

    private sealed class FinancePaymentResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string PaymentType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CounterpartyReference { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<InsightResponse> AgentInsights { get; set; } = [];
    }

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

    private sealed class InsightResponse
    {
        public Guid Id { get; set; }
        public string ConditionKey { get; set; } = string.Empty;
    }
}