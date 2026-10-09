using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class DecisionWorkJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), Owner = Guid.NewGuid(), SourceId = Guid.NewGuid(), TaskId = Guid.NewGuid();
    private static DecisionWorkSource Source => new(Company, new("scenario", SourceId, "0"), 1, new string('A', 64), "Capacity review", Owner,
        new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), "Europe/Stockholm", $"/dashboard?companyId={Company:D}&period=multiyear&scenario={SourceId:D}", "{\"finding\":\"Frozen source evidence\"}", true, "Original retained version");
    private static DecisionWorkInput Input => new(Source.Reference, "Capacity follow-up", Owner, new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc), "Reviewed outcome", [], "Internal only");
    private static DecisionWorkPreview Preview => new(Company, Input, Source, "Recorded owner", [], "Separate work and action approval", new string('B', 64));
    private static DecisionWorkDocument Doc => new(Company, TaskId, Preview, Source.SavedUtc, "new", null, $"/work?companyId={Company:D}&taskId={TaskId:D}", DecisionWorkRoutes.Source(Company, TaskId), "New source revisions do not rewrite work");
    private static DecisionWorkContext Options => new(Company, Source, [new(Owner, "Recorded owner")], []);
    [Fact] public void Planning_work_review_is_presented_as_human_followup_without_payment_language()
    {
        var value = ApprovalPresentationFormatter.Format(new ApprovalRequestViewModel { ApprovalType = "planning_work_review", TargetEntityType = "task", Status = "pending",
            AffectedEntities = [new() { EntityType = "task", EntityId = TaskId, Label = "Capacity follow-up" }] });
        Assert.Equal("Owned follow-up requires review", value.DisplayTitle); Assert.Equal("Capacity follow-up", value.DisplayReference); Assert.Null(value.DisplayAmount); Assert.Null(value.DisplayPaymentActivity);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => action(r, ct); }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static TestContext Context(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action)
    { var c = new TestContext().AddVirtualCompanyWebPresentationServices(); c.Services.AddSingleton(new DecisionWorkApiClient(new CompanyApiTransport(new HttpClient(new Handler(action)) { BaseAddress = new("http://localhost/") }))); return c; }
    [Fact] public void Source_traceability_renders_frozen_owner_outcome_and_canonical_review()
    {
        using var c = Context((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("review") ? Json(Doc with { Status = "awaiting_approval", ApprovalId = Guid.NewGuid() }) : Json(Doc)));
        var cut = c.RenderComponent<DecisionWorkPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.TaskId, TaskId));
        cut.WaitForAssertion(() => Assert.Contains("Frozen source evidence", cut.Markup)); Assert.Contains("Recorded owner", cut.Markup); Assert.Contains("Reviewed outcome", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Submit for work review").Click(); cut.WaitForAssertion(() => Assert.Contains("Waiting for human review", cut.Markup)); Assert.Contains("Open canonical work review", cut.Markup);
    }
    [Fact] public void Missing_due_date_blocks_preview_and_stale_source_has_no_creation_form()
    {
        using var c = Context((_, _) => Task.FromResult(Json(Options))); var cut = c.RenderComponent<DecisionWorkPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.KindCode, "scenario").Add(x => x.VersionId, SourceId).Add(x => x.ItemKey, "0"));
        cut.WaitForAssertion(() => Assert.Contains("Preview work", cut.Markup)); cut.FindAll("button").Single(x => x.TextContent == "Preview work").Click(); cut.WaitForAssertion(() => Assert.Contains("Enter the due date", cut.Markup)); Assert.DoesNotContain("work-preview", cut.Markup);
        using var stale = Context((_, _) => Task.FromResult(Json(Options with { Source = Source with { IsLatest = false } }))); var stopped = stale.RenderComponent<DecisionWorkPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.KindCode, "scenario").Add(x => x.VersionId, SourceId).Add(x => x.ItemKey, "0"));
        stopped.WaitForAssertion(() => Assert.Contains("Capacity review", stopped.Markup)); Assert.DoesNotContain("Preview work", stopped.Markup);
    }
    [Fact] public async Task Late_company_response_cannot_restore_previous_evidence()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(); var calls = 0; using var c = Context((_, _) => ++calls == 1 ? pending.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var cut = c.RenderComponent<DecisionWorkPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.TaskId, TaskId)); await cut.InvokeAsync(() => cut.SetParametersAndRender(p => p.Add(x => x.CompanyId, Guid.NewGuid()))); pending.SetResult(Json(Doc));
        cut.WaitForAssertion(() => Assert.Contains("Access changed", cut.Markup)); Assert.DoesNotContain("Frozen source evidence", cut.Markup); Assert.DoesNotContain("Recorded owner", cut.Markup);
    }
    [Fact] public async Task Typed_context_and_return_path_mismatches_fail_closed()
    {
        using var c = Context((_, _) => Task.FromResult(Json(Options with { Source = Source with { Reference = new("annual", SourceId, "decision") } }))); await Assert.ThrowsAsync<InvalidDataException>(() => c.Services.GetRequiredService<DecisionWorkApiClient>().Context(Company, Source.Reference, default));
        using var d = Context((_, _) => Task.FromResult(Json(Doc with { WorkPath = "https://untrusted.example/" }))); await Assert.ThrowsAsync<InvalidDataException>(() => d.Services.GetRequiredService<DecisionWorkApiClient>().Open(Company, TaskId, default));
    }
    [Theory][InlineData(400)][InlineData(403)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]
    public async Task Errors_are_explicit_and_never_return_mock_work(int status)
    {
        using var c = Context((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))); var client = c.Services.GetRequiredService<DecisionWorkApiClient>();
        if (status == 403) await Assert.ThrowsAsync<TodayWorkspaceAccessException>(() => client.Open(Company, TaskId, default)); else await Assert.ThrowsAsync<InvalidOperationException>(() => client.Open(Company, TaskId, default));
    }
}
