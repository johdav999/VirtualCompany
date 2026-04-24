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

public sealed class FinanceBillsPageTests
{
    [Fact]
    public void Bills_page_renders_list_selected_detail_and_agent_insights()
    {
        var companyId = Guid.Parse("d53590ef-f7ff-4b98-a372-a9f3133e0f6c");
        var billId = Guid.Parse("8dbde10a-cbd7-4fb3-aab8-459f0f55ed1f");
        var bills = new List<FinanceBillResponse>
        {
            new()
            {
                Id = billId,
                CounterpartyId = Guid.NewGuid(),
                CounterpartyName = "Northwind Supplies",
                BillNumber = "BILL-24018",
                ReceivedUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                DueUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
                Amount = 845.30m,
                Currency = "USD",
                Status = "open"
            }
        };

        var billDetail = new FinanceBillDetailResponse
        {
            Id = billId,
            CounterpartyId = bills[0].CounterpartyId,
            CounterpartyName = bills[0].CounterpartyName,
            BillNumber = bills[0].BillNumber,
            ReceivedUtc = bills[0].ReceivedUtc,
            DueUtc = bills[0].DueUtc,
            Amount = bills[0].Amount,
            Currency = bills[0].Currency,
            Status = bills[0].Status,
            LinkedDocument = new FinanceLinkedDocumentAccessResponse
            {
                Availability = "missing",
                Message = "No linked document."
            },
            AgentInsights =
            [
                new NormalizedFinanceInsightResponse
                {
                    Id = Guid.NewGuid(),
                    Severity = "critical",
                    Status = "active",
                    CheckName = "Payables pressure",
                    Message = "This bill is overdue and needs attention.",
                    Recommendation = "Prioritize payment timing with treasury.",
                    UpdatedAt = new DateTime(2026, 4, 22, 8, 30, 0, DateTimeKind.Utc)
                }
            ]
        };

        using var harness = CreateHarness(companyId, bills, billDetail);
        harness.Navigation.NavigateTo($"http://localhost/finance/bills/{billId:D}?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<BillsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.BillId, billId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Bill list", cut.Markup);
            Assert.Contains("Bill detail", cut.Markup);
            Assert.Contains("BILL-24018", cut.Markup);
            Assert.Contains("Northwind Supplies", cut.Markup);
            Assert.Contains("USD 845.30", cut.Markup);
            Assert.Contains("Agent insights", cut.Markup);
            Assert.Contains("This bill is overdue and needs attention.", cut.Markup);
            Assert.Contains("Prioritize payment timing with treasury.", cut.Markup);
        });
    }

    private static BillsPageHarness CreateHarness(Guid companyId, List<FinanceBillResponse> bills, FinanceBillDetailResponse billDetail)
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
            if (path == $"/internal/companies/{companyId:D}/finance/bills" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(bills));
            }

            if (path == $"/internal/companies/{companyId:D}/finance/bills/{billDetail.Id:D}" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(billDetail));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new BillsPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
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

    private sealed record BillsPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
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