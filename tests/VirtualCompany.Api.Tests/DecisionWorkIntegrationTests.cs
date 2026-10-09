using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
public sealed class DecisionWorkIntegrationTests
{
    [Theory][InlineData("month")][InlineData("quarter")][InlineData("annual")][InlineData("scenario")]
    public async Task Each_native_source_creates_one_owned_task_and_retains_snapshot_audit_and_idempotence(string kind)
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f);
        var input = s.Input(kind); var p = await AnnualPlanningFixture.Read<DecisionWorkPreview>(await h.PostAsync(s.Root + "/preview", JsonContent.Create(input)));
        var command = new ConfirmDecisionWork(Guid.NewGuid(), input, p.Fingerprint);
        var d = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(command)));
        var retry = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(command)));
        var semanticRetry = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(command with { RequestId = Guid.NewGuid() })));
        Assert.Equal(d.TaskId, retry.TaskId); Assert.Equal(d.TaskId, semanticRetry.TaskId); Assert.Equal(p.Source.EvidenceJson, d.Created.Source.EvidenceJson); Assert.Equal(input.OwnerUserId, d.Created.Input.OwnerUserId);
        if (kind == "month") Assert.Contains("period=month&", d.Created.Source.SourcePath);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsync(s.Root, JsonContent.Create(command with { Input = input with { Objective = "Different retry" } }))).StatusCode);
        await f.SeedAsync(async db => { var origin = await db.Set<DecisionWorkOrigin>().IgnoreQueryFilters().Include(x => x.Collaborators).SingleAsync(x => x.CompanyId == s.Company);
            Assert.Single(origin.Collaborators); Assert.Equal(input.OwnerUserId, origin.OwnerUserId); Assert.Equal(input.Source.VersionId, origin.SourceVersionId);
            Assert.Equal(1, await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == s.Company && x.Action == "decision.work.created"));
            Assert.False(await db.ToolExecutionAttempts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company)); Assert.False(await db.OperatingDispatches.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company)); });
    }
    [Fact] public async Task Stale_source_and_changed_preview_are_rejected_but_existing_work_and_retry_remain_frozen()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f);
        var input = s.Input(); var p = await AnnualPlanningFixture.Read<DecisionWorkPreview>(await h.PostAsync(s.Root + "/preview", JsonContent.Create(input))); var command = new ConfirmDecisionWork(Guid.NewGuid(), input, p.Fingerprint);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsync(s.Root, JsonContent.Create(command with { ExpectedFingerprint = new string('0', 64) }))).StatusCode);
        var d = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(command)));
        var old = await AnnualPlanningFixture.Read<VirtualCompany.Application.Finance.StrategicScenarioDocument>(await h.PostAsync(s.Planning.Root + $"/versions/{s.ScenarioId}/open", null));
        await StrategicScenarioFixture.Save(h, s.Planning, s.Planning.Input with { Notes = "Later source choice; no work rewrite" }, old);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsync(s.Root + "/preview", JsonContent.Create(input))).StatusCode);
        var open = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root + $"/tasks/{d.TaskId}/open", null)); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(d.Created), System.Text.Json.JsonSerializer.Serialize(open.Created)); Assert.Contains("newer source", open.RevisionNotice);
        Assert.Equal(d.TaskId, (await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root, JsonContent.Create(command)))).TaskId);
    }
    [Fact] public async Task Source_scope_invalid_owners_foreign_sources_and_corrupted_snapshot_fail_closed()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f); var input = s.Input();
        foreach (var bad in new[] { input with { OwnerUserId = Guid.NewGuid() }, input with { ProposedCollaborators = [Guid.NewGuid()] }, input with { ProposedCollaborators = [input.OwnerUserId, input.OwnerUserId] }, input with { AcceptanceOutcome = "" }, input with { AcceptanceOutcome = new string('a', 2000), ProposedConstraints = new string('c', 2000) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsync(s.Root + "/preview", JsonContent.Create(bad))).StatusCode);
        using var other = s.Planning.Annual.Client(f, "p19-manager"); Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync(s.Root + "/preview", JsonContent.Create(input))).StatusCode);
        using var foreign = s.Planning.Annual.Client(f, "p19-foreign"); Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsync(s.Root + "/preview", JsonContent.Create(input))).StatusCode);
        await f.SeedAsync(db => { var user = new User(Guid.NewGuid(), "p28-foreign@example.test", "Foreign owner", "dev-header", "p28-foreign"); db.Users.Add(user); db.CompanyMemberships.Add(new(Guid.NewGuid(), s.Planning.Annual.Quarter.Company.Foreign, user.Id, VirtualCompany.Domain.Enums.CompanyMembershipRole.Owner, VirtualCompany.Domain.Enums.CompanyMembershipStatus.Active)); return Task.CompletedTask; });
        using var foreignCompany = WeeklyWorkspaceFixture.Client(f, "p28-foreign"); foreignCompany.DefaultRequestHeaders.Add("X-Company-Id", s.Planning.Annual.Quarter.Company.Foreign.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await foreignCompany.PostAsync($"/api/companies/{s.Planning.Annual.Quarter.Company.Foreign}/decision-work/preview", JsonContent.Create(input with { Source = s.Source("month") }))).StatusCode);
        var d = await DecisionWorkFixture.Create(h, s); await f.SeedAsync(async db => { var o = await db.Set<DecisionWorkOrigin>().IgnoreQueryFilters().SingleAsync(x => x.TaskId == d.TaskId); db.Entry(o).Property(x => x.PreviewJson).CurrentValue = "{}"; });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await h.PostAsync(s.Root + $"/tasks/{d.TaskId}/open", null)).StatusCode);
    }
    [Fact] public async Task Review_required_handoff_waits_in_canonical_approval_and_cannot_inherit_agent_execution_authority()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f); var d = await DecisionWorkFixture.Create(h, s);
        var taskRoot = $"/api/companies/{s.Company}/tasks/{d.TaskId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await h.PostAsync($"/api/companies/{s.Company}/approvals", JsonContent.Create(
            new CreateApprovalRequestCommand("task", d.TaskId, "user", d.Created.Input.OwnerUserId, "generic_review",
                new() { ["reason"] = System.Text.Json.Nodes.JsonValue.Create("Attempt generic work review") }, RequiredRole: "owner")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.PatchAsync(taskRoot + "/status", JsonContent.Create(new UpdateTaskStatusCommand("completed", null, "Bypass review", null)))).StatusCode);
        var reviewed = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root + $"/tasks/{d.TaskId}/review", null)); Assert.Equal("awaiting_approval", reviewed.Status); Assert.NotNull(reviewed.ApprovalId);
        Assert.Equal(reviewed.ApprovalId, (await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root + $"/tasks/{d.TaskId}/review", null))).ApprovalId);
        var tool = new ExecuteAgentToolCommand("sales.draft_proposal", "recommend", "sales", new(), null, null, null, TaskId: d.TaskId);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.PostAsync($"/api/companies/{s.Company}/agents/{Guid.NewGuid()}/executions", JsonContent.Create(tool))).StatusCode);
        var approvalRoot = $"/api/companies/{s.Company}/approvals/{reviewed.ApprovalId}";
        var approval = await AnnualPlanningFixture.Read<ApprovalRequestDto>(await h.GetAsync(approvalRoot)); Assert.Equal("planning_work_review", approval.ApprovalType);
        Assert.Contains(approval.Review!.Comparison, x => x.Field == "Acceptance outcome" && x.Proposed == d.Created.Input.AcceptanceOutcome);
        Assert.Contains(approval.Review.Comparison, x => x.Field == "Accountable owner" && x.Proposed == d.Created.Owner);
        Assert.Contains(approval.Review.Evidence, x => x.Href == d.OriginPath);
        var decision = await AnnualPlanningFixture.Read<ApprovalDecisionResultDto>(await h.PostAsync(approvalRoot + "/decisions", JsonContent.Create(new ApprovalDecisionCommand(approval.Id, "approve", Comment: "Approved internal human follow-up", ClientRequestId: Guid.NewGuid(), ReviewToken: approval.Review!.Token))));
        var approved = await AnnualPlanningFixture.Read<DecisionWorkDocument>(await h.PostAsync(s.Root + $"/tasks/{d.TaskId}/open", null)); Assert.Equal("in_progress", approved.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.PostAsync($"/api/companies/{s.Company}/agents/{Guid.NewGuid()}/executions", JsonContent.Create(tool))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.PatchAsync(taskRoot + "/status", JsonContent.Create(new UpdateTaskStatusCommand("completed", null, "Recorded outcome retained", null)))).StatusCode);
        await f.SeedAsync(async db => { Assert.Equal(1, await db.ApprovalRequests.IgnoreQueryFilters().CountAsync(x => x.CompanyId == s.Company && x.TargetEntityId == d.TaskId)); Assert.False(await db.ToolExecutionAttempts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company)); Assert.False(await db.PaymentInstructions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company)); });
    }
    [Fact] public async Task Owner_membership_revoked_after_preview_cannot_create_work()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f);
        var input = s.Input() with { OwnerUserId = s.Planning.Annual.Quarter.Company.Manager, ProposedCollaborators = [] };
        var preview = await AnnualPlanningFixture.Read<DecisionWorkPreview>(await h.PostAsync(s.Root + "/preview", JsonContent.Create(input)));
        await f.SeedAsync(async db => { var owner = await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == s.Company && x.UserId == input.OwnerUserId);
            db.Entry(owner).Property(x => x.Status).CurrentValue = VirtualCompany.Domain.Enums.CompanyMembershipStatus.Revoked; });
        Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsync(s.Root, JsonContent.Create(new ConfirmDecisionWork(Guid.NewGuid(), input, preview.Fingerprint)))).StatusCode);
        await f.SeedAsync(async db => Assert.False(await db.Set<DecisionWorkOrigin>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company)));
    }
}
