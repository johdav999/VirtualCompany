using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/operational")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesOperationalReportsController(ISalesOperationalReportService reports, ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet("opportunities")]
    public Task<ActionResult<SalesOpportunityReport>> Opportunities(bool forecast = false, int days = 30, string? currency = null, Guid? stageId = null, CancellationToken cancellationToken = default) =>
        Read(() => reports.GetOpportunitiesAsync(CompanyId, forecast, days, currency, stageId, cancellationToken));
    [HttpGet("activities")]
    public Task<ActionResult<SalesActivityReport>> Activities(string status = "overdue", Guid? dealId = null, CancellationToken cancellationToken = default) =>
        Read(() => reports.GetActivitiesAsync(CompanyId, status, dealId, cancellationToken));
    [HttpPost("deals/{dealId:guid}/commitments")]
    public Task<ActionResult<SalesCommitmentDto>> Record(Guid dealId, RecordSalesCommitment request, CancellationToken cancellationToken) =>
        Command(() => reports.RecordCommitmentAsync(CompanyId, context.UserId ?? throw new UnauthorizedAccessException(), dealId, request, cancellationToken));
    [HttpPost("activities/{activityId:guid}/review")]
    public Task<ActionResult<SalesCommitmentDto>> Review(Guid activityId, ReviewSalesCommitment request, CancellationToken cancellationToken) =>
        Command(() => reports.ReviewCommitmentAsync(CompanyId, context.UserId ?? throw new UnauthorizedAccessException(), activityId, request, cancellationToken));

    private Guid CompanyId => context.CompanyId ?? throw new UnauthorizedAccessException();
    private async Task<ActionResult<T>> Read<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Check the report filters.", detail: ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
    private async Task<ActionResult<T>> Command<T>(Func<Task<T?>> action) where T : class
    {
        try { var result = await action(); return result is null ? NotFound() : Ok(result); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Check the commitment.", detail: ex.Message); }
        catch (InvalidOperationException ex) { return Problem(statusCode: 409, title: "Reload the activity history.", detail: ex.Message); }
    }
}
