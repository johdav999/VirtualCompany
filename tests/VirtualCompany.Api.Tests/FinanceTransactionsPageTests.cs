using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Shared;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceTransactionsPageTests
{
    [Fact]
    public void Transactions_page_renders_title_summary_and_selected_detail()
    {
        var companyId = Guid.Parse("b703c8ef-44aa-4591-9142-bb7602f5762c");
        var selectedId = Guid.Parse("6ae78009-887f-4f2f-aa0c-c476dcdf588c");
        var transactions = CreateTransactions(companyId, selectedId);

        using var harness = CreateHarness(companyId, transactions);
        harness.Navigation.NavigateTo($"http://localhost/finance/transactions/{selectedId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<TransactionsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.TransactionId, selectedId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Transactions", cut.Markup);
            Assert.Contains("All account activity across bank and integrations", cut.Markup);
            Assert.Contains("Total inflow", cut.Markup);
            Assert.Contains("USD 1,200.00", cut.Markup);
            Assert.Contains("Total outflow", cut.Markup);
            Assert.Contains("USD 550.00", cut.Markup);
            Assert.Contains("Uncategorized", cut.Markup);
            Assert.Contains("Needs review", cut.Markup);
            Assert.Contains("SIM-TXN-LICENSE", cut.Markup);
            Assert.Contains("This transaction has review flags.", cut.Markup);
        });
    }

    [Fact]
    public void Transactions_page_shows_empty_state_when_no_transactions_exist()
    {
        var companyId = Guid.Parse("0f0dc439-09ea-4355-8d80-9913a86e7703");
        using var harness = CreateHarness(companyId, []);
        harness.Navigation.NavigateTo($"http://localhost/finance/transactions?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<TransactionsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No transactions available", cut.Markup);
            Assert.Contains("Select a transaction to inspect details", cut.Markup);
        });
    }

    [Fact]
    public void Transactions_page_filters_displayed_rows_by_account_and_search()
    {
        var companyId = Guid.Parse("e065cbf4-b651-442b-b922-28585731d9ea");
        var selectedId = Guid.Parse("1f94ce99-539f-4d71-8529-ee8751c43e4e");
        var transactions = CreateTransactions(companyId, selectedId);

        using var harness = CreateHarness(companyId, transactions);
        harness.Navigation.NavigateTo($"http://localhost/finance/transactions?companyId={companyId:D}&account=Operating%20Cash&search=license");

        var cut = harness.Context.RenderComponent<TransactionsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 item", cut.Markup);
            Assert.Contains("Software license renewal", cut.Markup);
            Assert.Contains("USD 400.00", cut.Markup);
            Assert.DoesNotContain("Customer receipt", cut.Markup);
            Assert.DoesNotContain("Bank fee", cut.Markup);
        });
    }

    private static List<FinanceTransactionResponse> CreateTransactions(Guid companyId, Guid selectedId) =>
    [
        new()
        {
            Id = selectedId,
            AccountId = Guid.NewGuid(),
            AccountName = "Operating Cash",
            CounterpartyName = "Apex Software",
            TransactionUtc = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
            TransactionType = "software_licenses",
            Amount = -400m,
            Currency = "USD",
            Description = "Software license renewal",
            ExternalReference = "SIM-TXN-LICENSE",
            IsFlagged = true,
            AnomalyState = "active"
        },
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            AccountName = "Operating Cash",
            CounterpartyName = "Contoso Retail",
            TransactionUtc = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            TransactionType = "uncategorized",
            Amount = 1200m,
            Currency = "USD",
            Description = "Customer receipt",
            ExternalReference = "SIM-TXN-RECEIPT",
            IsFlagged = false,
            AnomalyState = "clear"
        },
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            AccountName = "Savings",
            TransactionUtc = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
            TransactionType = "bank_fees",
            Amount = -150m,
            Currency = "USD",
            Description = "Bank fee",
            ExternalReference = "SIM-TXN-FEE",
            IsFlagged = false,
            AnomalyState = "clear"
        }
    ];

    private static FinanceTransactionDetailResponse ToDetail(FinanceTransactionResponse response) =>
        new()
        {
            Id = response.Id,
            AccountId = response.AccountId,
            AccountName = response.AccountName,
            CounterpartyId = response.CounterpartyId,
            CounterpartyName = response.CounterpartyName,
            InvoiceId = response.InvoiceId,
            BillId = response.BillId,
            TransactionUtc = response.TransactionUtc,
            Category = response.TransactionType,
            Amount = response.Amount,
            Currency = response.Currency,
            Description = response.Description,
            ExternalReference = response.ExternalReference,
            IsFlagged = response.IsFlagged,
            AnomalyState = response.AnomalyState,
            Flags = response.IsFlagged ? ["Amount differs from normal pattern"] : [],
            Permissions = new FinanceActionPermissionsResponse { CanEditTransactionCategory = true }
        };

    private static TransactionsPageHarness CreateHarness(Guid companyId, List<FinanceTransactionResponse> transactions)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddOptions();
        context.Services.AddSingleton(new FinanceAccessResolver());

        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUser(companyId, "owner")),
                _ => CreateNotFoundResponse()
            })))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == $"/internal/companies/{companyId:D}/finance/transactions" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(transactions));
            }

            if (path?.StartsWith($"/internal/companies/{companyId:D}/finance/transactions/", StringComparison.Ordinal) == true &&
                request.Method == HttpMethod.Get)
            {
                var idText = path[(path.LastIndexOf('/') + 1)..];
                return Guid.TryParse(idText, out var transactionId)
                    ? transactions.SingleOrDefault(x => x.Id == transactionId) is { } transaction
                        ? Task.FromResult(CreateJsonResponse(ToDetail(transaction)))
                        : Task.FromResult(CreateNotFoundResponse())
                    : Task.FromResult(CreateNotFoundResponse());
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new TransactionsPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static CurrentUserContextViewModel CreateCurrentUser(Guid companyId, string membershipRole) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = Guid.NewGuid(),
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not found", detail = "Not found." })
        };

    private sealed record TransactionsPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    private sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
