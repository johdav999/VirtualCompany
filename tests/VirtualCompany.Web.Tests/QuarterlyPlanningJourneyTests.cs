using System.Net;
using System.Net.Http.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class QuarterlyPlanningJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), Goal = Guid.NewGuid(), Owner = Guid.NewGuid(), Review = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static PlanningPeriod Period => new(2026, 3, Start, Start.AddMonths(3), "UTC", "SEK", 1, 1, 1);
    private static QuarterReviewDocument Document()
    {
        var objective = new QuarterObjectiveInput(Goal, 1, Owner, 0, 10, "cases", "at_least", [], [new("Review capacity", Start.AddDays(2), "planned")], []);
        var input = new PreviewQuarterReview(2026, 3, [objective], [], "Reviewed outlook");
        return new(new(Review, Company, 2026, 3, 1, null, Owner, Start, input.Notes), new(Company, Period, input,
            [new(objective, "Recorded company objective", "Accountable member", null, null, "Evidence unavailable", "Protected or incomplete evidence", [], ["Cross-plan dependency blocked"] )], [], new string('A', 64)), ["Initial reviewed revision"]);
    }
    private static QuarterlyPlanningOptions Options => new(Company, Period, [], [new(Owner, "Accountable member")], [], []);
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => action(r, ct); }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static TestContext Context(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action)
    { var c = new TestContext().AddVirtualCompanyWebPresentationServices(); var h = new HttpClient(new Handler(action)) { BaseAddress = new("http://localhost/") }; c.Services.AddSingleton(new QuarterlyPlanningApiClient(new CompanyApiTransport(h))); return c; }
    private static HttpResponseMessage Route(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("options") ? Json(Options)
        : r.RequestUri.AbsolutePath.EndsWith("open") ? Json(Document()) : Json(new[] { Document().Summary });
    [Fact] public void Reopened_review_distinguishes_unavailable_actuals_and_preserves_fiscal_evidence_context()
    { using var c = Context((r, _) => Task.FromResult(Route(r))); var cut = c.RenderComponent<QuarterlyPlanningWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.FiscalYear, 2026).Add(x => x.Quarter, 3)); cut.WaitForAssertion(() => Assert.Contains("Recorded company objective", cut.Markup)); Assert.Contains("Evidence unavailable", cut.Markup); Assert.Contains("Cross-plan dependency blocked", cut.Markup); Assert.Contains("review=", cut.Markup); Assert.DoesNotContain("Missed target", cut.Markup); }
    [Fact] public void Restricted_reload_clears_prior_targets_and_edit_actions()
    { var denied = false; using var c = Context((r, _) => Task.FromResult(denied ? new(HttpStatusCode.Forbidden) : Route(r))); var cut = c.RenderComponent<QuarterlyPlanningWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.FiscalYear, 2026).Add(x => x.Quarter, 3)); cut.WaitForAssertion(() => Assert.Contains("Recorded company objective", cut.Markup)); denied = true; cut.FindAll("button").Single(x => x.TextContent == "Reload").Click(); cut.WaitForAssertion(() => Assert.Contains("manager access", cut.Markup)); Assert.DoesNotContain("Recorded company objective", cut.Markup); Assert.DoesNotContain("Propose a new revision", cut.Markup); }
    [Fact] public async Task Late_old_company_options_cannot_restore_prior_targets()
    { var pending = new TaskCompletionSource<HttpResponseMessage>(); var count = 0; using var c = Context((_, _) => ++count == 1 ? pending.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))); var cut = c.RenderComponent<QuarterlyPlanningWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.FiscalYear, 2026).Add(x => x.Quarter, 3)); await cut.InvokeAsync(() => cut.SetParametersAndRender(p => p.Add(x => x.CompanyId, Guid.NewGuid()))); pending.SetResult(Json(Options)); cut.WaitForAssertion(() => Assert.Contains("manager access", cut.Markup)); Assert.DoesNotContain("Recorded company objective", cut.Markup); }
    [Fact] public async Task Wrong_company_payload_and_offline_planning_fail_closed()
    { using var c = Context((_, _) => Task.FromResult(Json(Options with { CompanyId = Guid.NewGuid() }))); await Assert.ThrowsAsync<InvalidDataException>(() => c.Services.GetRequiredService<QuarterlyPlanningApiClient>().Options(Company, 2026, 3, default)); await Assert.ThrowsAsync<InvalidOperationException>(() => new QuarterlyPlanningApiClient(new CompanyApiTransport(new HttpClient()), true).Options(Company, 2026, 3, default)); }
    [Theory] [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(422)] [InlineData(503)] public async Task Typed_errors_clear_planning_commands(int status)
    { using var c = Context((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))); var api = c.Services.GetRequiredService<QuarterlyPlanningApiClient>(); if (status == 403) await Assert.ThrowsAsync<TodayWorkspaceAccessException>(() => api.Options(Company, 2026, 3, default)); else await Assert.ThrowsAsync<InvalidOperationException>(() => api.Options(Company, 2026, 3, default)); }
}
