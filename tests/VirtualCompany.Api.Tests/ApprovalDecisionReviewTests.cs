using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed partial class ApprovalDecisionApiIntegrationTests
{
    [Fact]
    public async Task Change_request_requires_a_note_records_reviewer_and_blocks_original_work()
    {
        var seed = await SeedPendingTaskApprovalAsync("changes");
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var route = $"/api/companies/{seed.CompanyId}/approvals/{seed.ApprovalId}";
        var review = (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        Assert.True(review.Review!.CanDecide);
        Assert.Empty(review.Review.Comparison);
        var command = new ApprovalDecisionCommand(seed.ApprovalId, "request_changes", seed.StepId, null, Guid.NewGuid(), review.Review.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostReviewAsync(route + "/decisions", command)).StatusCode);
        command = command with { Comment = "Reduce the proposed amount." };
        (await client.PostReviewAsync(route + "/decisions", command)).EnsureSuccessStatusCode();
        var updated = (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        Assert.Equal("changes_requested", updated.Status); Assert.False(updated.Review!.CanDecide);
        var step = Assert.Single(updated.Steps); Assert.Equal(seed.ApproverUserId, step.DecidedByUserId);
        Assert.Equal("changes_requested", step.Status); Assert.Equal("Reviewer", step.ReviewerName); Assert.NotNull(step.DecidedAt);
        await _factory.SeedAsync(async db =>
        {
            Assert.Equal(WorkTaskStatus.Blocked, (await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.TaskId)).Status);
            Assert.Contains(await db.AuditEvents.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).Select(x => x.Action).ToListAsync(),
                action => action == "approval.review.request_changes");
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostReviewAsync(route + "/decisions", command with { Decision = "approve", ClientRequestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Material_edit_invalidates_creation_binding_even_when_client_omits_review_token()
    {
        var seed = await SeedPendingTaskApprovalAsync("binding");
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var created = await client.PostReviewAsync($"/api/companies/{seed.CompanyId}/approvals", new CreateApprovalRequestCommand(
            "task", seed.TaskId, "user", seed.ApproverUserId, "material_review", new() { ["reason"] = JsonValue.Create("Review amount") }, RequiredRole: "finance_approver"));
        created.EnsureSuccessStatusCode(); var review = (await created.Content.ReadFromJsonAsync<ApprovalRequestDto>())!;
        await _factory.SeedAsync(async db => { var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.TaskId); task.InputPayload["amount"] = JsonValue.Create(40000); });
        var route = $"/api/companies/{seed.CompanyId}/approvals/{review.Id}";
        var changed = (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        Assert.True(changed.Review!.ProposalChanged); Assert.False(changed.Review.CanDecide);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostReviewAsync(route + "/decisions", new ApprovalDecisionCommand(review.Id, "approve", review.CurrentStep!.Id))).StatusCode);
        Assert.Equal("stale", (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!.Status);
    }

    [Fact]
    public async Task Legacy_review_token_binds_observed_material_and_denies_changed_page()
    {
        var seed = await SeedPendingTaskApprovalAsync("legacy-version");
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var route = $"/api/companies/{seed.CompanyId}/approvals/{seed.ApprovalId}";
        var review = (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        await _factory.SeedAsync(async db => { var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.TaskId); task.InputPayload["amount"] = JsonValue.Create(50000); });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostReviewAsync(route + "/decisions", new ApprovalDecisionCommand(seed.ApprovalId, "approve", seed.StepId, ReviewToken: review.Review!.Token))).StatusCode);
        Assert.Equal("stale", (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!.Status);
    }

    [Fact]
    public async Task Expired_review_is_not_actionable_and_cannot_resume_work()
    {
        var seed = await SeedPendingTaskApprovalAsync("expiry");
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var created = await client.PostReviewAsync($"/api/companies/{seed.CompanyId}/approvals", new CreateApprovalRequestCommand(
            "task", seed.TaskId, "user", seed.ApproverUserId, "time_limited", new() { ["expiresUtc"] = JsonValue.Create(DateTime.UtcNow.AddMinutes(-1)) }, RequiredRole: "finance_approver"));
        created.EnsureSuccessStatusCode(); var review = (await created.Content.ReadFromJsonAsync<ApprovalRequestDto>())!;
        Assert.False(review.Review!.CanDecide);
        var route = $"/api/companies/{seed.CompanyId}/approvals/{review.Id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostReviewAsync(route + "/decisions", new ApprovalDecisionCommand(review.Id, "approve", review.CurrentStep!.Id))).StatusCode);
        Assert.Equal("expired", (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!.Status);
    }

    [Fact]
    public async Task Approval_list_and_detail_withhold_non_reviewer_material()
    {
        var seed = await SeedPendingTaskApprovalAsync("read-scope");
        using var observer = CreateAuthenticatedClient(seed.ObserverSubject, seed.ObserverEmail, "Observer");
        Assert.Equal(HttpStatusCode.NotFound, (await observer.GetAsync($"/api/companies/{seed.CompanyId}/approvals/{seed.ApprovalId}")).StatusCode);
        Assert.Empty((await observer.GetFromJsonAsync<List<ApprovalRequestDto>>($"/api/companies/{seed.CompanyId}/approvals"))!);
    }

    [Fact]
    public async Task Idempotent_step_replay_does_not_advance_the_next_mandatory_reviewer()
    {
        var seed = await SeedPendingTaskApprovalAsync("chain-replay");
        using var client = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var created = await client.PostReviewAsync($"/api/companies/{seed.CompanyId}/approvals", new CreateApprovalRequestCommand(
            "task", seed.TaskId, "user", seed.ApproverUserId, "chain", new() { ["reason"] = JsonValue.Create("Two reviews") }, Steps:
            [new(1, "role", "finance_approver"), new(2, "role", "finance_approver")]));
        created.EnsureSuccessStatusCode(); var review = (await created.Content.ReadFromJsonAsync<ApprovalRequestDto>())!;
        var command = new ApprovalDecisionCommand(review.Id, "approve", review.CurrentStep!.Id, "First review", Guid.NewGuid(), review.Review!.Token);
        var route = $"/api/companies/{seed.CompanyId}/approvals/{review.Id}/decisions";
        (await client.PostReviewAsync(route, command)).EnsureSuccessStatusCode();
        var replay = await client.PostReviewAsync(route, command); replay.EnsureSuccessStatusCode();
        var result = (await replay.Content.ReadFromJsonAsync<ApprovalDecisionResultDto>())!;
        Assert.Equal(2, result.NextStep!.SequenceNo); Assert.False(result.IsFinalized);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostReviewAsync(route, command with { Decision = "reject" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostReviewAsync(route, command with { StepId = result.NextStep.Id, ClientRequestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_reviewers_record_exactly_one_decision() => await ConcurrentReviewAsync();

    [ApiSqlServerFact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_serializable_claim_and_changes_requested_storage_roundtrip()
    {
        using var sql = new ApprovalDecisionApiIntegrationTests(TestWebApplicationFactory.CreateSqlServer(TimeProvider.System));
        await sql.ConcurrentReviewAsync();
        await sql.Change_request_requires_a_note_records_reviewer_and_blocks_original_work();
    }

    private async Task ConcurrentReviewAsync()
    {
        var seed = await SeedPendingTaskApprovalAsync("concurrent-review");
        using var first = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        using var second = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, "Reviewer");
        var route = $"/api/companies/{seed.CompanyId}/approvals/{seed.ApprovalId}";
        var review = (await first.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        var command = new ApprovalDecisionCommand(seed.ApprovalId, "approve", seed.StepId, "Reviewed", Guid.NewGuid(), review.Review!.Token);
        var responses = await Task.WhenAll(first.PostReviewAsync(route + "/decisions", command), second.PostReviewAsync(route + "/decisions", command with { ClientRequestId = Guid.NewGuid(), Decision = "reject" }));
        Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.OK)); Assert.Single(responses.Where(x => x.StatusCode == HttpStatusCode.BadRequest));
        await _factory.SeedAsync(async db => Assert.Equal(1, await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && (x.Action == "approval.review.approve" || x.Action == "approval.review.reject"))));
    }
}

internal static class DecisionReviewTestHttp { public static Task<HttpResponseMessage> PostReviewAsync(this HttpClient client, string route, object body) => client.PostAsync(route, JsonContent.Create(body)); }
