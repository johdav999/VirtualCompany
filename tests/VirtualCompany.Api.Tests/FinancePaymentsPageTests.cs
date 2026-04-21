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

public sealed class FinancePaymentsPageTests
{
    [Fact]
    public void Payments_page_renders_list_and_selected_detail()
    {
        var companyId = Guid.Parse("a53bf35e-a656-45a3-8b3d-b6d498aa1a61");
        var paymentId = Guid.Parse("66d81e13-9914-44cf-8715-1c8998eeffaf");
        var payments = new List<FinancePaymentResponse>
        {
            new()
            {
                Id = Guid.Parse("f4a5aa2d-0b69-4f1d-b26e-8ceee32418a3"),
                CompanyId = companyId,
                PaymentType = "incoming",
                Amount = 1520.55m,
                Currency = "USD",
                PaymentDate = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                Method = "bank_transfer",
                Status = "completed",
                CounterpartyReference = "ACME-REC-18",
                CreatedUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                Id = paymentId,
                CompanyId = companyId,
                PaymentType = "outgoing",
                Amount = 640.10m,
                Currency = "EUR",
                PaymentDate = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc),
                Method = "ach",
                Status = "pending",
                CounterpartyReference = "VENDOR-PAYOUT-12",
                CreatedUtc = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        using var harness = CreateHarness(companyId, payments);
        harness.Navigation.NavigateTo($"http://localhost/finance/payments/{paymentId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<PaymentsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.PaymentId, paymentId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Payment list", cut.Markup);
            Assert.Contains("Payment detail", cut.Markup);
            Assert.Contains("VENDOR-PAYOUT-12", cut.Markup);
            Assert.Contains("EUR 640.10", cut.Markup);
            Assert.Contains("pending", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ach", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("outgoing", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static PaymentsPageHarness CreateHarness(Guid companyId, List<FinancePaymentResponse> payments)
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

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == $"/internal/companies/{companyId:D}/finance/payments" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(payments));
            }

            if (path?.StartsWith($"/internal/companies/{companyId:D}/finance/payments/", StringComparison.Ordinal) == true &&
                request.Method == HttpMethod.Get)
            {
                var idText = path[(path.LastIndexOf('/') + 1)..];
                return Guid.TryParse(idText, out var paymentId)
                    ? payments.SingleOrDefault(x => x.Id == paymentId) is { } payment
                        ? Task.FromResult(CreateJsonResponse(payment))
                        : Task.FromResult(CreateNotFoundResponse())
                    : Task.FromResult(CreateNotFoundResponse());
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new PaymentsPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
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

    private sealed record PaymentsPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
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