using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.Rendering;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayFocusPanelTests
{
    [Fact]
    public void Panel_renders_cards_in_priority_order_limits_to_five_and_uses_exact_navigation_target_for_cta()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var items = new[]
        {
            new FocusItemViewModel
            {
                Id = "approval-1",
                Title = "Approval required for task",
                Description = "Review the task approval chain.",
                ActionType = "review",
                PriorityScore = 100,
                NavigationTarget = $"/approvals?companyId={companyId:D}&approvalId=11111111-1111-1111-1111-111111111111"
            },
            new FocusItemViewModel
            {
                Id = "finance-1",
                Title = "Review cash anomaly",
                Description = "Cash movement needs immediate attention.",
                ActionType = "investigate",
                PriorityScore = 81,
                NavigationTarget = $"/finance/anomalies/33333333-3333-3333-3333-333333333333?companyId={companyId:D}"
            },
            new FocusItemViewModel
            {
                Id = "task-1",
                Title = "Resolve blocked task",
                Description = "A task is blocked and needs attention.",
                ActionType = "open",
                PriorityScore = 72,
                NavigationTarget = $"/tasks?companyId={companyId:D}&taskId=22222222-2222-2222-2222-222222222222"
            },
            new FocusItemViewModel
            {
                Id = "briefing-1",
                Title = "Read executive briefing",
                Description = "The latest weekly summary is ready.",
                ActionType = "view",
                PriorityScore = 54,
                NavigationTarget = $"/dashboard/briefings?companyId={companyId:D}"
            },
            new FocusItemViewModel
            {
                Id = "invoice-1",
                Title = "Approve invoice exception",
                Description = "Finance needs a decision to clear a blocked invoice.",
                ActionType = "approve",
                PriorityScore = 63,
                NavigationTarget = $"/finance/invoices/review/44444444-4444-4444-4444-444444444444?companyId={companyId:D}"
            },
            new FocusItemViewModel
            {
                Id = "ignored-1",
                Title = "Low priority follow-up",
                Description = "This item should not render because the panel caps at five cards.",
                ActionType = "open",
                PriorityScore = 5,
                NavigationTarget = $"/tasks?companyId={companyId:D}&taskId=55555555-5555-5555-5555-555555555555"
            }
        };

        using var context = CreateContext(_ => CreateJsonResponse(items));
        var navigation = context.Services.GetRequiredService<FakeNavigationManager>();
        var cut = context.RenderComponent<TodayFocusPanel>(parameters => parameters
            .Add(component => component.CompanyId, companyId)
            .Add(component => component.UserId, Guid.Empty));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(5, cut.FindAll("[data-testid='today-focus-card']").Count);
            Assert.Contains("Approval required for task", cut.Markup);
            Assert.Contains("Review cash anomaly", cut.Markup);
            Assert.Contains("Resolve blocked task", cut.Markup);
            Assert.Contains("Approve invoice exception", cut.Markup);
            Assert.Contains("Read executive briefing", cut.Markup);
            Assert.DoesNotContain("Low priority follow-up", cut.Markup);
            Assert.Equal(
                new[]
                {
                    "Approval required for task",
                    "Review cash anomaly",
                    "Resolve blocked task",
                    "Approve invoice exception",
                    "Read executive briefing"
                },
                cut.FindAll("[data-testid='today-focus-title']").Select(element => element.TextContent.Trim()).ToArray());
            Assert.Equal(
                new[]
                {
                    "Review the task approval chain.",
                    "Cash movement needs immediate attention.",
                    "A task is blocked and needs attention.",
                    "Finance needs a decision to clear a blocked invoice.",
                    "The latest weekly summary is ready."
                },
                cut.FindAll("[data-testid='today-focus-description']").Select(element => element.TextContent.Trim()).ToArray());
            Assert.Equal(
                new[]
                {
                    "Review",
                    "Investigate",
                    "Open",
                    "Approve",
                    "View"
                },
                cut.FindAll("[data-testid='today-focus-cta']").Select(element => element.TextContent.Trim()).ToArray());
        });

        cut.FindAll("[data-testid='today-focus-cta']")[1].Click();

        Assert.Equal($"http://localhost{DashboardRoutes.NormalizeFocusTarget(items[1].NavigationTarget, companyId)}", navigation.Uri);
    }

    [Fact]
    public void Panel_renders_each_focus_item_as_a_card_with_title_description_and_cta_button()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var items = new[]
        {
            new FocusItemViewModel
            {
                Id = "approval-1",
                Title = "Approval required for task",
                Description = "Review the task approval chain.",
                ActionType = "review",
                PriorityScore = 100,
                NavigationTarget = $"/approvals?companyId={companyId:D}&approvalId=11111111-1111-1111-1111-111111111111"
            },
            new FocusItemViewModel
            {
                Id = "task-1",
                Title = "Resolve blocked task",
                Description = "A task is blocked and needs attention.",
                ActionType = "open",
                PriorityScore = 72,
                NavigationTarget = $"/tasks/detail/22222222-2222-2222-2222-222222222222?companyId={companyId:D}&view=focus"
            }
        };

        using var context = CreateContext(_ => CreateJsonResponse(items));
        var cut = context.RenderComponent<TodayFocusPanel>(parameters => parameters.Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() => Assert.Equal(items.Length, cut.FindAll("[data-testid='today-focus-card']").Count));
        Assert.Equal(items.Select(item => item.Title), cut.FindAll("[data-testid='today-focus-title']").Select(element => element.TextContent.Trim()));
        Assert.Equal(items.Select(item => item.Description), cut.FindAll("[data-testid='today-focus-description']").Select(element => element.TextContent.Trim()));
        Assert.Equal(new[] { "Review", "Open" }, cut.FindAll("[data-testid='today-focus-cta']").Select(element => element.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void Panel_shows_empty_state_when_api_returns_no_items()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        using var context = CreateContext(_ => CreateJsonResponse(Array.Empty<FocusItemViewModel>()));
        var cut = context.RenderComponent<TodayFocusPanel>(parameters => parameters
            .Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='today-focus-empty']"));
            Assert.Contains("No focus items are ready for this workspace yet.", cut.Markup);
        });
    }

    [Fact]
    public void Panel_shows_error_state_when_api_request_fails()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        using var context = CreateContext(_ => CreateProblemResponse(HttpStatusCode.BadGateway, "Dashboard unavailable", "Focus feed is temporarily unavailable."));
        var cut = context.RenderComponent<TodayFocusPanel>(parameters => parameters
            .Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Focus feed is temporarily unavailable.", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='today-focus-card']"));
        });
    }

    private static TestContext CreateContext(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var context = new TestContext();
        context.Services.AddLogging();
        context.Services.AddSingleton(new TodayFocusApiClient(
            new HttpClient(new AsyncStubHttpMessageHandler((request, _) => Task.FromResult(responseFactory(request))))
            {
                BaseAddress = new Uri("http://localhost/")
            }));
        return context;
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateProblemResponse(HttpStatusCode statusCode, string title, string detail) =>
        new(statusCode)
        {
            Content = JsonContent.Create(new { title, detail })
        };
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
