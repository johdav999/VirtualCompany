using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/companies/{companyId:guid}/briefings/cadence"), Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
public sealed class BriefingCadenceController(IBriefingCadenceService service) : ControllerBase
{
    [HttpGet] public Task<ActionResult<BriefingCadenceContext>> Get(Guid companyId, CancellationToken ct) => Run(() => service.GetAsync(companyId, ct));
    [HttpPut] public Task<ActionResult<BriefingCadenceContext>> Save(Guid companyId, BriefingCadenceSettings settings, CancellationToken ct) => Run(() => service.SaveAsync(companyId, settings, ct));
    [HttpGet("preview")] public Task<ActionResult<BriefingCadencePreview>> Preview(Guid companyId, CancellationToken ct) => Run(() => service.PreviewAsync(companyId, ct));
    [HttpGet("deliveries/{deliveryId:guid}")] public Task<ActionResult<BriefingCadencePreview>> Open(Guid companyId, Guid deliveryId, CancellationToken ct) => Run(() => service.OpenAsync(companyId, deliveryId, ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException e) { return Problem(statusCode: 400, detail: e.Message); }
        catch (InvalidDataException e) { return Problem(statusCode: 422, detail: e.Message); }
    }
}
