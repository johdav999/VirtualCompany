using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class OnboardingController : ControllerBase
{
    private readonly ICompanyOnboardingService _onboardingService;
    private readonly ICompanyOnboardingWorkshopService _workshopService;

    public OnboardingController(ICompanyOnboardingService onboardingService, ICompanyOnboardingWorkshopService workshopService)
    {
        _onboardingService = onboardingService;
        _workshopService = workshopService;
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("workshop")]
    public async Task<ActionResult<CompanyOnboardingWorkshopBootstrapDto>> StartWorkshopAsync(
        [FromBody] CreateCompanyWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        try { return Ok(await _workshopService.StartOrResumeAsync(request,cancellationToken)); }
        catch(CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>(ex.Errors))
            { Title="Validation failed",Status=StatusCodes.Status400BadRequest });
        }
        catch(UnauthorizedAccessException) { return Forbid(); }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("company")]
    public async Task<ActionResult<CreateCompanyResultDto>> CreateCompanyAsync(
        [FromBody] CreateCompanyCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _onboardingService.CreateCompanyAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpGet("recommended-defaults")]
    public async Task<ActionResult<OnboardingTemplateRecommendationDto?>> GetRecommendedDefaultsAsync(
        [FromQuery] string? industry,
        [FromQuery] string? businessType,
        CancellationToken cancellationToken)
    {
        var recommendation = await _onboardingService.GetRecommendedDefaultsAsync(
            new GetOnboardingTemplateRecommendationRequest(industry, businessType),
            cancellationToken);

        return Ok(recommendation);
    }

    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<OnboardingTemplateDto>>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var templates = await _onboardingService.GetTemplatesAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("progress")]
    public async Task<ActionResult<CompanyOnboardingProgressDto?>> GetProgressAsync(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var progress = companyId.HasValue
            ? await _onboardingService.GetProgressAsync(companyId.Value, cancellationToken)
            : await _onboardingService.GetProgressAsync(cancellationToken);
        return Ok(progress);
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("workspace")]
    public async Task<ActionResult<CompanyOnboardingProgressDto>> CreateWorkspaceAsync(
        [FromBody] CreateCompanyWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = await _onboardingService.CreateWorkspaceAsync(request, cancellationToken);
            return Ok(progress);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPut("progress")]
    public async Task<ActionResult<CompanyOnboardingProgressDto>> SaveProgressAsync(
        [FromBody] SaveCompanyOnboardingProgressRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = await _onboardingService.SaveProgressAsync(request, cancellationToken);
            return Ok(progress);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("abandon")]
    public async Task<ActionResult<CompanyOnboardingProgressDto>> AbandonOnboardingAsync(
        [FromBody] AbandonCompanyOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = await _onboardingService.AbandonOnboardingAsync(request, cancellationToken);
            return Ok(progress);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("complete")]
    public async Task<ActionResult<CompleteCompanyOnboardingResultDto>> CompleteOnboardingAsync(
        [FromBody] CompleteCompanyOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _onboardingService.CompleteOnboardingAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (CompanyOnboardingValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
