using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/companies/{companyId:guid}/planning/quarters"), Authorize(Policy = CompanyPolicies.CompanyManager), RequireCompanyContext]
public sealed class QuarterlyPlanningController(IQuarterlyPlanningService planning) : ControllerBase
{
    [HttpGet("options")] public Task<ActionResult<QuarterlyPlanningOptions>> Options(Guid companyId, int fiscalYear, int quarter, CancellationToken ct) => Run(() => planning.OptionsAsync(companyId, fiscalYear, quarter, ct));
    [HttpGet("reviews")] public Task<ActionResult<IReadOnlyList<QuarterReviewSummary>>> History(Guid companyId, int fiscalYear, int quarter, CancellationToken ct) => Run(() => planning.HistoryAsync(companyId, fiscalYear, quarter, ct));
    [HttpPost("preview")] public Task<ActionResult<QuarterReviewPreview>> Preview(Guid companyId, PreviewQuarterReview proposal, CancellationToken ct) => Run(() => planning.PreviewAsync(companyId, proposal, ct));
    [HttpPost("reviews")] public Task<ActionResult<QuarterReviewDocument>> Save(Guid companyId, SaveQuarterReview command, CancellationToken ct) => Run(() => planning.SaveAsync(companyId, command, ct));
    [HttpPost("reviews/{id:guid}/open")] public Task<ActionResult<QuarterReviewDocument>> Open(Guid companyId, Guid id, CancellationToken ct) => Run(() => planning.OpenAsync(companyId, id, ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (CompanyOperatingConcurrencyException ex) { return Problem(statusCode: 409, detail: ex.Message); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, detail: ex.Message); }
        catch (InvalidDataException ex) { return Problem(statusCode: 422, detail: ex.Message); }
        catch (MonthlyReviewReproductionException ex) { return Problem(statusCode: 422, detail: ex.Message); }
    }
}
