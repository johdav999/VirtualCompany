using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed class QuarterlyPlanningIntegrationTests
{
    public static async Task<T> Read<T>(HttpResponseMessage response)
    { return await QuarterlyPlanningFixture.Read<T>(response); }
    public static async Task<QuarterReviewDocument> Save(HttpClient h, QuarterlyPlanningFixture s, PreviewQuarterReview? input = null, QuarterReviewDocument? prior = null)
    { return await QuarterlyPlanningFixture.Save(h, s, input, prior); }
    [Fact] public async Task Reconciled_actual_dependencies_conflicts_and_owner_milestone_revision_survive_reload()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var h = s.Client(f);
        var first = await Save(h, s); var o = Assert.Single(first.Review.Objectives); Assert.Equal(s.Actual, o.Actual); Assert.Equal("Missed target", o.Outcome); Assert.Contains(o.DependencyRisks, x => x.Contains("another operating plan")); Assert.Equal(10, Assert.Single(first.Review.Conflicts).Shortfall);
        var input = s.Proposal with { Objectives = [s.Proposal.Objectives[0] with { OwnerUserId = s.Company.Manager, Target = 90, Milestones = [new("Revised service review", new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), "completed")] }], Resources = [], Notes = "Revised outlook" };
        var second = await Save(h, s, input, first); Assert.Equal(2, second.Summary.Revision); Assert.Contains(second.Changes, x => x.Contains("owner")); Assert.Contains(second.Changes, x => x.Contains("milestones"));
        var old = await Read<QuarterReviewDocument>(await h.PostAsync(s.Root + $"/reviews/{first.Summary.Id}/open", null)); Assert.Equal(s.Company.Owner, old.Review.Objectives[0].Objective.OwnerUserId); Assert.Equal(100, old.Review.Objectives[0].Objective.Target); Assert.Equal(s.Actual, old.Review.Objectives[0].Actual);
        Assert.Equal(DateTimeKind.Utc, old.Review.Period.StartUtc.Kind); Assert.Equal(DateTimeKind.Utc, old.Review.Objectives[0].Objective.Milestones.Single().DueUtc.Kind);
        var retainedEdit = await Read<QuarterReviewPreview>(await h.PostAsJsonAsync(s.Root + "/preview", old.Review.Proposal)); Assert.Equal(old.Review.Objectives[0].Objective.Milestones.Single().DueUtc, retainedEdit.Objectives[0].Objective.Milestones.Single().DueUtc);
        var reload = await Read<QuarterReviewDocument>(await h.PostAsync(s.Root + $"/reviews/{second.Summary.Id}/open", null)); Assert.Equal(s.Company.Manager, reload.Review.Objectives[0].Objective.OwnerUserId); Assert.Equal("Revised service review", reload.Review.Objectives[0].Objective.Milestones.Single().Title);
    }
    [Fact] public async Task Missing_month_is_unavailable_and_snapshot_period_unit_owner_and_goal_version_are_validated()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var h = s.Client(f);
        var row = s.Proposal.Objectives[0];
        var partial = await Read<QuarterReviewPreview>(await h.PostAsJsonAsync(s.Root + "/preview", s.Proposal with { Objectives = [row with { Measures = row.Measures.Take(2).ToArray() }] })); Assert.Null(partial.Objectives[0].Actual); Assert.Equal("Evidence unavailable", partial.Objectives[0].Outcome);
        foreach (var invalid in new[] { row with { OwnerUserId = Guid.NewGuid() }, row with { Unit = "USD" }, row with { Milestones = [new("Outside quarter", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "planned")] } })
            Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsJsonAsync(s.Root + "/preview", s.Proposal with { Objectives = [invalid] })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsJsonAsync(s.Root + "/preview", s.Proposal with { Objectives = [row with { GoalVersion = 9 }] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsJsonAsync(s.Root + "/preview", s.Proposal with { Quarter = 4 })).StatusCode);
    }
    [Fact] public async Task Planning_role_does_not_grant_protected_snapshot_access_and_cross_company_reads_and_writes_are_denied()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var owner = s.Client(f); using var manager = s.Client(f, "p19-manager"); var saved = await Save(owner, s);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(s.Root + "/options?fiscalYear=2026&quarter=3")).StatusCode);
        await f.SeedAsync(async db => { var membership = await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == s.Company.Company && x.UserId == s.Company.Manager);
            db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), s.Company.Company, ResponsibilityArea.CompanyPerformance, ResponsibilityAssignmentKind.Primary, membership.Id, null, AgentAutonomyLevel.Level1, null, null)); });
        var protectedReview = await Read<QuarterReviewDocument>(await manager.PostAsync(s.Root + $"/reviews/{saved.Summary.Id}/open", null)); Assert.Null(protectedReview.Review.Objectives[0].Actual); Assert.Empty(protectedReview.Review.Objectives[0].EvidenceLinks);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(s.Root + "/preview", s.Proposal)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/companies/{s.Company.Foreign}/planning/quarters/reviews/{saved.Summary.Id}/open", null)).StatusCode);
        using var foreignContext = WeeklyWorkspaceFixture.Client(f); foreignContext.DefaultRequestHeaders.Add("X-Company-Id", s.Company.Foreign.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await foreignContext.PostAsync($"/api/companies/{s.Company.Foreign}/planning/quarters/reviews/{saved.Summary.Id}/open", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(s.Root + "/preview", s.Proposal with { Objectives = [s.Proposal.Objectives[0] with { GoalId = Guid.NewGuid() }] })).StatusCode);
    }
    [Fact] public async Task Stable_request_is_idempotent_and_stale_successor_or_changed_preview_is_rejected()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var h = s.Client(f);
        var p = await Read<QuarterReviewPreview>(await h.PostAsJsonAsync(s.Root + "/preview", s.Proposal)); var cmd = new SaveQuarterReview(s.Proposal, null, 0, Guid.NewGuid(), p.Fingerprint);
        var first = await Read<QuarterReviewDocument>(await h.PostAsJsonAsync(s.Root + "/reviews", cmd)); var again = await Read<QuarterReviewDocument>(await h.PostAsJsonAsync(s.Root + "/reviews", cmd)); Assert.Equal(first.Summary.Id, again.Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsJsonAsync(s.Root + "/reviews", cmd with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsJsonAsync(s.Root + "/reviews", cmd with { Proposal = s.Proposal with { Notes = "changed" }, PreviousId = first.Summary.Id, ExpectedRevision = 1, RequestId = Guid.NewGuid() })).StatusCode);
        await f.SeedAsync(async db => Assert.Equal(1, await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == s.Company.Company && x.Action == "company.quarter.reviewed")));
    }
    [Fact] public void Fiscal_quarter_Dst_year_boundary_and_nonadditive_measure_rules_are_explicit()
    {
        var p = QuarterlyPlanningCalculation.Period(2026, 4, 4, 1, "Europe/Stockholm", "SEK", 1); Assert.Equal(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc), p.StartUtc); Assert.Equal(new DateTime(2027, 3, 31, 22, 0, 0, DateTimeKind.Utc), p.EndUtc);
        var q = QuarterlyPlanningCalculation.Period(2026, 3, 1, 1, "UTC", "SEK", 1);
        (DateTime, DateTime, decimal?)[] rows = [(q.StartUtc, q.StartUtc.AddMonths(1), 1), (q.StartUtc.AddMonths(1), q.StartUtc.AddMonths(2), 2), (q.StartUtc.AddMonths(2), q.EndUtc, 3)];
        Assert.Equal(6, QuarterlyPlanningCalculation.Aggregate("support.volume", rows, q)); Assert.Null(QuarterlyPlanningCalculation.Aggregate("support.sla", rows, q)); Assert.Null(QuarterlyPlanningCalculation.Aggregate("sales.forecast", rows, q)); Assert.Null(QuarterlyPlanningCalculation.Aggregate("support.volume", [rows[0], rows[0], rows[2]], q));
        Assert.Equal(50, QuarterlyPlanningCalculation.Progress(5, 10, 0)); Assert.Null(QuarterlyPlanningCalculation.Progress(5, 10, 10));
    }
}
