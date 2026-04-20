using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAggregateCalculationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceAggregateCalculationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Internal_finance_summary_endpoints_calculate_aggregates_from_underlying_records()
    {
        var seed = await SeedFinanceAggregateCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var asOfUtc = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
        var cashResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-balance?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);
        var cashBalance = await cashResponse.Content.ReadFromJsonAsync<CashBalanceResponse>();
        Assert.NotNull(cashBalance);
        Assert.Equal(12950m, cashBalance!.Amount);
        Assert.Equal("USD", cashBalance.Currency);

        var positiveMonthResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/profit-and-loss/monthly?year=2026&month=3");
        Assert.Equal(HttpStatusCode.OK, positiveMonthResponse.StatusCode);
        var positiveMonth = await positiveMonthResponse.Content.ReadFromJsonAsync<MonthlyProfitAndLossResponse>();
        Assert.NotNull(positiveMonth);
        Assert.Equal(9000m, positiveMonth!.Revenue);
        Assert.Equal(3100m, positiveMonth.Expenses);
        Assert.Equal(5900m, positiveMonth.NetResult);

        var negativeMonthResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/profit-and-loss/monthly?year=2026&month=4");
        Assert.Equal(HttpStatusCode.OK, negativeMonthResponse.StatusCode);
        var negativeMonth = await negativeMonthResponse.Content.ReadFromJsonAsync<MonthlyProfitAndLossResponse>();
        Assert.NotNull(negativeMonth);
        Assert.Equal(1000m, negativeMonth!.Revenue);
        Assert.Equal(2600m, negativeMonth.Expenses);
        Assert.Equal(-1600m, negativeMonth.NetResult);

        var startUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var breakdownResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/expense-breakdown?startUtc={Uri.EscapeDataString(startUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, breakdownResponse.StatusCode);
        var breakdown = await breakdownResponse.Content.ReadFromJsonAsync<ExpenseBreakdownResponse>();
        Assert.NotNull(breakdown);
        Assert.Equal(3100m, breakdown!.TotalExpenses);
        Assert.Collection(
            breakdown.Categories,
            category =>
            {
                Assert.Equal("cloud_hosting", category.Category);
                Assert.Equal(2000m, category.Amount);
            },
            category =>
            {
                Assert.Equal("travel", category.Category);
                Assert.Equal(1100m, category.Amount);
            });
    }

    [Fact]
    public async Task Internal_finance_read_endpoints_expose_typed_transactions_invoices_and_balances()
    {
        var seed = await SeedFinanceAggregateCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var transactionsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions?limit=10");
        Assert.Equal(HttpStatusCode.OK, transactionsResponse.StatusCode);
        var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>();
        Assert.NotNull(transactions);
        Assert.Equal(6, transactions!.Count);
        Assert.All(transactions, transaction =>
        {
            Assert.NotEqual(Guid.Empty, transaction.Id);
            Assert.Equal("Operating Cash", transaction.AccountName);
            Assert.Equal("USD", transaction.Currency);
        });

        var invoicesResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices?limit=10");
        Assert.Equal(HttpStatusCode.OK, invoicesResponse.StatusCode);
        var invoices = await invoicesResponse.Content.ReadFromJsonAsync<List<InvoiceResponse>>();
        Assert.NotNull(invoices);
        Assert.Equal(2, invoices!.Count);
        Assert.All(invoices, invoice =>
        {
            Assert.NotEqual(Guid.Empty, invoice.Id);
            Assert.Equal("Customer", invoice.CounterpartyName);
            Assert.Equal("USD", invoice.Currency);
        });

        var balancesResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/balances?asOfUtc={Uri.EscapeDataString(new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, balancesResponse.StatusCode);
        var balances = await balancesResponse.Content.ReadFromJsonAsync<List<AccountBalanceResponse>>();
        Assert.NotNull(balances);
        var operatingCash = Assert.Single(balances!, balance => balance.AccountName == "Operating Cash");
        Assert.Equal(12950m, operatingCash.Amount);
        Assert.Equal("USD", operatingCash.Currency);
    }

    private async Task<FinanceAggregateSeed> SeedFinanceAggregateCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var subject = $"finance-aggregate-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Aggregate Reader";

        await _factory.SeedAsync(dbContext =>
        {
            var accountId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var vendorId = Guid.NewGuid();
            var marchInvoiceId = Guid.NewGuid();
            var aprilInvoiceId = Guid.NewGuid();

            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Finance Aggregate Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.FinanceAccounts.Add(new FinanceAccount(
                accountId,
                companyId,
                "1000",
                "Operating Cash",
                "asset",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            dbContext.FinanceCounterparties.AddRange(
                new FinanceCounterparty(customerId, companyId, "Customer", "customer", "customer@example.com"),
                new FinanceCounterparty(vendorId, companyId, "Vendor", "vendor", "vendor@example.com"));
            dbContext.FinanceBalances.Add(new FinanceBalance(
                Guid.NewGuid(),
                companyId,
                accountId,
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                10000m,
                "USD"));
            dbContext.FinanceInvoices.AddRange(
                new FinanceInvoice(
                    marchInvoiceId,
                    companyId,
                    customerId,
                    "INV-202603-001",
                    new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                    9000m,
                    "USD",
                    "open"),
                new FinanceInvoice(
                    aprilInvoiceId,
                    companyId,
                    customerId,
                    "INV-202604-001",
                    new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc),
                    1000m,
                    "USD",
                    "open"));
            dbContext.FinanceTransactions.AddRange(
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, customerId, marchInvoiceId, null, new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc), "customer_payment", 6000m, "USD", "March customer payment", "AGG-202603-001"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, vendorId, null, null, new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc), "cloud_hosting", -2000m, "USD", "March cloud hosting", "AGG-202603-002"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, vendorId, null, null, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), "travel", -1100m, "USD", "March travel", "AGG-202603-003"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, customerId, aprilInvoiceId, null, new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc), "customer_payment", 1000m, "USD", "April customer payment", "AGG-202604-001"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, vendorId, null, null, new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), "cloud_hosting", -1200m, "USD", "April cloud hosting", "AGG-202604-002"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, vendorId, null, null, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), "contractors", -1400m, "USD", "April contractors", "AGG-202604-003"));

            return Task.CompletedTask;
        });

        return new FinanceAggregateSeed(companyId, subject, email, displayName);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record FinanceAggregateSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed class CashBalanceResponse
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class MonthlyProfitAndLossResponse
    {
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal NetResult { get; set; }
    }

    private sealed class ExpenseBreakdownResponse
    {
        public decimal TotalExpenses { get; set; }
        public List<ExpenseCategoryResponse> Categories { get; set; } = [];
    }

    private sealed class ExpenseCategoryResponse
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private sealed class TransactionResponse
    {
        public Guid Id { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class InvoiceResponse
    {
        public Guid Id { get; set; }
        public string CounterpartyName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class AccountBalanceResponse
    {
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}
