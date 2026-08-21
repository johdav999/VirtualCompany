using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
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
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public InternalBankTransactionsController(
        IBankTransactionReadService readService,
        IBankTransactionCommandService commandService,
        ICurrentUserAccessor currentUserAccessor)
    {
        _readService = readService;
        _commandService = commandService;
        _currentUserAccessor = currentUserAccessor;
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
                request.ToCommand(
                    companyId,
                    bankTransactionId,
                    ResolveActorUserId(),
                    HttpContext.TraceIdentifier),
                cancellationToken);

            return Ok(result);
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (AccountingPostingException ex)
        {
            return BuildAccountingProblem(ex);
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

    [HttpGet("reconciliation")]
    public async Task<ActionResult<BankReconciliationWorkspaceDto>> ListReconciliationAsync(
        Guid companyId,
        [FromQuery] string? state,
        [FromQuery] string? search,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _readService.ListReconciliationAsync(
                new ListBankReconciliationItemsQuery(companyId, state, search, fromUtc, toUtc, limit), cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Reconciliation filters are invalid.", Detail = ex.Message, Status = 400 });
        }
    }

    [HttpGet("{bankTransactionId:guid}/reconciliation")]
    public async Task<ActionResult<BankReconciliationDetailDto>> GetReconciliationDetailAsync(
        Guid companyId, Guid bankTransactionId, CancellationToken cancellationToken)
    {
        var result = await _readService.GetReconciliationDetailAsync(new(companyId, bankTransactionId), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("imports")]
    public async Task<ActionResult<BankStatementImportResultDto>> ImportAsync(
        Guid companyId, [FromBody] ImportBankStatementRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _commandService.ImportStatementAsync(new ImportBankStatementCommand(
                companyId, request.BankAccountId, request.SourceKey, request.StatementIdentity, request.ContentHash,
                request.Rows ?? [], ResolveActorUserId(), HttpContext.TraceIdentifier), cancellationToken));
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Bank account was not found", Detail = ex.Message, Status = 404, Instance = HttpContext.Request.Path });
        }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("{bankTransactionId:guid}/reclassify-suspense")]
    public async Task<ActionResult<BankReconciliationDetailDto>> ReclassifySuspenseAsync(
        Guid companyId, Guid bankTransactionId, [FromBody] ReclassifyBankSuspenseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _commandService.ReclassifySuspenseAsync(new ReclassifyBankSuspenseCommand(
                companyId, bankTransactionId, request.TargetFinanceAccountId, request.FiscalPeriodId, request.PostingDate,
                request.Reason, request.ExpectedSourceVersion, request.IdempotencyKey, ResolveActorUserId(), HttpContext.TraceIdentifier), cancellationToken));
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (AccountingPostingException ex)
        {
            return BuildAccountingProblem(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Reconciliation target was not found", Detail = ex.Message, Status = 404, Instance = HttpContext.Request.Path });
        }
    }

    private Guid ResolveActorUserId() => _currentUserAccessor.UserId
        ?? throw new UnauthorizedAccessException("A resolved company user is required for bank reconciliation.");

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

    private ActionResult BuildAccountingProblem(AccountingPostingException exception)
    {
        var status = exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails
        {
            Title = exception.IsConflict ? "Bank reconciliation conflict" : "Bank reconciliation posting was rejected",
            Detail = exception.Message,
            Status = status,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["reasonCode"] = exception.ReasonCode;
        return StatusCode(status, problem);
    }
}
