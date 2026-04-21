using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/reconciliation-suggestions")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalReconciliationSuggestionsController : ControllerBase
{
    private readonly IReconciliationSuggestionReadService _readService;
    private readonly IReconciliationSuggestionCommandService _commandService;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public InternalReconciliationSuggestionsController(
        IReconciliationSuggestionReadService readService,
        IReconciliationSuggestionCommandService commandService,
        ICurrentUserAccessor currentUserAccessor)
    {
        _readService = readService;
        _commandService = commandService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<ActionResult<ReconciliationSuggestionPageDto>> ListAsync(
        Guid companyId,
        [FromQuery] string? entityType,
        [FromQuery] string? status,
        [FromQuery] decimal? minConfidence,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _readService.GetSuggestionsAsync(
                new GetReconciliationSuggestionsQuery(
                    companyId,
                    entityType,
                    status,
                    minConfidence,
                    page,
                    pageSize),
                cancellationToken);

            return Ok(result);
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Reconciliation suggestion request is invalid.", StatusCodes.Status400BadRequest));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Reconciliation suggestion request is invalid.", StatusCodes.Status400BadRequest));
        }
    }

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{suggestionId:guid}/accept")]
    public async Task<ActionResult<AcceptedReconciliationSuggestionDto>> AcceptAsync(
        Guid companyId,
        Guid suggestionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.AcceptSuggestionAsync(
                new AcceptReconciliationSuggestionCommand(companyId, suggestionId, ResolveActorUserId()),
                cancellationToken);

            return Ok(result);
        }
        catch (FinanceValidationException ex)
        {
            return BuildValidationProblem(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
    }

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{suggestionId:guid}/reject")]
    public Task<ActionResult<ReconciliationSuggestionRecordDto>> RejectAsync(
        Guid companyId,
        Guid suggestionId,
        CancellationToken cancellationToken) =>
        ExecuteRejectAsync(companyId, suggestionId, cancellationToken);

    private async Task<ActionResult<ReconciliationSuggestionRecordDto>> ExecuteRejectAsync(Guid companyId, Guid suggestionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandService.RejectSuggestionAsync(
                new RejectReconciliationSuggestionCommand(companyId, suggestionId, ResolveActorUserId()),
                cancellationToken);
            return Ok(result);
        }
        catch (FinanceValidationException ex) { return BuildValidationProblem(ex); }
        catch (KeyNotFoundException ex) { return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest)); }
        catch (ArgumentException ex) { return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest)); }
        catch (InvalidOperationException ex) { return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest)); }
    }

    private Guid ResolveActorUserId() => _currentUserAccessor.UserId ?? throw new UnauthorizedAccessException("A resolved company user identity is required for reconciliation suggestion review actions.");
    private ActionResult BuildValidationProblem(FinanceValidationException ex) => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase)) { Title = "Finance validation failed", Detail = ex.Message, Status = StatusCodes.Status400BadRequest, Instance = HttpContext.Request.Path });
    private ProblemDetails CreateProblemDetails(string detail, string title, int status) => new() { Title = title, Detail = detail, Status = status, Instance = HttpContext.Request.Path };
}