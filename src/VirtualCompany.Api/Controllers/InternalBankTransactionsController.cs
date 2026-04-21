using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/bank-transactions")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalBankTransactionsController : ControllerBase
{
    private readonly IBankTransactionReadService _readService;
    private readonly IBankTransactionCommandService _commandService;

    public InternalBankTransactionsController(
        IBankTransactionReadService readService,
        IBankTransactionCommandService commandService)
    {
        _readService = readService;
        _commandService = commandService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BankTransactionDto>>> ListAsync(
        Guid companyId,
        [FromQuery] Guid? bankAccountId,
        [FromQuery] DateTime? bookingDateFromUtc,
        [FromQuery] DateTime? bookingDateToUtc,
        [FromQuery] string? status,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _readService.ListAsync(
                new ListBankTransactionsQuery(
                    companyId,
                    bankAccountId,
                    bookingDateFromUtc,
                    bookingDateToUtc,
                    status,
                    minAmount,
                    maxAmount,
                    limit),
                cancellationToken);

            return Ok(result);
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bank transaction request is invalid.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
    }

    [HttpGet("{bankTransactionId:guid}")]
    public async Task<ActionResult<BankTransactionDetailDto>> GetDetailAsync(
        Guid companyId,
        Guid bankTransactionId,
        CancellationToken cancellationToken)
    {
        var result = await _readService.GetDetailAsync(
            new GetBankTransactionDetailQuery(companyId, bankTransactionId),
            cancellationToken);

        return result is null
            ? NotFound(new ProblemDetails
            {
                Title = "Bank transaction was not found.",
                Detail = "The requested bank transaction does not exist in the active company context.",
                Status = StatusCodes.Status404NotFound,
                Instance = HttpContext.Request.Path
            })
            : Ok(result);
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{bankTransactionId:guid}/reconcile")]
    public async Task<ActionResult<BankTransactionDetailDto>> ReconcileAsync(
        Guid companyId,
        Guid bankTransactionId,
        [FromBody] ReconcileBankTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.ReconcileAsync(
                request.ToCommand(companyId, bankTransactionId),
                cancellationToken);

            return Ok(result);
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Bank transaction was not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound,
                Instance = HttpContext.Request.Path
            });
        }
    }

    private ActionResult BuildValidationProblem(FinanceValidationException ex)
    {
        var errors = ex.Errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = "Finance validation failed",
            Detail = ex.Message,
            Status = StatusCodes.Status400BadRequest,
            Instance = HttpContext.Request.Path
        });
    }
}