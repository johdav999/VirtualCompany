using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentInsightDetailEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceAgentInsightDetailEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Invoice_bill_and_payment_detail_endpoints_return_only_matching_agent_insights()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var invoiceResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.InvoiceId}");
        var billResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/bills/{seed.BillId}");
        var paymentResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/payments/{seed.PaymentId}");

        Assert.Equal(HttpStatusCode.OK, invoiceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, billResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);

        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        var bill = await billResponse.Content.ReadFromJsonAsync<BillDetailResponse>();
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentDetailResponse>();

        Assert.NotNull(invoice);
        Assert.NotNull(bill);
        Assert.NotNull(payment);

        Assert.Equal(new[] { seed.InvoiceInsightId, seed.InvoiceAliasInsightId }.OrderBy(x => x).ToArray(), invoice!.AgentInsights.Select(x => x.Id).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { seed.BillInsightId, seed.BillAliasInsightId }.OrderBy(x => x).ToArray(), bill!.AgentInsights.Select(x => x.Id).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { seed.PaymentInsightId, seed.PaymentAliasInsightId }.OrderBy(x => x).ToArray(), payment!.AgentInsights.Select(x => x.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Detail_endpoints_do_not_leak_other_tenant_agent_insights_even_when_entity_reference_matches()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.InvoiceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invoice = await response.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        Assert.NotNull(invoice);
        Assert.DoesNotContain(invoice!.AgentInsights, x => x.Id == seed.OtherCompanyInvoiceInsightId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeaderName, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeaderName, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeaderName, displayName);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.ProviderHeaderName, "dev-header");
        return client;
    }

    private async Task<SeedContext> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-detail-insights-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Detail Insight User";

        var invoiceInsightId = Guid.NewGuid();
        var billInsightId = Guid.NewGuid();
        var paymentInsightId = Guid.NewGuid();
        var invoiceAliasInsightId = Guid.NewGuid();
        var billAliasInsightId = Guid.NewGuid();
        var paymentAliasInsightId = Guid.NewGuid();
        var otherCompanyInvoiceInsightId = Guid.NewGuid();

        var invoiceId = Guid.Empty;
        var billId = Guid.Empty;
        var paymentId = Guid.Empty;

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Finance Detail Insight Company"),
                new Company(otherCompanyId, "Other Finance Detail Insight Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var financeSeed = FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            var otherFinanceSeed = FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);
            invoiceId = financeSeed.InvoiceIds[0];
            billId = financeSeed.BillIds[0];
            paymentId = financeSeed.PaymentIds[0];

            dbContext.FinanceAgentInsights.AddRange(
                new FinanceAgentInsight(
                    invoiceInsightId,
                    companyId,
                    FinancialCheckDefinitions.TransactionAnomaly.Code,
                    "invoice:detail-review",
                    "invoice",
                    invoiceId.ToString("D"),
                    FinancialCheckSeverity.High,
                    "Invoice detail needs finance review.",
                    "Open the invoice and confirm the exception.",
                    0.87m,
                    "Invoice detail",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("invoice", invoiceId.ToString("D"), "Invoice detail", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 8, 5, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    billInsightId,
                    companyId,
                    FinancialCheckDefinitions.PayablesPressure.Code,
                    "bill:priority-payment",
                    "bill",
                    billId.ToString("D"),
                    FinancialCheckSeverity.Critical,
                    "Bill detail is overdue.",
                    "Prioritize the overdue bill.",
                    0.93m,
                    "Bill detail",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("bill", billId.ToString("D"), "Bill detail", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 5, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    paymentInsightId,
                    companyId,
                    FinancialCheckDefinitions.CashRisk.Code,
                    "payment:reconciliation",
                    "payment",
                    paymentId.ToString("D"),
                    FinancialCheckSeverity.Medium,
                    "Payment detail needs reconciliation review.",
                    "Review the linked payment allocations.",
                    0.79m,
                    "Payment detail",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("payment", paymentId.ToString("D"), "Payment detail", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 5, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    invoiceAliasInsightId,
                    companyId,
                    FinancialCheckDefinitions.TransactionAnomaly.Code,
                    "invoice:detail-review-prefixed",
                    "finance_invoice",
                    invoiceId.ToString("D"),
                    FinancialCheckSeverity.Medium,
                    "Invoice detail still needs a second review.",
                    "Keep the invoice in the review queue.",
                    0.73m,
                    "Invoice detail alias",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("finance_invoice", invoiceId.ToString("D"), "Invoice detail alias", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 8, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 8, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 8, 35, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    billAliasInsightId,
                    companyId,
                    FinancialCheckDefinitions.PayablesPressure.Code,
                    "bill:priority-payment-prefixed",
                    "finance_bill",
                    billId.ToString("D"),
                    FinancialCheckSeverity.High,
                    "Bill alias insight should still appear on the selected bill.",
                    "Confirm the supplier payment plan.",
                    0.84m,
                    "Bill detail alias",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("finance_bill", billId.ToString("D"), "Bill detail alias", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 9, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 40, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    paymentAliasInsightId,
                    companyId,
                    FinancialCheckDefinitions.CashRisk.Code,
                    "payment:reconciliation-prefixed",
                    "finance_payment",
                    paymentId.ToString("D"),
                    FinancialCheckSeverity.Low,
                    "Payment alias insight should still surface on the selected payment.",
                    "Confirm whether the payment needs follow-up.",
                    0.66m,
                    "Payment detail alias",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("finance_payment", paymentId.ToString("D"), "Payment detail alias", true)
                    }),
                    "{\"source\":\"detail-test\"}",
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 16, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    Guid.NewGuid(),
                    companyId,
                    FinancialCheckDefinitions.CashRisk.Code,
                    "payment:other-detail",
                    "payment",
                    financeSeed.PaymentIds[1].ToString("D"),
                    FinancialCheckSeverity.Low,
                    "Different payment insight should not be included.",
                    "Ignore for the selected payment.",
                    0.62m,
                    "Other payment",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("payment", financeSeed.PaymentIds[1].ToString("D"), "Other payment", true)
                    }),
                    null,
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 10, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 5, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    Guid.NewGuid(),
                    companyId,
                    FinancialCheckDefinitions.CashRisk.Code,
                    "payment:other-detail",
                    "payment",
                    financeSeed.PaymentIds[1].ToString("D"),
                    FinancialCheckSeverity.Low,
                    "Different payment insight should not be included.",
                    "Ignore for the selected payment.",
                    0.62m,
                    "Other payment",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("payment", financeSeed.PaymentIds[1].ToString("D"), "Other payment", true)
                    }),
                    null,
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 10, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 10, 31, 0, DateTimeKind.Utc)),
                new FinanceAgentInsight(
                    otherCompanyInvoiceInsightId,
                    otherCompanyId,
                    FinancialCheckDefinitions.TransactionAnomaly.Code,
                    "invoice:detail-review",
                    "invoice",
                    invoiceId.ToString("D"),
                    FinancialCheckSeverity.Critical,
                    "Other tenant insight with matching entity id should never leak.",
                    "Tenant isolation must hold.",
                    0.99m,
                    "Other tenant invoice",
                    JsonSerializer.Serialize(new[]
                    {
                        new FinanceInsightEntityReferenceDto("invoice", invoiceId.ToString("D"), "Other tenant invoice", true),
                        new FinanceInsightEntityReferenceDto("invoice", otherFinanceSeed.InvoiceIds[0].ToString("D"), "Other invoice", false)
                    }),
                    null,
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 11, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 11, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 11, 1, 0, DateTimeKind.Utc)));

            await dbContext.SaveChangesAsync();
        });

        return new SeedContext(
            companyId,
            subject,
            email,
            displayName,
            invoiceId,
            billId,
            paymentId,
            invoiceAliasInsightId,
            billAliasInsightId,
            paymentAliasInsightId,
            invoiceInsightId,
            billInsightId,
            paymentInsightId,
            otherCompanyInvoiceInsightId);
    }

    private sealed record SeedContext(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid InvoiceId,
        Guid BillId,
        Guid PaymentId,
        Guid InvoiceAliasInsightId,
        Guid BillAliasInsightId,
        Guid PaymentAliasInsightId,
        Guid InvoiceInsightId,
        Guid BillInsightId,
        Guid PaymentInsightId,
        Guid OtherCompanyInvoiceInsightId);

    private sealed class InvoiceDetailResponse
    {
        public Guid Id { get; set; }
        public List<InsightResponse> AgentInsights { get; set; } = [];
    }

    private sealed class BillDetailResponse
    {
        public Guid Id { get; set; }
        public List<InsightResponse> AgentInsights { get; set; } = [];
    }

    private sealed class PaymentDetailResponse
    {
        public Guid Id { get; set; }
        public List<InsightResponse> AgentInsights { get; set; } = [];
    }

    private sealed class InsightResponse
    {
        public Guid Id { get; set; }
        public string ConditionKey { get; set; } = string.Empty;
    }
}