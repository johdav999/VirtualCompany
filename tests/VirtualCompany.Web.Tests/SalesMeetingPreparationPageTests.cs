using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed partial class SalesMeetingPreparationPageTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvitationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Loading_then_ready_surface_shows_invitation_context_and_defaults_to_alex()
    {
        var handler = new PreparationHandler { HoldFirstPreparationResponse = true };
        using var context = CreateContext(handler);
        var cut = Render(context);

        Assert.Contains("Loading presentation preparation", cut.Markup);
        handler.ReleasePreparation();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Welheld discovery", cut.Markup);
            Assert.Contains("Northstar AB", cut.Markup);
            Assert.Contains("Alex", cut.Markup);
            Assert.Equal(3, cut.FindAll(".meeting-readiness__list li").Count);
        });
    }

    [Fact]
    public void Valid_creation_uses_standard_retention_and_remains_in_preparation_workspace()
    {
        var handler = new PreparationHandler();
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.WaitForElement("#meeting-goal");

        cut.Find("#meeting-goal").Change("Confirm the operational fit");
        cut.Find("#meeting-audience").Change("Operations leadership");
        cut.Find("#meeting-demo").Change("Show the weekly review");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(1, handler.PutCalls));
        Assert.NotNull(handler.LastRequest);
        Assert.Null(handler.LastRequest!.ExpectedVersion);
        Assert.Equal("standard", handler.LastRequest.RetentionPolicy);
        Assert.Equal(365, handler.LastRequest.RetentionDays);
        Assert.Equal("not_requested", handler.LastRequest.ConsentStatus);
        Assert.Contains("Meeting session created", cut.Markup);
        Assert.Contains($"/app/sales/meeting-invitations/{InvitationId:D}/prepare", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Existing_session_is_prefilled_and_saved_with_expected_version()
    {
        var handler = new PreparationHandler { Session = CreateSession(4, "Review current workflow") };
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.WaitForElement("#meeting-goal");

        Assert.Equal("Review current workflow", cut.Find("#meeting-goal").GetAttribute("value") ?? cut.Find("#meeting-goal").TextContent);
        cut.Find("#meeting-goal").Change("Agree the rollout path");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(1, handler.PutCalls));
        Assert.Equal(4, handler.LastRequest!.ExpectedVersion);
        Assert.Equal("Agree the rollout path", handler.LastRequest.MeetingGoal);
        Assert.Contains("Meeting session updated", cut.Markup);
    }

    [Fact]
    public void Invalid_duration_is_rejected_without_a_request()
    {
        var handler = new PreparationHandler();
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.WaitForElement("#meeting-goal");

        cut.Find("#meeting-goal").Change("Confirm fit");
        cut.Find("#meeting-audience").Change("Decision makers");
        cut.Find("#meeting-duration").Change("4");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Duration must be between 5 and 480 minutes", cut.Markup));
        Assert.Equal(0, handler.PutCalls);
    }

    [Fact]
    public void Stale_update_reloads_authoritative_session_and_explains_the_conflict()
    {
        var handler = new PreparationHandler
        {
            Session = CreateSession(2, "Original goal"),
            ConflictOnPut = true
        };
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.WaitForElement("#meeting-goal");

        cut.Find("#meeting-goal").Change("My stale edit");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Someone updated this session while you were editing", cut.Markup);
            Assert.Equal("Authoritative goal", cut.Find("#meeting-goal").GetAttribute("value"));
        });
        Assert.Equal(2, handler.LastRequest!.ExpectedVersion);
        Assert.True(handler.PreparationCalls >= 2);
    }

    [Fact]
    public void Invalid_relationship_conflict_shows_the_real_prerequisite_and_preserves_form_values()
    {
        var handler = new PreparationHandler { InvalidRelationshipOnPut = true };
        using var context = CreateContext(handler);
        var cut = Render(context);
        cut.WaitForElement("#meeting-goal");

        cut.Find("#meeting-goal").Change("Confirm the operational fit");
        cut.Find("#meeting-audience").Change("Operations leadership");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Associate the meeting lead, deal, or contact with a customer company", cut.Markup);
            Assert.Equal("Confirm the operational fit", cut.Find("#meeting-goal").GetAttribute("value"));
            Assert.DoesNotContain("Someone updated this session", cut.Markup);
            Assert.DoesNotContain("Reload latest session", cut.Markup);
        });
        Assert.Equal(1, handler.PreparationCalls);
    }

    [Fact]
    public void Backend_readiness_blocks_controls_and_preserves_the_explanation()
    {
        var handler = new PreparationHandler { Blocked = true };
        using var context = CreateContext(handler);
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Contains("Approve and schedule the invitation first.", cut.Markup));
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Equal(0, handler.PutCalls);
    }

    [Fact]
    public void Waiting_invitation_exposes_the_approval_next_step()
    {
        var handler = new PreparationHandler { Blocked = true };
        using var context = CreateContext(handler);
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            var link = cut.FindAll("a").Single(x =>
                x.TextContent.Contains("Return to lead and review approval", StringComparison.Ordinal));
            Assert.Equal(
                $"/app/sales/leads/55555555-5555-5555-5555-555555555555?companyId={CompanyId:D}",
                link.GetAttribute("href"));
        });
    }

    [Fact]
    public void Authorization_failure_is_rendered_as_a_safe_retryable_error()
    {
        var handler = new PreparationHandler { Forbidden = true };
        using var context = CreateContext(handler);
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Contains("You cannot prepare this invitation.", cut.Markup));
        Assert.Contains("Presentation preparation is unavailable", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll("button"));
    }

    [Fact]
    public void Ready_meeting_with_browser_diagnostics_exposes_exact_private_presenter_link()
    {
        var handler = new PreparationHandler { Session = CreateSession(2, "Confirm fit"), Ready = true };
        using var context = CreateContext(handler, browserDiagnostics: true);
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Final readiness review", cut.Markup);
            Assert.Contains("Active · 3 slide", cut.Markup);
            Assert.Contains("does not join Teams", cut.Markup);
            var link = cut.FindAll("a").Single(x => x.TextContent.Contains("Open browser presenter", StringComparison.Ordinal));
            Assert.Equal($"/teams/meetings/{handler.Session!.Id:D}/side-panel?companyId={CompanyId:D}", link.GetAttribute("href"));
        });
    }

    [Fact]
    public void Diagnostics_disabled_omits_browser_launch_and_explains_teams_entry()
    {
        var handler = new PreparationHandler { Session = CreateSession(2, "Confirm fit"), Ready = true };
        using var context = CreateContext(handler, browserDiagnostics: false);
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Open presenter controls through Teams", cut.Markup);
            Assert.Contains("installed Virtual Company Teams meeting application", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("a"), x => x.TextContent.Contains("Open browser presenter", StringComparison.Ordinal));
        });
    }

    private static TestContext CreateContext(PreparationHandler handler, bool browserDiagnostics = false)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var baseClient = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(new OnboardingApiClient(baseClient, useOfflineMode: true));
        context.Services.AddSingleton(new SalesApiClient(baseClient, useOfflineMode: true));
        context.Services.AddSingleton(serviceProvider => new SalesPresentationPreparationApiClient(
            transport, false, serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(serviceProvider => new SalesMeetingSessionApiClient(
            transport, false, serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(new SalesPresentationDeckApiClient(transport, false));
        context.Services.AddSingleton(new SalesBrowserMeetingApiClient(transport,false));
        context.Services.AddSingleton(new TeamsCallControlApiClient(transport, false));
        context.Services.AddSingleton(new SalesPresentationPreparationTelemetry());
        context.Services.AddSingleton<IOptions<TeamsMeetingUiOptions>>(
            Options.Create(new TeamsMeetingUiOptions { BrowserDiagnosticsEnabled = browserDiagnostics }));
        return context;
    }

    private static IRenderedComponent<SalesMeetingPreparation> Render(TestContext context)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"/app/sales/meeting-invitations/{InvitationId:D}/prepare?companyId={CompanyId:D}");
        return context.RenderComponent<SalesMeetingPreparation>(parameters => parameters
            .Add(x => x.InvitationId, InvitationId));
    }

    private static SalesMeetingSessionViewModel CreateSession(long version, string goal) => new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"), CompanyId, InvitationId,
        Guid.Parse("55555555-5555-5555-5555-555555555555"), null, null,
        Guid.Parse("66666666-6666-6666-6666-666666666666"), goal, "Operations leadership", 45,
        null, "provider-event", "draft", 0, 0, null, "not_requested", null, null,
        "standard", 365, DateTime.UtcNow, DateTime.UtcNow.AddDays(365), null, null,
        Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, version);

    private sealed class PreparationHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldFirstPreparationResponse { get; init; }
        public bool ConflictOnPut { get; init; }
        public bool InvalidRelationshipOnPut { get; init; }
        public bool Blocked { get; init; }
        public bool Forbidden { get; init; }
        public bool Ready { get; init; }
        public string Conferencing {get;init;}="teams";
        public int PreparationCalls { get; private set; }
        public int PutCalls { get; private set; }
        public CreateOrUpdateSalesMeetingSessionViewModel? LastRequest { get; private set; }
        public SalesMeetingSessionViewModel? Session { get; set; }

        public void ReleasePreparation() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/presentation-preparation", StringComparison.Ordinal))
            {
                PreparationCalls++;
                if (HoldFirstPreparationResponse && PreparationCalls == 1)
                    await _gate.Task.WaitAsync(cancellationToken);
                if (Forbidden)
                    return Problem(HttpStatusCode.Forbidden, "You cannot prepare this invitation.");
                return Json(CreatePreparation());
            }

            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/session", StringComparison.Ordinal))
            {
                PutCalls++;
                LastRequest = await request.Content!.ReadFromJsonAsync<CreateOrUpdateSalesMeetingSessionViewModel>(cancellationToken: cancellationToken);
                if (ConflictOnPut)
                {
                    Session = CreateSession(3, "Authoritative goal");
                    return Problem(HttpStatusCode.Conflict, "The session was updated by another user.", "sales.meeting_session.conflict");
                }
                if (InvalidRelationshipOnPut)
                {
                    return Problem(
                        HttpStatusCode.Conflict,
                        "Associate the meeting lead, deal, or contact with a customer company before creating a session.",
                        "sales.meeting_session.invalid_relationship");
                }

                Session = CreateSession((LastRequest!.ExpectedVersion ?? 0) + 1, LastRequest.MeetingGoal) with
                {
                    IntendedAudience = LastRequest.IntendedAudience,
                    DemoScenario = LastRequest.DemoScenario,
                    ConsentStatus = LastRequest.ConsentStatus,
                    RetentionPolicy = LastRequest.RetentionPolicy,
                    RetentionDays = LastRequest.RetentionDays
                };
                return Json(Session);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/presenter", StringComparison.Ordinal))
                return Json(new TeamsMeetingPresenterViewModel(null, Session?.ConcurrencyVersion ?? 1, []));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private SalesPresentationPreparationViewModel CreatePreparation()
        {
            var invitation = new SalesPresentationPreparationInvitationViewModel(
                InvitationId, Guid.Parse("55555555-5555-5555-5555-555555555555"), null, null,
                "Welheld discovery", new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 15, 9, 45, 0, DateTimeKind.Utc), "Europe/Stockholm", null,
                Conferencing!="browser", "microsoft365", Blocked ? "waiting_for_approval" : "scheduled", !Blocked, !Blocked,Conferencing);
            var agents = Blocked
                ? Array.Empty<SalesPresentationPreparationAgentViewModel>()
                : [new SalesPresentationPreparationAgentViewModel(AgentId, "Alex", "Sales representative", "alex-sales", "Sales", "active", null)];
            var deck = Ready && Session is not null
                ? new SalesPresentationPreparationDeckViewModel(
                    Guid.Parse("77777777-7777-7777-7777-777777777777"), AgentId, 1,
                    "Welheld", "wellheld-overview.pptx", "processed", 1, 1, 3,
                    null, null, false, true, DateTime.UtcNow, DateTime.UtcNow,
                    DateTime.UtcNow, null, DateTime.UtcNow, 1)
                : null;
            return new SalesPresentationPreparationViewModel(
                CompanyId, InvitationId, invitation.LeadId, "Welheld opportunity", "Northstar AB",
                Session?.Id, 26_214_400, invitation, Session, agents, deck is null ? [] : [deck], deck?.Id, deck?.SlideCount ?? 0,
                Blocked ? "blocked" : Ready ? "ready" : Session is null ? "session_required" : "deck_required", Ready,
                Blocked ? [new SalesPresentationPreparationBlockerViewModel("invitation_not_ready", "Approve and schedule the invitation first.")] : [],
                Blocked ? [] : Ready ? ["update_session", "upload_deck", "open_presenter"] : [Session is null ? "create_session" : "update_session"]);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };

        private static HttpResponseMessage Problem(HttpStatusCode status, string detail, string? code = null) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                status = (int)status,
                title = status.ToString(),
                detail,
                code
            }), System.Text.Encoding.UTF8, "application/problem+json")
        };
    }
}
