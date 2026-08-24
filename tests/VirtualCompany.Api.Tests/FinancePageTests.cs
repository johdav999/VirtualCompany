using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePageTests
{
    [Fact]
    public void Finance_page_renders_the_current_operating_overview_for_an_authorized_company()
    {
        var companyId = Guid.Parse("c99702c3-ecda-49ac-b782-5ebcfdbf1471");
        using var context = CreateContext(companyId, "owner");
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/finance?companyId={companyId:D}");

        var cut = context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash plan snapshot", cut.Markup);
            Assert.Contains("Finance Manager", cut.Markup);
            Assert.Contains("Monthly summary", cut.Markup);
            Assert.Contains($"companyId={companyId:D}", cut.Markup);
        });
    }

    [Fact]
    public void Finance_page_does_not_surface_retired_seed_or_simulation_controls()
    {
        var companyId = Guid.Parse("7b791dba-0d66-4717-a832-8f562cdf07ce");
        using var context = CreateContext(companyId, "owner");
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/finance?companyId={companyId:D}");

        var cut = context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Generate finance data", cut.Markup);
            Assert.DoesNotContain("Regenerate finance data", cut.Markup);
            Assert.DoesNotContain("Finance progression controls", cut.Markup);
            Assert.Contains("Cash plan snapshot", cut.Markup);
        });
    }

    [Fact]
    public void Finance_page_keeps_company_access_authoritative()
    {
        var companyId = Guid.Parse("44f7a342-08c8-4b33-ab40-39180c5c97f0");
        using var context = CreateContext(companyId, "employee");
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/finance?companyId={companyId:D}");

        var cut = context.RenderComponent<FinancePage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance access requires the finance.view permission for the selected company.", cut.Markup);
            Assert.DoesNotContain("Cash plan snapshot", cut.Markup);
        });
    }

    private static TestContext CreateContext(Guid companyId, string membershipRole)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/auth/me"
                ? CreateJsonResponse(CreateCurrentUser(companyId, membershipRole))
                : new HttpResponseMessage(HttpStatusCode.NotFound)))
        {
            BaseAddress = new Uri("http://localhost/")
        }));
        context.Services.AddSingleton(new FinanceApiClient(
            new HttpClient { BaseAddress = new Uri("http://localhost/") },
            logger: null,
            useOfflineMode: true));
        return context;
    }

    private static CurrentUserContextViewModel CreateCurrentUser(Guid companyId, string membershipRole)
    {
        var membershipId = Guid.NewGuid();
        return new CurrentUserContextViewModel
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = membershipId,
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = membershipId,
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
