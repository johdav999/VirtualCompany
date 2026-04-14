using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Alerts;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/alerts")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class AlertsController : ControllerBase
{
    private readonly ICompanyAlertService _alerts;

    public AlertsController(ICompanyAlertService alerts)
    {
        _alerts = alerts;
    }

    [HttpPost]
    public async Task<ActionResult<AlertMutationResultDto>> CreateAsync(Guid companyId, [FromBody] CreateAlertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _alerts.CreateAsync(companyId, command, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(GetByIdAsync), new { companyId, alertId = result.Alert.Id }, result)
                : Ok(result);
        }
        catch (AlertValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("detections")]
    public async Task<ActionResult<AlertMutationResultDto>> CreateFromDetectionAsync(Guid companyId, [FromBody] CreateDetectionAlertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _alerts.CreateOrDeduplicateFromDetectionAsync(companyId, command, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(GetByIdAsync), new { companyId, alertId = result.Alert.Id }, result)
                : Ok(result);
        }
        catch (AlertValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{alertId:guid}")]
    public async Task<ActionResult<AlertDto>> GetByIdAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _alerts.GetByIdAsync(companyId, alertId, cancellationToken));
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

    [HttpGet]
    public async Task<ActionResult<AlertListResultDto>> ListAsync(
        Guid companyId,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] string? status,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _alerts.ListAsync(companyId, new ListAlertsQuery(type, severity, status, createdFrom, createdTo, page, pageSize, skip, take), cancellationToken));
        }
        catch (AlertValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("{alertId:guid}")]
    public async Task<ActionResult<AlertDto>> UpdateAsync(Guid companyId, Guid alertId, [FromBody] UpdateAlertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _alerts.UpdateAsync(companyId, alertId, command, cancellationToken));
        }
        catch (AlertValidationException ex)
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

    [HttpDelete("{alertId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken)
    {
        try
        {
            await _alerts.DeleteAsync(companyId, alertId, cancellationToken);
            return NoContent();
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

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}
