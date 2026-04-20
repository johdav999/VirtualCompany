using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardPageTests
{
    [Fact]
    public void Dashboard_renders_required_section_order_for_action_first_layout()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        using var context = CreateContext(companyId);

        var cut = context.RenderComponent<Dashboard>(parameters => parameters
            .Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='company-health-banner']"));
            Assert.NotNull(cut.Find("[data-testid='today-focus-panel']"));
            Assert.NotNull(cut.Find("[data-testid='top-actions-panel']"));
            Assert.NotNull(cut.Find("[data-testid='dashboard-snapshot-grid']"));
            Assert.NotNull(cut.Find("[data-testid='agent-activity-panel']"));
            Assert.NotNull(cut.Find("[data-testid='department-cards-panel']"));
        });

        Assert.True(
            cut.Markup.IndexOf("data-testid=\"company-health-banner\"", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-testid=\"today-focus-panel\"", StringComparison.Ordinal) &&
            cut.Markup.IndexOf("data-testid=\"today-focus-panel\"", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-testid=\"top-actions-panel\"", StringComparison.Ordinal) &&
            cut.Markup.IndexOf("data-testid=\"top-actions-panel\"", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-testid=\"dashboard-snapshot-grid\"", StringComparison.Ordinal) &&
            cut.Markup.IndexOf("data-testid=\"dashboard-snapshot-grid\"", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-testid=\"agent-activity-panel\"", StringComparison.Ordinal) &&
            cut.Markup.IndexOf("data-testid=\"agent-activity-panel\"", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-testid=\"department-cards-panel\"", StringComparison.Ordinal));
    }

    private static TestContext CreateContext(Guid companyId)
    {
        var context = new TestContext();
        context.Services.AddLogging();
        context.Services.AddScoped<IDashboardInteractionService, DashboardInteractionService>();

        var onboardingHttpClient = new HttpClient(new AsyncStubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            return Task.FromResult(path switch
            {
                "/api/auth/me" => CreateJsonResponse(new CurrentUserContextViewModel
                {
                    User = new CurrentUserViewModel
                    {
                        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Email = "founder@example.com",
                        DisplayName = "Founder",
                        AuthProvider = "dev-header",
                        AuthSubject = "founder"
                    },
                    Memberships =
                    [
                        new CompanyMembershipViewModel
                        {
                            MembershipId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CompanyId = companyId,
                            CompanyName = "Contoso Labs",
                            MembershipRole = "owner",
                            Status = "active"
                        }
                    ]
                }),
                "/api/onboarding/progress" => new HttpResponseMessage(HttpStatusCode.NotFound),
                var candidate when candidate == $"/api/companies/{companyId:D}/access" => CreateJsonResponse(new CompanyAccessViewModel
                {
                    CompanyId = companyId,
                    CompanyName = "Contoso Labs",
                    MembershipRole = "owner",
                    Status = "active"
                }),
                var candidate when candidate == $"/api/companies/{companyId:D}/dashboard-entry" => CreateJsonResponse(new CompanyDashboardEntryViewModel
                {
                    CompanyId = companyId,
                    CompanyName = "Contoso Labs",
                    RequiresOnboarding = false,
                    ShowStarterGuidance = false
                }),
                var candidate when candidate == $"/api/companies/{companyId:D}/briefings/latest" => CreateJsonResponse(new DashboardBriefingCardViewModel()),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        };

        context.Services.AddSingleton(new OnboardingApiClient(onboardingHttpClient));
        context.Services.AddSingleton(new ActionInsightApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        context.Services.AddSingleton(new TodayFocusApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        context.Services.AddSingleton(new ExecutiveCockpitApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        context.Services.AddSingleton(new FinanceApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, logger: null, useOfflineMode: true));

        return context;
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}