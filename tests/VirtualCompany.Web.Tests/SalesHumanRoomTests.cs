using VirtualCompany.Api.Tests;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class SalesHumanRoomTests
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    [Fact]
    public async Task Host_client_reads_actual_API_contract_and_keeps_company_scope()
    {
        var participant = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(Company.ToString(), Assert.Single(request.Headers.GetValues("X-Company-Id")));
            Assert.False(request.Headers.Contains("X-Sales-Room-Session"));
            Assert.EndsWith($"/browser-rooms/{Room:D}", request.RequestUri!.AbsolutePath);
            return Ok(new SalesBrowserRoomView(Room, null, "lobby", "not_started", 2, DateTime.UtcNow.AddHours(1),
                [new(participant, "Host", "admitted", 1, false, "human-host-1", true)], []));
        })) { BaseAddress = new("https://api.example.test/") };
        var client = new SalesBrowserRoomApiClient(new CompanyApiTransport(http), false);
        var view = await client.StatusAsync(Company, Room, default);
        Assert.True(Assert.Single(view.Participants).IsOrganizer);
        await Assert.ThrowsAsync<ArgumentException>(() => client.StatusAsync(Guid.Empty, Room, default));
    }
    [Fact]
    public async Task Guest_client_sends_only_room_credential_and_reads_public_contract()
    {
        using var client = new SalesBrowserGuestApiClient(new HttpClient(new Handler(request =>
        {
            Assert.Equal("credential", Assert.Single(request.Headers.GetValues("X-Sales-Room-Session")));
            Assert.False(request.Headers.Contains("X-Company-Id")); Assert.Null(request.Headers.Authorization);
            return Ok(new SalesRoomGuestView(Room, Guid.NewGuid(), "lobby", "lobby", 1, DateTime.UtcNow.AddHours(1), false, false));
        })) { BaseAddress = new("https://api.example.test/") });
        var view = await client.StatusAsync(Room, "credential", default); Assert.Empty(view.Participants);
    }
    [Fact]
    public async Task Provider_body_is_never_shown_in_failure_message_and_tokens_are_redacted()
    {
        using var client = new SalesBrowserGuestApiClient(new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden)
        { Content = JsonContent.Create(new { code = "private-provider-secret", detail = "private-provider-secret" }) })) { BaseAddress = new("https://api.example.test/") });
        var error = await Assert.ThrowsAsync<BrowserRoomRequestException>(() => client.StatusAsync(Room, "credential", default));
        Assert.DoesNotContain("private-provider-secret", error.Message);
        Assert.DoesNotContain("secret-token", new BrowserRoomToken("https://example.test", "secret-token", "human", DateTimeOffset.UtcNow).ToString());
    }
    [Fact]
    public void Guest_prejoin_has_no_internal_navigation_or_host_controls_and_never_requests_media()
    {
        using var context = Context(_ => throw new Exception("No backend access before redemption"));
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        module.Setup<SalesHumanRoom.Entry>("entry", _ => true).SetResult(new(new string('x', 43), ""));
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room));
        cut.WaitForAssertion(() => Assert.Contains("Ask to join", cut.Markup));
        Assert.DoesNotContain("Private host controls", cut.Markup); Assert.Empty(cut.FindAll("nav"));
        Assert.Contains("AI audio processing starts only after everyone consents", cut.Markup); Assert.Contains("not recorded", cut.Markup);
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "connect"); Export("guest-prejoin", cut.Markup);
    }
    [Fact]
    public void Lobby_and_access_denial_do_not_connect_media()
    {
        using var context = Context(_ => Ok(new SalesRoomGuestView(Room, Guid.NewGuid(), "lobby", "lobby", 1, DateTime.UtcNow.AddHours(1), false, false)));
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        module.Setup<SalesHumanRoom.Entry>("entry", _ => true).SetResult(new("", "credential"));
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room));
        cut.WaitForAssertion(() => Assert.Contains("Waiting for the host", cut.Markup));
        Assert.True(cut.Find(".room-join").HasAttribute("disabled"));
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "connect"); Export("guest-lobby", cut.Markup);
    }
    [Fact]
    public void Host_sees_private_lobby_and_versioned_admission_control()
    {
        var pending = Guid.NewGuid(); bool admitted = false;
        using var context = Context(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith($"/participants/{pending:D}/admit", request.RequestUri!.AbsolutePath);
                var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.Equal(5, json.RootElement.GetProperty("expectedVersion").GetInt64()); admitted = true;
            }
            return Ok(new SalesBrowserRoomView(Room, null, "lobby", "not_started", admitted ? 6 : 5, DateTime.UtcNow.AddHours(1),
                [new(Guid.NewGuid(), "Organizer", "admitted", 1, false, "human-host-1", true), new(pending, "Casey Wang", admitted ? "admitted" : "lobby", 1, false, "human-guest-1")], []));
        });
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room).Add(x => x.HostCompanyId, Company));
        cut.WaitForAssertion(() => Assert.Contains("Private host controls", cut.Markup)); Export("host-lobby", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent == "Admit").Click();
        cut.WaitForAssertion(() => Assert.True(admitted)); Assert.Contains("Remove", cut.Markup);
    }
    [Fact]
    public void Host_agent_panel_shows_consent_health_private_evidence_and_honest_billing_state()
    {
        var presentation = PresentationFixture();
        var hostParticipant = Guid.NewGuid();
        var guestParticipant = Guid.NewGuid();
        var question = Guid.NewGuid();
        var room = new BrowserRoomSnapshot(Room, Guid.NewGuid(), "live", "paused", 5, DateTime.UtcNow.AddHours(1),
            [new(hostParticipant, "Organizer", "admitted", 1, true, "human-host-1", true, true, true),
             new(guestParticipant, "Casey Wang", "admitted", 1, true, "human-guest-1", false, true, false)], []);
        var status = new BrowserRoomAgentStatus(Room, Guid.NewGuid(), "Nora", "paused", "degraded", 3, 9,
            2, 2, true, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddSeconds(20), Guid.NewGuid(), Guid.NewGuid(), "ready",
            8_000, 4_200, 5_600, null, "provider_duration_not_reported", 2_400, 31, 5, 0.0132m,
            "transcription_unavailable", "Speech transcription failed. AI is paused; use typed questions while the human call continues.", 5,
            new(question, "What supports the delivery date?", "The approved implementation plan and signed scope support it.",
                "completed", "private_host", 2, [new("scope-1", "document", "Signed implementation scope")]), [],
            new("pending", "Casey Wang", guestParticipant, "assisted", Guid.NewGuid(), guestParticipant,
                "Casey Wang", question, "confirmation_required", true, false, 9, 4, 9, 2, 1,
                "slide:2:talking-point:1", 1420, null, 7,
                new(Guid.NewGuid(), "waiting", 2, 1, DateTime.UtcNow.AddMilliseconds(-320),
                    DateTime.UtcNow.AddMilliseconds(430), 320)));
        using var context = Context(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/agent")) return Ok(status);
            if (path.EndsWith("/media-token"))
                return Ok(new SalesRoomMediaToken("wss://test.example", "synthetic", "human-host-1", DateTimeOffset.UtcNow.AddMinutes(2)));
            if (path.Contains("/presentation/decks/")) return SlideImage();
            if (path.EndsWith("/presentation")) return Ok(presentation.Host);
            return Ok(room);
        });
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;

        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room).Add(x => x.HostCompanyId, Company));

        cut.WaitForAssertion(() => Assert.Contains("Nora", cut.Markup));
        cut.Find(".room-join").Click();
        cut.WaitForAssertion(() => Assert.Contains("Audience readiness", cut.Markup));
        Assert.Contains("2 of 2 consented", cut.Markup);
        Assert.Contains("Voice unavailable", cut.Markup);
        Assert.Contains("Signed implementation scope", cut.Markup);
        Assert.Contains("Provider billed", cut.Markup);
        Assert.Contains("Not reported", cut.Markup);
        Assert.Contains("Human call and manual slides stay available", cut.Markup);
        Assert.Contains("No savings claim is made without provider billing evidence", cut.Markup);
        Assert.Contains("Floor: Casey Wang", cut.Markup);
        Assert.Contains("Needs host confirmation", cut.Markup);
        Assert.Contains("Approve answer", cut.Markup);
        Assert.Contains("1 of 2 clients acknowledged", cut.Markup);
        Export("host-agent", cut.Markup);
    }
    [Fact]
    public void Denied_session_stops_media_and_clears_browser_credential()
    {
        using var context = Context(_ => new(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { code = "guest_session_invalid" }) });
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        module.Setup<SalesHumanRoom.Entry>("entry", _ => true).SetResult(new("", "credential"));
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room));
        cut.WaitForAssertion(() => Assert.Contains("denied or removed", cut.Markup));
        Assert.Contains(module.Invocations, x => x.Identifier == "disconnect"); Assert.Contains(module.Invocations, x => x.Identifier == "forgetSession");
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "connect");
    }
    [Fact]
    public async Task Host_join_uses_authorized_token_and_renders_live_controls()
    {
        using var context = Context(request => request.RequestUri!.AbsolutePath.EndsWith("/media-token")
            ? Ok(new SalesRoomMediaToken("wss://test.example", "synthetic", "human-host-1", DateTimeOffset.UtcNow.AddMinutes(2)))
            : Ok(new SalesBrowserRoomView(Room, null, "live", "not_started", 5, DateTime.UtcNow.AddHours(1),
                [new(Guid.NewGuid(), "Organizer", "admitted", 1, true, "human-host-1", true), new(Guid.NewGuid(), "Casey Wang", "admitted", 1, true, "human-guest-1")], [])));
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room).Add(x => x.HostCompanyId, Company));
        cut.WaitForAssertion(() => Assert.False(cut.Find(".room-join").HasAttribute("disabled")));
        cut.Find(".room-join").Click();
        cut.WaitForAssertion(() => Assert.Contains(module.Invocations, x => x.Identifier == "connect"));
        await cut.InvokeAsync(() => cut.Instance.MediaChanged(new("connected", "", false, false, false, true)));
        Assert.False(cut.Find("[data-participants]").HasAttribute("hidden")); Export("host-live", cut.Markup);
    }
    [Fact]
    public void Host_renders_shared_slide_private_notes_modes_and_audience_readiness()
    {
        var fixture = PresentationFixture();
        using var context = Context(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/media-token"))
                return Ok(new SalesRoomMediaToken("wss://test.example", "synthetic", "human-host-1", DateTimeOffset.UtcNow.AddMinutes(2)));
            if (path.Contains("/presentation/decks/")) return SlideImage();
            if (path.EndsWith("/presentation")) return Ok(fixture.Host);
            return Ok(fixture.Room);
        });
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room).Add(x => x.HostCompanyId, Company));
        cut.WaitForAssertion(() => Assert.False(cut.Find(".room-join").HasAttribute("disabled")));
        cut.Find(".room-join").Click();
        cut.WaitForAssertion(() => Assert.Contains("Shared presentation", cut.Markup));
        Assert.Contains("Confidential host note", cut.Markup);
        Assert.Contains("Only you can see this", cut.Markup);
        Assert.Contains("1 of 2 rendered", cut.Markup);
        Assert.Equal(3, cut.FindAll(".room-mode button").Count);
        Export("host-presentation", cut.Markup);
    }
    [Fact]
    public void Guest_renders_same_slide_without_private_notes_or_host_controls()
    {
        var fixture = PresentationFixture();
        using var context = Context(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("credential", Assert.Single(request.Headers.GetValues("X-Sales-Room-Session")));
            if (path.EndsWith("/media-token"))
                return Ok(new SalesRoomMediaToken("wss://test.example", "synthetic", "human-guest-1", DateTimeOffset.UtcNow.AddMinutes(2)));
            if (path.Contains("/presentation/decks/")) return SlideImage();
            if (path.EndsWith("/presentation")) return Ok(fixture.GuestPresentation);
            return Ok(fixture.GuestRoom);
        });
        var module = context.JSInterop.SetupModule("./js/sales-human-room.mjs"); module.Mode = JSRuntimeMode.Loose;
        module.Setup<SalesHumanRoom.Entry>("entry", _ => true).SetResult(new("", "credential"));
        var cut = context.RenderComponent<SalesHumanRoom>(p => p.Add(x => x.RoomId, Room));
        cut.WaitForAssertion(() => Assert.False(cut.Find(".room-join").HasAttribute("disabled")));
        cut.Find(".room-join").Click();
        cut.WaitForAssertion(() => Assert.Contains("Shared presentation", cut.Markup));
        Assert.Contains("Approved customer slide", cut.Markup);
        Assert.DoesNotContain("Confidential host note", cut.Markup);
        Assert.DoesNotContain("Only you can see this", cut.Markup);
        Assert.Empty(cut.FindAll("[aria-label='Private presentation controls']"));
        Export("guest-presentation", cut.Markup);
    }

    private static (BrowserRoomSnapshot Room, BrowserGuestSnapshot GuestRoom,
        SalesBrowserPresentationHostViewModel Host, SalesBrowserPresentationPublicViewModel GuestPresentation) PresentationFixture()
    {
        var session = Guid.NewGuid(); var deck = Guid.NewGuid(); var hostParticipant = Guid.NewGuid(); var guestParticipant = Guid.NewGuid();
        var stage = new SalesPresentationStageSnapshotViewModel(session, "active", 4, 9, deck, 2, 1, 3,
            "Approved customer slide", "Customer-safe text", null, 1600, 900);
        var privateStage = new SalesPresentationPrivateSnapshotViewModel(stage, "manual", 0, null,
            "Confidential host note", "Confirm the next step", 45, "Move to the commercial plan", []);
        var readiness = new SalesBrowserPresentationReadinessViewModel(Room, deck, 2, 1, 4, 9,
            DateTime.UtcNow.AddSeconds(3), false,
            [new(hostParticipant, "Organizer", true, "rendered", DateTime.UtcNow),
             new(guestParticipant, "Casey Wang", true, "pending", null)]);
        var room = new BrowserRoomSnapshot(Room, session, "live", "not_started", 5, DateTime.UtcNow.AddHours(1),
            [new(hostParticipant, "Organizer", "admitted", 1, true, "human-host-1", true, true, true),
             new(guestParticipant, "Casey Wang", "admitted", 1, true, "human-guest-1", false, true, false)], [])
            { InvitationId = Guid.NewGuid() };
        var guestRoom = new BrowserGuestSnapshot(Room, guestParticipant, "live", "admitted", 1,
            DateTime.UtcNow.AddHours(1), false, false,
            [new(hostParticipant, "Organizer", "human-host-1"), new(guestParticipant, "Casey Wang", "human-guest-1")]);
        return (room, guestRoom,
            new(Room, hostParticipant, 1, new(stage, privateStage), readiness),
            new(Room, guestParticipant, 1, stage));
    }
    private static HttpResponseMessage SlideImage() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1600 900'><rect width='1600' height='900' fill='#123b34'/><text x='100' y='170' fill='white' font-size='64' font-family='sans-serif'>Approved customer slide</text></svg>",
            System.Text.Encoding.UTF8, "image/svg+xml")
    };
    private static TestContext Context(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new SalesBrowserRoomApiClient(new CompanyApiTransport(new HttpClient(new Handler(respond)) { BaseAddress = new("https://api.example.test/") }), false));
        context.Services.AddSingleton(new SalesBrowserGuestApiClient(new HttpClient(new Handler(respond)) { BaseAddress = new("https://api.example.test/") }));
        return context;
    }
    private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static void Export(string name, string markup)
    { var path = Environment.GetEnvironmentVariable("VC_BROWSER_UAT_DIRECTORY"); if (path is not null) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name + ".html"), markup); } }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}


