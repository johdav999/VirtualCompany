using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class DecisionReviewJourneyTests
{
    [Fact]
    public void Decision_return_updates_during_same_company_navigation()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var company = Guid.NewGuid(); var id = Guid.NewGuid();
        var nav = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo($"/work?companyId={company}&tab=approvals&itemId={id}");
        var cut = context.RenderComponent<VirtualCompany.Web.Components.Work.DecisionReturnLink>(p => p.Add(x => x.CompanyId, company));
        Assert.Empty(cut.FindAll("a"));
        var review = $"/work?companyId={company}&tab=approvals&itemId={id}";
        nav.NavigateTo($"/work?companyId={company}&tab=tasks&decisionReturnUrl={Uri.EscapeDataString(review)}");
        cut.WaitForAssertion(() => Assert.Equal(review, cut.Find("a").GetAttribute("href")));
        nav.NavigateTo($"/work?companyId={company}&tab=tasks");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("a")));
    }
    [Fact]
    public void Review_returns_accept_only_same_company_approval_identity()
    {
        var company = Guid.NewGuid(); var id = Guid.NewGuid();
        var path = $"/work?companyId={company}&tab=approvals&itemId={id}&agentWorkReturnUrl=" + Uri.EscapeDataString($"/agents/work/task/{Guid.NewGuid()}/collaboration?companyId={company}&view=list&artifactId={Guid.NewGuid()}");
        Assert.Equal(path, DecisionReviewRoutes.Local(path, company));
        Assert.Null(DecisionReviewRoutes.Local(path, Guid.NewGuid()));
        Assert.Null(DecisionReviewRoutes.Local("https://example.com" + path, company));
        Assert.Null(DecisionReviewRoutes.Local($"/work?companyId={company}&tab=tasks&taskId={id}", company));
        Assert.Null(DecisionReviewRoutes.Local($"/work?companyId={company}&tab=approvals&itemId=invalid", company));
    }
    [Theory]
    [InlineData("Approve", "approve", "approved")]
    [InlineData("Reject", "reject", "rejected")]
    [InlineData("Request changes", "request_changes", "changes_requested")]
    public void Review_posts_exact_identity_step_and_version_then_keeps_history(string button, string decision, string status)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var handler = new Handler();
        context.Services.AddSingleton(new ApprovalApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var cut = context.RenderComponent<DecisionReview>(p => p.Add(x => x.CompanyId, handler.Approval.CompanyId).Add(x => x.Approval, handler.Approval));
        Assert.Equal(0, handler.Writes); Assert.Contains("Before/after evidence was not recorded", cut.Markup);
        if (decision == "request_changes") { cut.FindAll("button").Single(x => x.TextContent == button).Click(); Assert.Equal(0, handler.Writes); Assert.Contains("Explain the requested changes", cut.Markup); }
        cut.Find("textarea").Input("Please revise the terms."); cut.FindAll("button").Single(x => x.TextContent == button).Click();
        cut.WaitForAssertion(() => Assert.Contains("Decision recorded", cut.Markup));
        Assert.Equal(1, handler.Writes); Assert.Equal(handler.Approval.CompanyId.ToString(), handler.CompanyHeader);
        using var json = JsonDocument.Parse(handler.Body!); var root = json.RootElement;
        Assert.Equal(decision, root.GetProperty("decision").GetString()); Assert.Equal("version-one", root.GetProperty("reviewToken").GetString());
        Assert.Equal(handler.Step, root.GetProperty("stepId").GetGuid()); Assert.NotEqual(Guid.Empty, root.GetProperty("clientRequestId").GetGuid());
        Assert.Contains("Reviewer Jane", cut.Markup); Assert.Empty(cut.FindAll("textarea")); Assert.Equal(status, handler.Approval.Status);
        Assert.Contains("Approval does not confirm delivery", cut.Markup);
    }

    [Fact]
    public void Conflict_refreshes_stale_status_and_does_not_claim_success()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var handler = new Handler { Conflict = true };
        context.Services.AddSingleton(new ApprovalApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }));
        var cut = context.RenderComponent<DecisionReview>(p => p.Add(x => x.CompanyId, handler.Approval.CompanyId).Add(x => x.Approval, handler.Approval));
        cut.Find("textarea").Input("Retain this note"); cut.FindAll("button").Single(x => x.TextContent == "Approve").Click();
        cut.WaitForAssertion(() => Assert.Contains("Proposal changed", cut.Find("[role='alert']").TextContent));
        Assert.DoesNotContain("Decision recorded", cut.Markup); Assert.Empty(cut.FindAll("textarea")); Assert.Contains("Stale", cut.Markup);
    }

    [Fact]
    public async Task Offline_and_empty_company_fail_without_fake_decisions()
    {
        var handler = new Handler(); var client = new ApprovalApiClient(new HttpClient(handler), true);
        await Assert.ThrowsAsync<OnboardingApiException>(() => client.DecideAsync(handler.Approval.CompanyId, handler.Approval.Id, new()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Guid.Empty, handler.Approval.Id)); Assert.Equal(0, handler.Writes);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Guid Step = Guid.NewGuid(); public int Writes; public bool Conflict; public string? Body, CompanyHeader;
        public ApprovalRequestViewModel Approval;
        public Handler() => Approval = new() { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), TargetEntityType = "task", Status = "pending",
            RationaleSummary = "Review the proposed terms", CurrentStep = new() { Id = Step, ApproverType = "role", ApproverRef = "owner", Status = "pending" },
            Review = new() { Token = "version-one", CanDecide = true, Reviewer = "Owner", VersionEvidence = "Recorded proposal version" } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CompanyHeader = request.Headers.GetValues("X-Company-Id").Single();
            if (request.Method == HttpMethod.Post)
            {
                Writes++; Body = await request.Content!.ReadAsStringAsync(ct);
                if (Conflict) { Approval.Status = "stale"; Approval.Review!.CanDecide = false; return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { detail = "Proposal changed. Review again." }) }; }
                using var document = JsonDocument.Parse(Body); var decision = document.RootElement.GetProperty("decision").GetString();
                Approval.Status = decision == "approve" ? "approved" : decision == "reject" ? "rejected" : "changes_requested";
                Approval.Review!.CanDecide = false; Approval.CurrentStep = null;
                Approval.Steps = [new() { Id = Step, SequenceNo = 1, Status = Approval.Status, DecidedAt = DateTime.UtcNow, ReviewerName = "Reviewer Jane", Comment = "Please revise the terms." }];
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ApprovalDecisionResultViewModel { Approval = Approval, IsFinalized = true }) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Approval) };
        }
    }
}
