using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/cash-posting")]
[Authorize(Policy = CompanyPolicies.FinanceEdit)]
[RequireCompanyContext]
public sealed class InternalFinanceCashPostingBackfillController : ControllerBase
{
    private readonly ICashPostingTraceabilityBackfillService _backfillService;

    public InternalFinanceCashPostingBackfillController(ICashPostingTraceabilityBackfillService backfillService)
    {
        _backfillService = backfillService;
    }

    [HttpPost("backfill")]
    public async Task<ActionResult<CashPostingTraceabilityBackfillResultDto>> BackfillAsync(
        Guid companyId,
        [FromBody] RunCashPostingTraceabilityBackfillRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is not null && request.BatchSize <= 0)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(RunCashPostingTraceabilityBackfillRequest.BatchSize)] = ["Batch size must be greater than zero."]
            })
            {
                Title = "Finance validation failed",
                Detail = "Update the backfill request and try again.",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        var result = await _backfillService.BackfillAsync(new BackfillCashPostingTraceabilityCommand(companyId, request?.BatchSize ?? 250, request?.CorrelationId), cancellationToken);
        return Ok(result);
    }
}

public sealed record RunCashPostingTraceabilityBackfillRequest(int BatchSize = 250, string? CorrelationId = null);