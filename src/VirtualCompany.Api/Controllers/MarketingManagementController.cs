using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/marketing/management"), Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
public sealed class MarketingManagementController(IMarketingManagementService service, ICompanyContextAccessor context) : ControllerBase
{
    private Guid Company => context.CompanyId ?? throw new UnauthorizedAccessException();
    [HttpGet] public Task<ActionResult<MarketingManagementReport>> Report([FromQuery] MarketingManagementQuery query, CancellationToken ct)
        => Run(() => service.ReportAsync(Company, query, ct));
    [HttpGet("export")] public Task<ActionResult<MarketingManagementExport>> Export([FromQuery] MarketingManagementQuery query, CancellationToken ct)
        => Run(() => service.ExportAsync(Company, query, ct));
    [HttpGet("proposals")] public Task<ActionResult<IReadOnlyList<MarketingBudgetProposalSummary>>> History(int skip = 0, CancellationToken ct = default)
        => Run(() => service.HistoryAsync(Company, skip, ct));
    [HttpGet("proposals/{id:guid}")] public Task<ActionResult<MarketingBudgetProposal>> Open(Guid id, CancellationToken ct)
        => Run(() => service.OpenAsync(Company, id, ct));
    [HttpPost("proposals")] public Task<ActionResult<MarketingBudgetProposal>> Save(SaveMarketingBudgetProposal command, CancellationToken ct)
        => Run(() => service.SaveAsync(Company, command, ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, title: "Check the cohort and budget assumptions.", detail: ex.Message); }
        catch (InvalidDataException) { return Problem(statusCode: 422, title: "Retained budget results cannot be reproduced."); }
        catch (InvalidOperationException ex) { return Problem(statusCode: 409, title: "Reload the latest budget revision.", detail: ex.Message); }
    }
}
