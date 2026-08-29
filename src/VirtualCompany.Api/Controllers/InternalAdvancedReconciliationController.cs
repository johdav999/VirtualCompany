using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/advanced-reconciliation")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalAdvancedReconciliationController : ControllerBase
{
    private readonly IAdvancedReconciliationReadService _readService;
    private readonly IAdvancedReconciliationCommandService _commandService;
    private readonly ICurrentUserAccessor _currentUser;

    public InternalAdvancedReconciliationController(IAdvancedReconciliationReadService readService,
        IAdvancedReconciliationCommandService commandService, ICurrentUserAccessor currentUser)
    { _readService = readService; _commandService = commandService; _currentUser = currentUser; }

    [HttpGet]
    public Task<ActionResult<AdvancedReconciliationWorkspaceDto>> ListAsync(Guid companyId, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] decimal? maximumConfidence, [FromQuery] int limit,
        CancellationToken cancellationToken) => ExecuteAsync(() => _readService.ListAsync(
            new(companyId, status, search, maximumConfidence, limit), cancellationToken));

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<AdvancedReconciliationGroupDetailDto>> GetAsync(Guid companyId, Guid groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _readService.GetAsync(new(companyId, groupId), cancellationToken);
            return result is null ? NotFound(Problem("The reconciliation group was not found.", 404)) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, 400)); }
    }

    [HttpGet("rules")]
    public Task<ActionResult<IReadOnlyList<AdvancedReconciliationRuleDto>>> ListRulesAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteAsync(() => _readService.ListRulesAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("rules")]
    public Task<ActionResult<AdvancedReconciliationRuleDto>> CreateRuleAsync(Guid companyId,
        [FromBody] CreateAdvancedReconciliationRuleRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commandService.CreateRuleVersionAsync(new(companyId, request.Name, request.ReferenceNormalizationPattern,
                request.CounterpartyNormalizationPattern, request.ProviderPattern, request.AmountTolerance,
                request.TimingWindowDays, request.RecommendationThreshold, request.LowConfidenceThreshold,
                request.MaterialityThreshold, Actor()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost]
    public Task<ActionResult<AdvancedReconciliationGroupDetailDto>> CreateAsync(Guid companyId,
        [FromBody] CreateAdvancedReconciliationGroupRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commandService.CreateGroupAsync(new(companyId, request.Reference, request.Counterparty, request.Currency,
                request.RuleVersion, request.CorrectionOfGroupId, request.Nodes ?? [], request.Edges ?? [], Actor(),
                HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{groupId:guid}/accept")]
    public Task<ActionResult<AdvancedReconciliationGroupDetailDto>> AcceptAsync(Guid companyId, Guid groupId,
        [FromBody] AcceptAdvancedReconciliationGroupRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commandService.AcceptAsync(new(companyId, groupId, request.ExpectedVersion, request.ExpectedRuleVersion,
                request.DecisionReason, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{groupId:guid}/reject")]
    public Task<ActionResult<AdvancedReconciliationGroupDetailDto>> RejectAsync(Guid companyId, Guid groupId,
        [FromBody] RejectAdvancedReconciliationGroupRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commandService.RejectAsync(new(companyId, groupId, request.ExpectedVersion, request.DecisionReason,
                Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{groupId:guid}/reverse")]
    public Task<ActionResult<AdvancedReconciliationGroupDetailDto>> ReverseAsync(Guid companyId, Guid groupId,
        [FromBody] ReverseAdvancedReconciliationGroupRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commandService.ReverseAsync(new(companyId, groupId, request.ExpectedVersion, request.FiscalPeriodId,
                request.PostingDate, request.Reason, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceValidationException ex) { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase)) { Title = "Advanced reconciliation validation failed", Detail = ex.Message, Status = 400 }); }
        catch (KeyNotFoundException ex) { return NotFound(Problem(ex.Message, 404)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DbUpdateConcurrencyException) { return Conflict(Problem("The reconciliation group changed after it was opened.", 409)); }
        catch (AccountingPostingException ex)
        {
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return StatusCode(status, Problem(ex.Message, status));
        }
        catch (InvalidOperationException ex) { return Conflict(Problem(ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, 400)); }
    }

    private Guid Actor() => _currentUser.UserId ?? throw new UnauthorizedAccessException("A resolved company user is required.");
    private ProblemDetails Problem(string detail, int status) => new() { Title = "Advanced reconciliation request failed", Detail = detail, Status = status, Instance = HttpContext.Request.Path };
}
