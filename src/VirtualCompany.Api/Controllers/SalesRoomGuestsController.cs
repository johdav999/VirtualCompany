using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Api.Controllers;
[ApiController, Route("api/sales/browser-room-guests"), AllowAnonymous, RequestSizeLimit(4096)]
[TypeFilter(typeof(SalesRoomApiFilter)), EnableRateLimiting("sales-room-access")]
public sealed class SalesRoomGuestsController(ISalesBrowserRoomService rooms) : ControllerBase
{
    private string Credential => Request.Headers["X-Sales-Room-Session"].Count == 1 ? Request.Headers["X-Sales-Room-Session"].ToString() : "";
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem(RedeemSalesRoomInvitation request, CancellationToken ct) => Ok(await rooms.RedeemAsync(request, ct));
    [HttpGet("{room:guid}")]
    public async Task<IActionResult> Status(Guid room, CancellationToken ct) => Ok(await rooms.GuestStatusAsync(Credential, room, ct));
    [HttpPost("{room:guid}/media-token")]
    public async Task<IActionResult> Token(Guid room, CancellationToken ct) => Ok(await rooms.GuestTokenAsync(Credential, room, ct));
    [HttpPost("{room:guid}/consent")]
    public async Task<IActionResult> Consent(Guid room, SetSalesRoomConsent request, CancellationToken ct) => Ok(await rooms.ConsentAsync(Credential, room, request, ct));
    [HttpPost("{room:guid}/leave")]
    public async Task<IActionResult> Leave(Guid room, CancellationToken ct) { await rooms.LeaveAsync(Credential, room, ct); return Accepted(); }
}
[ApiController, Route("api/sales/browser-room-provider/livekit"), AllowAnonymous, RequestSizeLimit(65536)]
[TypeFilter(typeof(SalesRoomApiFilter))]
public sealed class SalesRoomProviderController(ISalesBrowserRoomService rooms) : ControllerBase
{
    [HttpPost, Consumes("application/webhook+json", "application/json")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        await rooms.AcceptWebhookAsync(await reader.ReadToEndAsync(ct), Request.Headers.Authorization.ToString(), ct);
        return NoContent();
    }
}
