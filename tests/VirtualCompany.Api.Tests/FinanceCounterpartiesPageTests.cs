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

public sealed class FinanceCounterpartiesPageTests
{
    [Fact]
    public void Counterparties_page_renders_selected_customer_finance_summary_and_editor_fields()
    {
        var companyId = Guid.Parse("7d48d5a7-59d4-4d9d-95a2-634afe31d810");
        var customerId = Guid.Parse("6081f057-5cbf-4e92-92c5-b28e4603a368");
        var customer = new FinanceCounterpartyResponse
        {
            Id = customerId,
            CompanyId = companyId,
            CounterpartyType = "customer",
            Name = "Fourth Coffee",
            Email = "finance@fourthcoffee.example",
            PaymentTerms = "Net45",
            TaxId = "SE556677-8899",
            CreditLimit = 25000m,
            PreferredPaymentMethod = "bank_transfer",
            DefaultAccountMapping = "1100",
            CreatedUtc = new DateTime(2026, 4, 1, 10, 15, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 4, 2, 13, 45, 0, DateTimeKind.Utc)
        };

        using var harness = CreateHarness(companyId, [customer], []);
        harness.Navigation.NavigateTo($"http://localhost/finance/admin/counterparties?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<CounterpartiesPage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() => Assert.Contains("Fourth Coffee", cut.Markup));
        cut.Find(".list-group-item").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Tax ID", cut.Markup);
            Assert.Contains("SE556677-8899", cut.Markup);
            Assert.Contains("25000", cut.Markup);
            Assert.Contains("bank_transfer", cut.Markup);
            Assert.Contains("1100", cut.Markup);
            Assert.Contains("Net45", cut.Markup);
            Assert.NotNull(cut.Find("#counterparty-tax-id"));
            Assert.NotNull(cut.Find("#counterparty-credit-limit"));
            Assert.NotNull(cut.Find("#counterparty-payment-method"));
            Assert.NotNull(cut.Find("#counterparty-account-mapping"));
        });
    }

    [Fact]
    public void Counterparties_page_creates_supplier_and_reloads_persisted_finance_fields()
    {
        var companyId = Guid.Parse("f35d18f2-7383-4cb8-a18b-723ec5e552bb");
        UpsertFinanceCounterpartyRequest? capturedSupplierRequest = null;
        var suppliers = new List<FinanceCounterpartyResponse>();

        using var harness = CreateHarness(
            companyId,
            [],
            suppliers,
            onCreateSupplier: request =>
            {
                capturedSupplierRequest = request;
                var created = new FinanceCounterpartyResponse
                {
                    Id = Guid.Parse("c647b653-fb9e-4f1e-a1cc-2defa6dc5a3a"),
                    CompanyId = companyId,
                    CounterpartyType = "supplier",
                    Name = request.Name,
                    Email = request.Email,
                    PaymentTerms = request.PaymentTerms,
                    TaxId = request.TaxId,
                    CreditLimit = request.CreditLimit,
                    PreferredPaymentMethod = request.PreferredPaymentMethod,
                    DefaultAccountMapping = request.DefaultAccountMapping,
                    CreatedUtc = new DateTime(2026, 4, 20, 8, 30, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 4, 20, 8, 30, 0, DateTimeKind.Utc)
                };

                suppliers.Clear();
                suppliers.Add(created);
                return created;
            });

        harness.Navigation.NavigateTo($"http://localhost/finance/admin/counterparties?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<CounterpartiesPage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() => Assert.Contains("Finance counterparties", cut.Markup));
        cut.FindAll("button").Single(button => button.TextContent.Contains("Suppliers", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("Create Supplier", cut.Markup));

        cut.Find("#counterparty-name").Change("Litware Services");
        cut.Find("#counterparty-email").Change("ap@litware.example");
        cut.Find("#counterparty-payment-terms").Change("Net30");
        cut.Find("#counterparty-tax-id").Change("SE998877-6655");
        cut.Find("#counterparty-credit-limit").Change("5000");
        cut.Find("#counterparty-payment-method").Change("bank_transfer");
        cut.Find("#counterparty-account-mapping").Change("2100");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Supplier saved.", cut.Markup);
            Assert.DoesNotContain("alert alert-danger", cut.Markup);
            Assert.Contains("Litware Services", cut.Markup);
            Assert.Contains("SE998877-6655", cut.Markup);
            Assert.Contains("5000", cut.Markup);
            Assert.Contains("2100", cut.Markup);
        });

        Assert.NotNull(capturedSupplierRequest);
        Assert.Equal("Litware Services", capturedSupplierRequest!.Name);
        Assert.Equal("ap@litware.example", capturedSupplierRequest.Email);
        Assert.Equal("Net30", capturedSupplierRequest.PaymentTerms);
        Assert.Equal("SE998877-6655", capturedSupplierRequest.TaxId);
        Assert.Equal(5000m, capturedSupplierRequest.CreditLimit);
        Assert.Equal("bank_transfer", capturedSupplierRequest.PreferredPaymentMethod);
        Assert.Equal("2100", capturedSupplierRequest.DefaultAccountMapping);
    }

    private static CounterpartiesPageHarness CreateHarness(
        Guid companyId,
        List<FinanceCounterpartyResponse> customers,
        List<FinanceCounterpartyResponse> suppliers,
        Func<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>? onCreateSupplier = null)
    {
        var context = new TestContext();
        context.Services.AddOptions();
        context.Services.AddSingleton(new FinanceAccessResolver());

        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUser(companyId, "owner")),
                _ => CreateNotFoundResponse()
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == $"/internal/companies/{companyId:D}/finance/customers" &&
                request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse(customers);
            }

            if (path == $"/internal/companies/{companyId:D}/finance/suppliers" &&
                request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse(suppliers);
            }

            if (path?.StartsWith($"/internal/companies/{companyId:D}/finance/customers/", StringComparison.Ordinal) == true &&
                request.Method == HttpMethod.Get)
            {
                var idText = path[(path.LastIndexOf('/') + 1)..];
                return Guid.TryParse(idText, out var customerId)
                    ? customers.SingleOrDefault(x => x.Id == customerId) is { } customer
                        ? CreateJsonResponse(customer)
                        : CreateNotFoundResponse()
                    : CreateNotFoundResponse();
            }

            if (path?.StartsWith($"/internal/companies/{companyId:D}/finance/suppliers/", StringComparison.Ordinal) == true &&
                request.Method == HttpMethod.Get)
            {
                var idText = path[(path.LastIndexOf('/') + 1)..];
                return Guid.TryParse(idText, out var supplierId)
                    ? suppliers.SingleOrDefault(x => x.Id == supplierId) is { } supplier
                        ? CreateJsonResponse(supplier)
                        : CreateNotFoundResponse()
                    : CreateNotFoundResponse();
            }

            if (path == $"/internal/companies/{companyId:D}/finance/suppliers" &&
                request.Method == HttpMethod.Post)
            {
                var payload = await request.Content!.ReadFromJsonAsync<UpsertFinanceCounterpartyRequest>(cancellationToken: cancellationToken);
                var created = onCreateSupplier!(payload!);
                return CreateJsonResponse(created);
            }

            return CreateNotFoundResponse();
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new CounterpartiesPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
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

    private sealed record CounterpartiesPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
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
