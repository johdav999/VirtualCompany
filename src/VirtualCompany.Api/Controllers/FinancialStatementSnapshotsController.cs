using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/financial-statements")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinancialStatementSnapshotsController : ControllerBase
{
    private readonly IFinanceReadService _financeReadService;

    public FinancialStatementSnapshotsController(IFinanceReadService financeReadService)
    {
        _financeReadService = financeReadService;
    }

    [HttpGet("snapshots")]
    public async Task<ActionResult<IReadOnlyList<FinancialStatementSnapshotSummaryDto>>> ListSnapshotsAsync(
        Guid companyId,
        [FromQuery] Guid? fiscalPeriodId,
        [FromQuery] string? statementType,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatementType(statementType, required: false, out var parsedStatementType, out var validationError))
        {
            return BadRequest(CreateProblemDetails(validationError!, "Invalid financial statement request.", StatusCodes.Status400BadRequest));
        }

        try
        {
            return Ok(await _financeReadService.ListFinancialStatementSnapshotsAsync(
                new ListFinancialStatementSnapshotsQuery(companyId, fiscalPeriodId, parsedStatementType),
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("snapshots/{snapshotId:guid}")]
    public async Task<ActionResult<FinancialStatementSnapshotDetailDto>> GetSnapshotAsync(
        Guid companyId,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _financeReadService.GetFinancialStatementSnapshotAsync(
                new GetFinancialStatementSnapshotQuery(companyId, snapshotId),
                cancellationToken);

            return snapshot is null
                ? NotFound(CreateProblemDetails("Financial statement snapshot was not found.", "Financial statement snapshot was not found.", StatusCodes.Status404NotFound))
                : Ok(snapshot);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("snapshots/{snapshotId:guid}/lines/{lineCode}/drilldown")]
    public async Task<ActionResult<FinancialStatementDrilldownDto>> GetSnapshotDrilldownAsync(
        Guid companyId,
        Guid snapshotId,
        string lineCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lineCode))
        {
            return BadRequest(CreateProblemDetails("LineCode is required.", "Invalid financial statement request.", StatusCodes.Status400BadRequest));
        }

        try
        {
            return Ok(await _financeReadService.GetFinancialStatementDrilldownAsync(
                new GetFinancialStatementDrilldownQuery(companyId, null, null, lineCode.Trim(), null, snapshotId),
                cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, "Financial statement resource was not found.", StatusCodes.Status404NotFound));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("drilldown")]
    public async Task<ActionResult<FinancialStatementDrilldownDto>> GetDrilldownAsync(
        Guid companyId,
        [FromQuery] Guid fiscalPeriodId,
        [FromQuery] string statementType,
        [FromQuery] string lineCode,
        [FromQuery] int? snapshotVersionNumber,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatementType(statementType, required: true, out var parsedStatementType, out var validationError))
        {
            return BadRequest(CreateProblemDetails(validationError!, "Invalid financial statement request.", StatusCodes.Status400BadRequest));
        }

        if (string.IsNullOrWhiteSpace(lineCode))
        {
            return BadRequest(CreateProblemDetails("LineCode is required.", "Invalid financial statement request.", StatusCodes.Status400BadRequest));
        }

        try
        {
            return Ok(await _financeReadService.GetFinancialStatementDrilldownAsync(
                new GetFinancialStatementDrilldownQuery(companyId, fiscalPeriodId, parsedStatementType!.Value, lineCode.Trim(), snapshotVersionNumber),
                cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, "Financial statement resource was not found.", StatusCodes.Status404NotFound));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static bool TryParseStatementType(string? value, bool required, out FinancialStatementType? statementType, out string? validationError)
    {
        statementType = null;
        validationError = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                validationError = $"StatementType is required. Allowed values: {string.Join(", ", FinancialStatementTypeValues.AllowedValues)}.";
                return false;
            }

            return true;
        }

        if (!FinancialStatementTypeValues.TryParse(value, out var parsed))
        {
            validationError = $"StatementType must be one of: {string.Join(", ", FinancialStatementTypeValues.AllowedValues)}.";
            return false;
        }

        statementType = parsed;
        return true;
    }

    private ProblemDetails CreateProblemDetails(string detail, string title, int status) =>
        new()
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };
}
