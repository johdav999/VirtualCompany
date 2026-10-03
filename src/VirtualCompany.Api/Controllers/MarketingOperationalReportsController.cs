using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/marketing/operational")]
[Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
public sealed class MarketingOperationalReportsController(IMarketingOperationalReportService reports, ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet("report")]
    public Task<ActionResult<MarketingOperationalReport>> Report(DateTime fromUtc, DateTime toUtc, Guid? campaignId = null,
        string? currency = null, string? state = null, CancellationToken ct = default) =>
        Read(() => reports.GetAsync(CompanyId, new(fromUtc, toUtc, campaignId, currency, state), ct));
    [HttpGet("review")]
    public Task<ActionResult<MarketingCampaignReview>> Review(Guid? campaignId = null, Guid? briefId = null, CancellationToken ct = default) =>
        Read(() => reports.GetReviewAsync(CompanyId, campaignId, briefId, ct));
    private Guid CompanyId => context.CompanyId ?? throw new UnauthorizedAccessException();
    private async Task<ActionResult<T>> Read<T>(Func<Task<T>> read)
    {
        try { return Ok(await read()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Check the Marketing selection.", detail: ex.Message); }
    }
}
