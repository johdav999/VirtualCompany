using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class LowCashAlertPageTests
{
    [Fact]
    public void Low_cash_alert_page_renders_finance_links_and_triggers_review_invoice_action()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var alertId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");
        var invoiceId = Guid.Parse("7edb1408-42d4-4a79-9aa2-3a2d2c84da3f");
        var financeRequests = new List<CapturedFinanceRequest>();

        using var context = new TestContext();
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUserContext(companyId, "owner")),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") });

        context.Services.AddSingleton(new ExecutiveCockpitApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                var path when request.Method == HttpMethod.Get &&
                               path == $"/api/companies/{companyId:D}/executive-cockpit/finance-alerts/{alertId:D}" =>
                    CreateJsonResponse(CreateAlertDetail(companyId, alertId, invoiceId)),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") });

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            financeRequests.Add(new CapturedFinanceRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-Company-Id", out var values) ? values.Single() : null));

            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                var path when request.Method == HttpMethod.Post &&
                               path == $"/internal/companies/{companyId:D}/finance/invoices/{invoiceId:D}/review-workflow" =>
                    CreateJsonResponse(new { workflowInstanceId = Guid.NewGuid() }),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") });

        var navigation = context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo($"http://localhost/finance/alerts/{alertId:D}?companyId={companyId:D}");

        var cut = context.RenderComponent<LowCashAlertPage>(parameters => parameters
            .Add(x => x.AlertId, alertId)
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash runway dropped below the critical threshold.", cut.Find("[data-testid='low-cash-alert-detail']").TextContent);
            Assert.Contains("Average monthly burn is USD 18,500.00.", cut.Find("[data-testid='low-cash-alert-factors']").TextContent);
            Assert.Equal(
                $"/finance?companyId={companyId:D}",
                cut.Find("[data-testid='finance-alert-link-finance-workspace']").GetAttribute("href"));
            Assert.Equal(
                $"/finance/monthly-summary?companyId={companyId:D}",
                cut.Find("[data-testid='finance-alert-link-finance-summary']").GetAttribute("href"));
            Assert.Equal(
                $"/finance/issues?companyId={companyId:D}",
                cut.Find("[data-testid='finance-alert-link-anomaly-workbench']").GetAttribute("href"));
            Assert.Equal(
                $"/finance/cash-position?companyId={companyId:D}",
                cut.Find("[data-testid='finance-alert-link-cash-detail']").GetAttribute("href"));
            Assert.Equal(
                $"/finance/monthly-summary?companyId={companyId:D}",
                cut.Find("[data-testid='finance-alert-action-open-finance-summary']").GetAttribute("href"));
        });

        var reviewInvoiceButton = cut.Find("[data-testid='finance-alert-action-review-invoice']");
        reviewInvoiceButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(financeRequests);
            Assert.Equal(
                $"/internal/companies/{companyId:D}/finance/invoices/{invoiceId:D}/review-workflow",
                financeRequests[0].Path);
            Assert.Equal(companyId.ToString(), financeRequests[0].CompanyHeaderValue);
            Assert.Equal(
                $"http://localhost/finance/reviews/{invoiceId:D}?companyId={companyId:D}",
                navigation.Uri);
        });
    }

    private static CurrentUserContextViewModel CreateCurrentUserContext(Guid companyId, string membershipRole) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    CompanyId = companyId,
                    CompanyName = "Contoso Labs",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                CompanyId = companyId,
                CompanyName = "Contoso Labs",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };

    private static ExecutiveCockpitFinanceAlertDetailViewModel CreateAlertDetail(Guid companyId, Guid alertId, Guid invoiceId) =>
        new()
        {
            AlertId = alertId,
            Severity = "warning",
            Status = "open",
            Summary = "Cash runway dropped below the critical threshold.",
            ContributingFactors =
            [
                "Available cash is USD 42,000.00.",
                "Average monthly burn is USD 18,500.00."
            ],
            AvailableActions =
            [
                new ExecutiveCockpitFinanceActionViewModel
                {
                    Key = "review_invoice",
                    Label = "Review invoice",
                    IsEnabled = true,
                    Route = FinanceRoutes.BuildInvoiceReviewDetailPath(invoiceId, companyId),
                    OrchestrationEndpoint = $"/internal/companies/{companyId:D}/finance/invoices/{invoiceId:D}/review-workflow",
                    HttpMethod = "POST",
                    TargetId = invoiceId
                },
                new ExecutiveCockpitFinanceActionViewModel
                {
                    Key = "open_finance_summary",
                    Label = "Open finance summary",
                    IsEnabled = true,
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId),
                    HttpMethod = "GET"
                }
            ],
            Links =
            [
                new ExecutiveCockpitDeepLinkViewModel
                {
                    Key = "finance_summary",
                    Label = "Finance summary",
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId)
                },
                new ExecutiveCockpitDeepLinkViewModel
                {
                    Key = "finance_workspace",
                    Label = "Finance workspace",
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)
                },
                new ExecutiveCockpitDeepLinkViewModel
                {
                    Key = "anomaly_workbench",
                    Label = "Anomaly workbench",
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.Issues, companyId)
                },
                new ExecutiveCockpitDeepLinkViewModel
                {
                    Key = "cash_detail",
                    Label = "Cash detail",
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId)
                }
            ],
            Route = FinanceRoutes.BuildAlertDetailPath(alertId, companyId)
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound);

    private sealed record CapturedFinanceRequest(
        string Path,
        string? CompanyHeaderValue);

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
