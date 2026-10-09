using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/companies/{companyId:guid}/decision-work"), Authorize(Policy = CompanyPolicies.CompanyManager), RequireCompanyContext]
public sealed class DecisionWorkController(IDecisionWorkService service) : ControllerBase
{
    [HttpGet("context")] public Task<ActionResult<DecisionWorkContext>> Context(Guid companyId, string kind, Guid versionId, string itemKey, CancellationToken ct) => Run(() => service.ContextAsync(companyId, new(kind, versionId, itemKey), ct));
    [HttpPost("preview")] public Task<ActionResult<DecisionWorkPreview>> Preview(Guid companyId, DecisionWorkInput input, CancellationToken ct) => Run(() => service.PreviewAsync(companyId, input, ct));
    [HttpPost] public Task<ActionResult<DecisionWorkDocument>> Create(Guid companyId, ConfirmDecisionWork command, CancellationToken ct) => Run(() => service.CreateAsync(companyId, command, ct));
    [HttpPost("tasks/{taskId:guid}/open")] public Task<ActionResult<DecisionWorkDocument>> Open(Guid companyId, Guid taskId, CancellationToken ct) => Run(() => service.OpenAsync(companyId, taskId, ct));
    [HttpPost("tasks/{taskId:guid}/review")] public Task<ActionResult<DecisionWorkDocument>> Review(Guid companyId, Guid taskId, CancellationToken ct) => Run(() => service.SubmitReviewAsync(companyId, taskId, ct));
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (CompanyOperatingConcurrencyException ex) { return Problem(statusCode: 409, detail: ex.Message); }
        catch (ArgumentException ex) { return Problem(statusCode: 400, detail: ex.Message); }
        catch (TaskValidationException) { return Problem(statusCode: 400, detail: "This work proposal is invalid. Review its objective and scope."); }
        catch (InvalidDataException ex) { return Problem(statusCode: 422, detail: ex.Message); }
        catch (MonthlyReviewReproductionException ex) { return Problem(statusCode: 422, detail: ex.Message); }
    }
}
