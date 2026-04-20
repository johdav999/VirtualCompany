using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Bunit.TestDoubles;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TopActionsListTests
{
    [Fact]
    public void Component_renders_top_five_items_and_visible_full_queue_link()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var items = new[]
        {
            Item(companyId, "approval-1", "critical", 95, DateTime.UtcNow.AddMinutes(-30), "Approval required"),
            Item(companyId, "task-1", "critical", 90, DateTime.UtcNow.AddHours(1), "Resolve blocked release"),
            Item(companyId, "task-2", "high", 81, DateTime.UtcNow.AddHours(2), "Review supplier escalation"),
            Item(companyId, "task-3", "high", 75, DateTime.UtcNow.AddHours(2), "Contact customer"),
            Item(companyId, "task-4", "medium", 60, DateTime.UtcNow.AddHours(8), "Prepare handoff"),
            Item(companyId, "task-5", "low", 10, null, "Deferred follow-up")
        };

        using var context = CreateContext(_ => CreateJsonResponse(items));
        var navigation = context.Services.GetRequiredService<FakeNavigationManager>();
        var cut = context.RenderComponent<TopActionsList>(parameters => parameters
            .Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(5, cut.FindAll("[data-testid='top-action-item']").Count);
            Assert.Contains("Approval required", cut.Markup);
            Assert.Contains("Resolve blocked release", cut.Markup);
            Assert.Contains("Review supplier escalation", cut.Markup);
            Assert.Contains("Contact customer", cut.Markup);
            Assert.Contains("Prepare handoff", cut.Markup);
            Assert.DoesNotContain("Deferred follow-up", cut.Markup);
        });

        var expectedQueuePath = DashboardRoutes.EnsureCompanyContext(QueueRoutes.BuildPath(companyId), companyId, QueueRoutes.Home);
        Assert.Equal(expectedQueuePath, cut.Find("[data-testid='view-full-queue-link']").GetAttribute("href"));
        cut.Find("[data-testid='view-full-queue-link']").Click();

        Assert.Equal($"http://localhost{expectedQueuePath}", navigation.Uri);
    }

    [Fact]
    public void Component_caps_requested_top_actions_at_five()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        Uri? requestUri = null;
        var items = Enumerable.Range(1, 6)
            .Select(index => Item(companyId, $"task-{index}", "high", 100 - index, DateTime.UtcNow.AddHours(index), $"Action {index}"))
            .ToArray();

        using var context = CreateContext(request =>
        {
            requestUri = request.RequestUri;
            return CreateJsonResponse(items);
        });

        var cut = context.RenderComponent<TopActionsList>(parameters => parameters
            .Add(component => component.CompanyId, companyId)
            .Add(component => component.Count, 10));

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("[data-testid='top-action-item']").Count));
        Assert.NotNull(requestUri);
        Assert.Contains("count=5", requestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("Action 6", cut.Markup);
    }

    [Fact]
    public void Component_shows_empty_state_and_still_exposes_full_queue_link()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        using var context = CreateContext(_ => CreateJsonResponse(Array.Empty<ActionQueueItemViewModel>()));
        var cut = context.RenderComponent<TopActionsList>(parameters => parameters
            .Add(component => component.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No actionable insights need attention right now.", cut.Markup);
            Assert.NotNull(cut.Find("[data-testid='view-full-queue-link']"));
        });
    }

    private static ActionQueueItemViewModel Item(
        Guid companyId,
        string suffix,
        string priority,
        int impactScore,
        DateTime? dueUtc,
        string title) =>
        new()
        {
            InsightKey = $"{companyId:N}:{suffix}",
            CompanyId = companyId,
            Type = "task",
            SourceEntityType = "task",
            SourceEntityId = Guid.NewGuid(),
            TargetType = "task",
            TargetId = Guid.NewGuid(),
            Title = title,
            Reason = $"Reason for {title}",
            Owner = "Operations",
            DueUtc = dueUtc,
            SlaState = dueUtc.HasValue ? "due_soon" : "none",
            PriorityScore = impactScore,
            ImpactScore = impactScore,
            Priority = priority,
            DeepLink = $"/tasks?companyId={companyId:D}&taskId={Guid.NewGuid():D}",
            StableSortKey = $"{priority}:{impactScore}:{suffix}"
        };

    private static TestContext CreateContext(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var context = new TestContext();
        context.Services.AddLogging();
        context.Services.AddSingleton(new ActionInsightApiClient(
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