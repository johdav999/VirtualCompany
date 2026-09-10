using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;
[ApiController, Route("api/sales/browser-rooms"), Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
[TypeFilter(typeof(SalesRoomApiFilter)), EnableRateLimiting("sales-room-access")]
public sealed class SalesBrowserRoomsController(
    ISalesBrowserRoomService rooms,
    ISalesBrowserPresentationService presentations,
    ISalesRoomAgentService agents,
    ISalesRoomCaptureService capture,
    ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet("{room:guid}/capture-review")]
    public async Task<IActionResult> CaptureReview(Guid room, CancellationToken ct) =>
        Ok(await capture.GetReviewAsync(Company, Actor, room, ct));

    private Guid Company => context.CompanyId ?? throw new SalesRoomAccessException("company_required", 403);
    private Guid Actor => context.UserId ?? throw new SalesRoomAccessException("member_required", 403);
    [HttpPost("meetings/{meeting:guid}")]
    public async Task<IActionResult> Create(Guid meeting, CreateSalesBrowserRoom request, CancellationToken ct) => Accepted(await rooms.CreateAsync(Company, Actor, meeting, request, ct));
    [HttpGet("{room:guid}"), HttpGet("{room:guid}/lobby"), EnableRateLimiting("sales-room-status")]
    public async Task<IActionResult> Get(Guid room, CancellationToken ct) => Ok(await rooms.GetAsync(Company, Actor, room, ct));
    [HttpPost("{room:guid}/invitations")]
    public async Task<IActionResult> Invite(Guid room, CreateSalesRoomInvitation request, CancellationToken ct) => Ok(await rooms.InviteAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/invitations/{invitation:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid room, Guid invitation, SalesRoomCommand request, CancellationToken ct) => Accepted(await rooms.RevokeInvitationAsync(Company, Actor, room, invitation, request, ct));
    [HttpPost("{room:guid}/participants/{participant:guid}/{decision}")]
    public async Task<IActionResult> Decide(Guid room, Guid participant, string decision, SalesRoomCommand request, CancellationToken ct) => Accepted(await rooms.DecideAsync(Company, Actor, room, participant, decision, request, ct));
    [HttpPost("{room:guid}/end")]
    public async Task<IActionResult> End(Guid room, SalesRoomCommand request, CancellationToken ct) => Accepted(await rooms.EndAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/operations/{operation:guid}/retry")]
    public async Task<IActionResult> Retry(Guid room, Guid operation, SalesRoomCommand request, CancellationToken ct) => Accepted(await rooms.RetryAsync(Company, Actor, room, operation, request, ct));
    [HttpPost("{room:guid}/media-token")]
    public async Task<IActionResult> Token(Guid room, CancellationToken ct) => Ok(await rooms.HostTokenAsync(Company, Actor, room, ct));
    [HttpPost("{room:guid}/consent")]
    public async Task<IActionResult> Consent(Guid room, SetSalesRoomConsent request, CancellationToken ct) => Ok(await rooms.HostConsentAsync(Company, Actor, room, request, ct));
    [HttpGet("{room:guid}/agent"), EnableRateLimiting("sales-room-status")]
    public async Task<IActionResult> Agent(Guid room, CancellationToken ct) => Ok(await agents.GetAsync(Company, Actor, room, ct));
    [HttpPost("{room:guid}/agent/start")]
    public async Task<IActionResult> StartAgent(Guid room, StartSalesRoomAgent request, CancellationToken ct) =>
        Accepted(await agents.StartAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/stop")]
    public async Task<IActionResult> StopAgent(Guid room, StopSalesRoomAgent request, CancellationToken ct) =>
        Accepted(await agents.StopAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/narration")]
    public async Task<IActionResult> InvokeNarration(Guid room, InvokeSalesRoomNarration request, CancellationToken ct) =>
        Accepted(await agents.InvokeNarrationAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/questions")]
    public async Task<IActionResult> AskAgent(Guid room, AskSalesRoomAgent request, CancellationToken ct) =>
        Accepted(await agents.AskAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/answers")]
    public async Task<IActionResult> SpeakAnswer(Guid room, SpeakSalesRoomAnswer request, CancellationToken ct) =>
        Accepted(await agents.SpeakAnswerAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/takeover")]
    public async Task<IActionResult> TakeOver(Guid room, TakeOverSalesRoomAgent request, CancellationToken ct) =>
        Accepted(await agents.TakeOverAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/resume")]
    public async Task<IActionResult> ResumeAgent(Guid room, ResumeSalesRoomAgent request, CancellationToken ct) =>
        Accepted(await agents.ResumeAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/pending-turn/confirm")]
    public async Task<IActionResult> ConfirmPendingTurn(Guid room, ConfirmSalesRoomPendingTurn request, CancellationToken ct) =>
        Accepted(await agents.ConfirmPendingTurnAsync(Company, Actor, room, request, ct));
    [HttpPost("{room:guid}/agent/pending-turn/dismiss")]
    public async Task<IActionResult> DismissPendingTurn(Guid room, DismissSalesRoomPendingTurn request, CancellationToken ct) =>
        Accepted(await agents.DismissPendingTurnAsync(Company, Actor, room, request, ct));
    [HttpPut("{room:guid}/agent/co-host")]
    public async Task<IActionResult> AuthorizeCoHost(Guid room, AuthorizeSalesRoomCoHost request, CancellationToken ct) =>
        Ok(await agents.AuthorizeCoHostAsync(Company, Actor, room, request, ct));
    [HttpGet("{room:guid}/presentation")]
    public async Task<IActionResult> Presentation(Guid room, CancellationToken ct) =>
        Ok(await presentations.GetHostAsync(Company, Actor, room, ct));
    [HttpPost("{room:guid}/presentation/commands/{toolName}")]
    public async Task<IActionResult> PresentationCommand(
        Guid room, string toolName, SalesPresentationCommandRequest request, CancellationToken ct) =>
        Ok(await presentations.ExecuteHostAsync(Company, Actor, room, toolName, request,
            HttpContext.TraceIdentifier, ct));
    [HttpPut("{room:guid}/presentation/control-mode")]
    public async Task<IActionResult> PresentationControlMode(
        Guid room, SetSalesBrowserPresentationControlModeRequest request, CancellationToken ct) =>
        Ok(await presentations.SetControlModeAsync(Company, Actor, room, request,
            HttpContext.TraceIdentifier, ct));
    [HttpPost("{room:guid}/presentation/render-override/{expectedPresentationVersion:long}")]
    public async Task<IActionResult> PresentationOverride(
        Guid room, long expectedPresentationVersion, CancellationToken ct) =>
        Ok(await presentations.OverrideAsync(Company, Actor, room, expectedPresentationVersion, ct));
    [HttpGet("{room:guid}/presentation/decks/{deckId:guid}/versions/{deckVersion:int}/slides/{slideNumber:int}/image")]
    public async Task<IActionResult> PresentationImage(
        Guid room, Guid deckId, int deckVersion, int slideNumber, CancellationToken ct)
    {
        var asset = await presentations.OpenHostSlideAsync(
            Company, Actor, room, deckId, deckVersion, slideNumber, ct);
        Response.Headers.ETag = $"\"{asset.ContentHash}\"";
        Response.Headers.CacheControl = "private, max-age=300";
        return File(asset.Content, asset.ContentType, enableRangeProcessing: false);
    }
}
