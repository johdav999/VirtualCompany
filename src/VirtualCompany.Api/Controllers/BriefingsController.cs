using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/briefings")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class BriefingsController : ControllerBase
{
    private readonly ICompanyBriefingService _briefingService;

    public BriefingsController(ICompanyBriefingService briefingService)
    {
        _briefingService = briefingService;
    }

    [HttpPost("aggregate")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<BriefingAggregateResultDto>> AggregateAsync(
        Guid companyId,
        [FromBody] GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.AggregateAsync(companyId, command, cancellationToken));
        }
        catch (BriefingValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("latest")]
    public async Task<ActionResult<DashboardBriefingCardDto>> GetLatestAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.GetLatestDashboardBriefingsAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("generate")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<CompanyBriefingGenerationResult>> GenerateAsync(
        Guid companyId,
        [FromBody] GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.GenerateAsync(companyId, command, cancellationToken));
        }
        catch (BriefingValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<CompanyBriefingDeliveryPreferenceDto>> GetPreferencesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.GetDeliveryPreferenceAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<CompanyBriefingDeliveryPreferenceDto>> UpdatePreferencesAsync(
        Guid companyId,
        [FromBody] UpdateCompanyBriefingDeliveryPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.UpdateDeliveryPreferenceAsync(companyId, command, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}