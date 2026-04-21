using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/financial-statement-mappings")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinancialStatementMappingsController : ControllerBase
{
    private readonly IFinancialStatementMappingService _mappings;

    public FinancialStatementMappingsController(IFinancialStatementMappingService mappings)
    {
        _mappings = mappings;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinancialStatementMappingDto>>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mappings.ListAsync(new ListFinancialStatementMappingsQuery(companyId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("validation")]
    public async Task<ActionResult<FinancialStatementMappingValidationResultDto>> ValidateAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mappings.ValidateAsync(new ValidateFinancialStatementMappingsQuery(companyId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    public async Task<ActionResult<FinancialStatementMappingDto>> CreateAsync(
        Guid companyId,
        [FromBody] CreateFinancialStatementMappingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _mappings.CreateAsync(
                new CreateFinancialStatementMappingCommand(
                    companyId,
                    request.AccountId,
                    request.StatementType,
                    request.ReportSection,
                    request.LineClassification,
                    request.IsActive),
                cancellationToken);

            return Created($"/api/companies/{companyId:D}/financial-statement-mappings/{created.Id:D}", created);
        }
        catch (FinancialStatementMappingCommandException ex)
        {
            return StatusCode(ex.StatusCode, CreateProblemDetails(ex.Title, ex.Message, ex.StatusCode, ex.Code, ex.Errors));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("{mappingId:guid}")]
    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    public async Task<ActionResult<FinancialStatementMappingDto>> UpdateAsync(
        Guid companyId,
        Guid mappingId,
        [FromBody] UpdateFinancialStatementMappingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mappings.UpdateAsync(
                new UpdateFinancialStatementMappingCommand(
                    companyId,
                    mappingId,
                    request.AccountId,
                    request.StatementType,
                    request.ReportSection,
                    request.LineClassification,
                    request.IsActive),
                cancellationToken));
        }
        catch (FinancialStatementMappingCommandException ex)
        {
            return StatusCode(ex.StatusCode, CreateProblemDetails(ex.Title, ex.Message, ex.StatusCode, ex.Code, ex.Errors));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(
                "Financial statement mapping was not found.",
                ex.Message,
                StatusCodes.Status404NotFound,
                FinancialStatementMappingValidationErrorCodes.MappingNotFound));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private ProblemDetails CreateProblemDetails(
        string title,
        string detail,
        int status,
        string code,
        IReadOnlyList<FinancialStatementMappingCommandErrorDto>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }
}

public sealed record CreateFinancialStatementMappingRequest(
    Guid AccountId,
    string StatementType,
    string ReportSection,
    string LineClassification,
    bool IsActive = true);

public sealed record UpdateFinancialStatementMappingRequest(
    Guid AccountId,
    string StatementType,
    string ReportSection,
    string LineClassification,
    bool IsActive = true);
