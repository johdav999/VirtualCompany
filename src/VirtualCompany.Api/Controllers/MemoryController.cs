using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Memory;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/memory")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class MemoryController : ControllerBase
{
    private readonly ICompanyMemoryService _memoryService;

    public MemoryController(ICompanyMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    [HttpGet]
    public async Task<ActionResult<MemorySearchResultDto>> ListAsync(
        Guid companyId,
        [FromQuery] Guid? agentId,
        [FromQuery] string? memoryType,
        [FromQuery] string[]? memoryTypes,
        [FromQuery] DateTime? createdAfterUtc,
        [FromQuery] string? scope,
        [FromQuery] DateTime? createdBeforeUtc,
        [FromQuery] decimal? minSalience,
        [FromQuery] bool? onlyActive,
        [FromQuery] bool? includeDeleted,
        [FromQuery] bool? includeCompanyWide,
        [FromQuery(Name = "q")] string? queryText,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var filters = new MemorySearchFilters(
                agentId,
                memoryType,
                createdAfterUtc,
                createdBeforeUtc,
                minSalience,
                onlyActive ?? true,
                includeDeleted ?? false,
                includeCompanyWide ?? true,
                queryText,
                offset ?? 0,
                limit ?? 20,
                scope,
                memoryTypes,
                asOfUtc);

            return Ok(await _memoryService.SearchAsync(companyId, filters, cancellationToken));
        }
        catch (MemoryValidationException ex)
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

    [HttpPost("search")]
    public async Task<ActionResult<MemorySearchResultDto>> SearchAsync(
        Guid companyId,
        [FromBody] MemorySearchFilters filters,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _memoryService.SearchAsync(companyId, filters, cancellationToken));
        }
        catch (MemoryValidationException ex)
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

    [HttpGet("{memoryId:guid}")]
    public async Task<ActionResult<MemoryItemDto>> GetAsync(Guid companyId, Guid memoryId, CancellationToken cancellationToken)
    {
        try
        {
            var memoryItem = await _memoryService.GetAsync(companyId, memoryId, cancellationToken);
            return memoryItem is null ? NotFound() : Ok(memoryItem);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MemoryItemDto>> CreateAsync(
        Guid companyId,
        [FromBody] CreateMemoryItemCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var memoryItem = await _memoryService.CreateAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetAsync), new { companyId, memoryId = memoryItem.Id }, memoryItem);
        }
        catch (MemoryValidationException ex)
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

    [HttpPost("{memoryId:guid}/expire")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MemoryItemDto>> ExpireAsync(Guid companyId, Guid memoryId, [FromBody] ExpireMemoryItemCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var tenantScopedCommand = command with
            {
                CompanyId = companyId,
                MemoryItemId = memoryId
            };

            return Ok(await _memoryService.ExpireAsync(tenantScopedCommand, cancellationToken));
        }
        catch (MemoryValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid memory lifecycle operation", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
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

    [HttpDelete("{memoryId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> DeleteAsync(Guid companyId, Guid memoryId, [FromBody] DeleteMemoryItemCommand? command, CancellationToken cancellationToken)
    {
        try
        {
            var tenantScopedCommand = (command ?? new DeleteMemoryItemCommand()) with
            {
                CompanyId = companyId,
                MemoryItemId = memoryId
            };

            await _memoryService.DeleteAsync(tenantScopedCommand, cancellationToken);
            return NoContent();
        }
        catch (MemoryValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
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
}