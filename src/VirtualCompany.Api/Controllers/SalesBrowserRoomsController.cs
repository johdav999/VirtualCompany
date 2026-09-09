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
public sealed class SalesBrowserRoomsController(ISalesBrowserRoomService rooms, ICompanyContextAccessor context) : ControllerBase
{
    private Guid Company => context.CompanyId ?? throw new SalesRoomAccessException("company_required", 403);
    private Guid Actor => context.UserId ?? throw new SalesRoomAccessException("member_required", 403);
    [HttpPost("meetings/{meeting:guid}")]
    public async Task<IActionResult> Create(Guid meeting, CreateSalesBrowserRoom request, CancellationToken ct) => Accepted(await rooms.CreateAsync(Company, Actor, meeting, request, ct));
    [HttpGet("{room:guid}"), HttpGet("{room:guid}/lobby")]
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
}
