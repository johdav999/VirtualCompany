using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class PriorityWorkActionTests
{
    private static readonly Guid Company = Guid.NewGuid(), TaskId = Guid.NewGuid();
    [Fact]
    public async Task Missing_optional_marketing_review_is_null_and_preserves_company_scope()
    {
        var handler = new EmptyReviewHandler();
        var client = new MarketingApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }), false);
        Assert.Null(await client.GetDailyReviewAsync(Company, DateTime.UtcNow));
        Assert.Equal(Company.ToString(), handler.CompanyHeader);
    }
    private sealed class EmptyReviewHandler : HttpMessageHandler
    {
        public string? CompanyHeader;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CompanyHeader = request.Headers.GetValues("X-Company-Id").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
    [Fact]
    public void Opening_task_approval_shows_real_review_reason_without_payment_claim_or_decision()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var handler = new ApprovalHandler();
        context.Services.AddSingleton(new ApprovalApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));
        context.Services.GetRequiredService<VirtualCompany.Web.Localization.Formatting.ICompanyPresentationContext>().SetFormattingCulture("en-GB");
        var cut = context.RenderComponent<VirtualCompany.Web.Components.Work.WorkApprovalsPanel>(parameters =>
            parameters.Add(x => x.CompanyId, Company).Add(x => x.SelectedId, TaskId));
        cut.WaitForAssertion(() => Assert.Contains("Review the internal proposal", cut.Markup));
        Assert.Contains("Review task", cut.Markup);
        Assert.DoesNotContain("Payment requires approval", cut.Markup);
        Assert.DoesNotContain("Exceeds approval limit", cut.Markup);
        Assert.Equal(0, handler.Writes);
        Assert.Contains("23/09/2026 16:49", cut.Markup);
    }

    private sealed class ApprovalHandler : HttpMessageHandler
    {
        public int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            var approval = new ApprovalRequestViewModel { Id = TaskId, CompanyId = Company, TargetEntityType = "task",
                CreatedAt = new DateTime(2026,9,23,16,49,0,DateTimeKind.Utc),
                ApprovalType = "manual_review", Status = "pending", ThresholdContext = new() { ["reason"] = System.Text.Json.Nodes.JsonValue.Create("Review the internal proposal") } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(TaskId.ToString())
                ? JsonContent.Create(approval) : JsonContent.Create(new[] { approval }) });
        }
    }
    [Fact]
    public void Manual_follow_up_changes_only_on_explicit_action_then_reloads_persisted_detail()
    {
        using var context = Context(new Handler());
        var handler = context.Services.GetRequiredService<Handler>();
        var cut = context.RenderComponent<Work>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='complete-follow-up']"));
        Assert.Equal(0, handler.Writes);
        cut.Find("[data-testid='complete-follow-up']").Click();
        cut.WaitForAssertion(() => {
            Assert.Equal(1, handler.Writes);
            Assert.Empty(cut.FindAll("[data-testid='complete-follow-up']"));
            Assert.Contains("Completed", cut.Markup);
        });
        Assert.Equal(Company.ToString("D"), handler.CompanyHeader);
        Assert.Contains("completed", handler.Body);
        Assert.Contains("Preserve the original rationale", handler.Body);
        Assert.Contains("sourceRecord", handler.Body);
        var taskLink = cut.Find(".vc-list-item").GetAttribute("href")!;
        Assert.Contains("priorityReturnUrl=", taskLink);
        Assert.Contains("returnUrl=", taskLink);
    }

    [Theory]
    [InlineData("awaiting_approval", null, null)]
    [InlineData("in_progress", "11111111-1111-1111-1111-111111111111", null)]
    [InlineData("in_progress", null, "11111111-1111-1111-1111-111111111111")]
    public void Approval_agent_or_workflow_task_does_not_offer_manual_completion(string status, string? agent, string? workflow)
    {
        using var context = Context(new Handler(status, agent, workflow));
        var cut = context.RenderComponent<Work>();
        cut.WaitForAssertion(() => Assert.Contains("Confirm terms", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid='complete-follow-up']"));
        Assert.Equal(0, context.Services.GetRequiredService<Handler>().Writes);
    }

    [Fact]
    public void Failed_completion_shows_error_and_does_not_claim_success()
    {
        using var context = Context(new Handler(fail: true));
        var cut = context.RenderComponent<Work>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='complete-follow-up']"));
        cut.Find("[data-testid='complete-follow-up']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Task changed; refresh and retry", cut.Find("[role='alert']").TextContent));
        Assert.DoesNotContain("Completed", cut.Markup);
    }

    private static TestContext Context(Handler handler)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(handler);
        context.Services.AddSingleton(new TaskApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }));
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        var origin = DashboardRoutes.BuildTodayPath(Company, "sales");
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.WithQuery($"/work?companyId={Company:D}&taskId={TaskId:D}",
            ("returnUrl", origin), ("priorityReturnUrl", DashboardRoutes.BuildPriorityPath(Company, "sales", "focus:task", origin))));
        return context;
    }
    [Theory]
    [InlineData("2026-09-23T16:49:00")]
    [InlineData("2026-09-23T16:49:00Z")]
    [InlineData("2026-09-23T18:49:00+02:00")]
    public void Persisted_evidence_time_remains_UTC_for_legacy_and_explicit_offset_payloads(string observed)
    {
        using var context = Context(new Handler(observed: observed));
        context.Services.GetRequiredService<VirtualCompany.Web.Localization.Formatting.ICompanyPresentationContext>().SetFormattingCulture("en-GB");
        var cut = context.RenderComponent<Work>();
        cut.WaitForAssertion(() => Assert.Contains("23/09/2026 16:49", cut.Find("[data-testid='work-risk-evidence']").TextContent));
        Assert.Equal(0, context.Services.GetRequiredService<Handler>().Writes);
    }

    private sealed class Handler(string status = "new", string? agent = null, string? workflow = null, bool fail = false, string? observed = null) : HttpMessageHandler
    {
        public int Writes;
        public string? CompanyHeader, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Patch)
            {
                Writes++; CompanyHeader = request.Headers.GetValues("X-Company-Id").Single(); Body = await request.Content!.ReadAsStringAsync(ct);
                if (fail) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { detail = "Task changed; refresh and retry" }) };
                status = "completed";
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = TaskId, companyId = Company, status }) };
            }
            var detail = new TaskDetailViewModel { Id = TaskId, CompanyId = Company, Title = "Confirm terms", Type = "follow_up",
                CreatedByActorType = "user", Status = status, Priority = "high", UpdatedAt = DateTime.UtcNow,
                RationaleSummary = "Preserve the original rationale", OutputPayload = new() { ["sourceRecord"] = System.Text.Json.Nodes.JsonValue.Create("terms") },
                InputPayload = observed is null ? new() : new() { ["priorityEvidenceKey"] = System.Text.Json.Nodes.JsonValue.Create("sales-deal:1"), ["sourceCompanyId"] = System.Text.Json.Nodes.JsonValue.Create(Company.ToString("D")), ["sourceObservedUtc"] = System.Text.Json.Nodes.JsonValue.Create(observed) },
                AssignedAgentId = agent is null ? null : Guid.Parse(agent), WorkflowInstanceId = workflow is null ? null : Guid.Parse(workflow) };
            return new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(TaskId.ToString("D"))
                ? JsonContent.Create(detail) : JsonContent.Create(new TaskListResultViewModel { Items = [new() { Id = TaskId, CompanyId = Company, Title = "Confirm terms", Status = status, Type = "follow_up", Priority = "high" }] }) };
        }
    }
}
