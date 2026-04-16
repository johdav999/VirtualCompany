using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Tenancy;
using AppBriefings = VirtualCompany.Application.Briefings;
using MobileDtos = VirtualCompany.Shared.Mobile;

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
    public async Task<ActionResult<AppBriefings.DashboardBriefingCardDto>> GetLatestAsync(Guid companyId, CancellationToken cancellationToken)
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

    [HttpGet("latest/mobile")]
    public async Task<ActionResult<MobileDtos.MobileBriefingDto>> GetLatestMobileAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _briefingService.GetLatestDashboardBriefingsAsync(companyId, cancellationToken);
            var daily = latest.Daily;
            if (daily is null)
            {
                return Ok(new MobileDtos.MobileBriefingDto { SyncedAtUtc = DateTime.UtcNow });
            }

            return Ok(new MobileDtos.MobileBriefingDto
            {
                Id = daily.Id,
                Title = daily.Title,
                Summary = TrimForMobile(daily.SummaryBody, 360),
                GeneratedUtc = daily.GeneratedUtc,
                Highlights = daily.SourceReferences
                    .Select(x => string.IsNullOrWhiteSpace(x.Status) ? x.Label : $"{x.Label} ({x.Status})")
                    .Take(5)
                    .ToList(),
                SyncedAtUtc = DateTime.UtcNow
            });
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

    [HttpGet("preferences/me")]
    public async Task<ActionResult<BriefingPreferenceDto>> GetUserPreferencesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.GetUserBriefingPreferenceAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("preferences/me")]
    public async Task<ActionResult<BriefingPreferenceDto>> UpsertUserPreferencesAsync(
        Guid companyId,
        [FromBody] UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.UpsertUserBriefingPreferenceAsync(companyId, command, cancellationToken));
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

    [HttpGet("tenant-defaults")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<TenantBriefingDefaultDto?>> GetTenantDefaultsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.GetTenantBriefingDefaultAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("tenant-defaults")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<TenantBriefingDefaultDto>> UpsertTenantDefaultsAsync(
        Guid companyId,
        [FromBody] UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _briefingService.UpsertTenantBriefingDefaultAsync(companyId, command, cancellationToken));
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

    private static string TrimForMobile(string value, int maxLength) =>
        value.Trim().Length <= maxLength ? value.Trim() : value.Trim()[..maxLength].TrimEnd() + "...";

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}
