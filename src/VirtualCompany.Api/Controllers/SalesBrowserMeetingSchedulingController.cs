using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;
[ApiController, Route("api/sales/browser-meeting-scheduling"), Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
[TypeFilter(typeof(SalesRoomApiFilter))]
public sealed class SalesBrowserMeetingSchedulingController(ISalesBrowserMeetingScheduling scheduling, ICompanyContextAccessor context) : ControllerBase
{
    [HttpPost("invitations/{invitation:guid}/changes/{change:guid}/retry")]
    public async Task<IActionResult> RetryChange(Guid invitation,Guid change,CancellationToken ct)
    {await scheduling.RetryChangeAsync(context.CompanyId!.Value,context.UserId??throw new SalesRoomAccessException("member_required",403),invitation,change,ct);return Accepted();}
    [HttpPost("invitations/{invitation:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid invitation, [FromQuery] Guid? change, CancellationToken ct)
    { await scheduling.ReconcileAsync(context.CompanyId!.Value, context.UserId ?? throw new SalesRoomAccessException("member_required", 403), invitation, change, ct); return Accepted(); }
    [HttpGet("readiness")]
    public ActionResult<SalesBrowserMeetingReadiness> Readiness() => Ok(scheduling.Readiness());
    [HttpGet("invitations/{invitation:guid}/link")]
    public async Task<ActionResult<SalesBrowserMeetingLink>> Link(Guid invitation, CancellationToken ct) => Ok(await scheduling.GetLinkAsync(context.CompanyId!.Value, context.UserId ?? throw new SalesRoomAccessException("member_required", 403), invitation, ct));
}
