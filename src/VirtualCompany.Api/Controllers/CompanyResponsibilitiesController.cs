using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/responsibilities")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CompanyResponsibilitiesController : ControllerBase
{
    private readonly ICompanyResponsibilityService _service;
    public CompanyResponsibilitiesController(ICompanyResponsibilityService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<CompanyResponsibilitiesDto>> GetAsync(Guid companyId, CancellationToken cancellationToken)
        => Ok(await _service.GetAsync(companyId, cancellationToken));

    [HttpPost("presets/preview")]
    public Task<ActionResult<ResponsibilityPresetPreviewDto>> PreviewAsync(Guid companyId,
        [FromBody] ResponsibilityPresetRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _service.PreviewPresetAsync(companyId, request, cancellationToken));

    [HttpPost("presets/apply")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public Task<ActionResult<ResponsibilityPresetApplyResultDto>> ApplyAsync(Guid companyId,
        [FromBody] ResponsibilityPresetRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _service.ApplyPresetAsync(companyId, request, cancellationToken));

    [HttpPut("assignments")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public Task<ActionResult<CompanyResponsibilityAssignmentDto>> UpsertAsync(Guid companyId,
        [FromBody] UpsertCompanyResponsibilityAssignmentCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _service.UpsertAsync(companyId, command, cancellationToken));

    [HttpDelete("assignments/{assignmentId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<IActionResult> RemoveAsync(Guid companyId, Guid assignmentId, [FromQuery] long? expectedVersion,
        [FromQuery] string? reason, CancellationToken cancellationToken)
    {
        try { await _service.RemoveAsync(companyId, assignmentId, expectedVersion, reason, cancellationToken); return NoContent(); }
        catch (CompanyResponsibilityValidationException ex) { return ValidationProblem(CreateValidation(ex)); }
        catch (CompanyResponsibilityConflictException ex) { return Conflict(CreateConflict(ex)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (CompanyResponsibilityValidationException ex) { return ValidationProblem(CreateValidation(ex)); }
        catch (CompanyResponsibilityConflictException ex) { return Conflict(CreateConflict(ex)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private static ValidationProblemDetails CreateValidation(CompanyResponsibilityValidationException ex) =>
        new(new Dictionary<string, string[]>(ex.Errors)) { Title = "Validation failed", Status = StatusCodes.Status400BadRequest };
    private static ProblemDetails CreateConflict(CompanyResponsibilityConflictException ex) =>
        new() { Title = "Responsibility assignment conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict };
}
