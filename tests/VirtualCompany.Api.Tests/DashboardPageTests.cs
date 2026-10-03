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
    public void Dashboard_renders_current_action_first_workspace_through_typed_client()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        using var context = CreateContext(companyId);

        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/dashboard?companyId={companyId:D}");

        var cut = context.RenderComponent<Dashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='today-workspace']");
            Assert.Contains("Contoso Labs", cut.Markup);
            Assert.Single(cut.FindAll("[data-testid='today-priority']"));
            Assert.Contains("returnUrl=", cut.Find(".today-priority a").GetAttribute("href"));
        });
        Assert.True(cut.Markup.IndexOf("today-situation-title", StringComparison.Ordinal) <
                    cut.Markup.IndexOf("today-priorities-title", StringComparison.Ordinal));
    }
    private static TestContext CreateContext(Guid companyId)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
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
                    ActiveCompany = new() { CompanyId = companyId, CompanyName = "Contoso Labs", Status = "active" },
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
                var candidate when candidate == $"/api/companies/{companyId:D}/workspace/today" => CreateJsonResponse(new TodayWorkspaceViewModel(
                    companyId, new("Contoso Labs", "Today", "Your work"), "company",
                    [new("company", "Company", true, "Active membership")],
                    new("Current work", "Source-linked work", DateTime.UtcNow, "current", true),
                    [new("priority", 1, "company", "Review work", "Deadline approaching", "Owner", null, "Open record",
                        DateTime.UtcNow, "current", "work_task", null, "/work?tab=tasks", false, null, true, 1m)],
                    [], null, null, null, null, [], [], DateTime.UtcNow, null, false, [])),
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
        var transport = new CompanyApiTransport(onboardingHttpClient);
        context.Services.AddSingleton<ITodayWorkspaceApiClient>(new TodayWorkspaceApiClient(transport, false));
        context.Services.AddSingleton<IMonthlyWorkspaceApiClient>(new MonthlyWorkspaceApiClient(transport, false));
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
