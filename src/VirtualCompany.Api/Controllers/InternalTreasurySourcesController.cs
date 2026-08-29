using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/treasury-sources")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalTreasurySourcesController : ControllerBase
{
    private readonly ITreasuryMovementReadService _read;
    private readonly ITreasuryMovementCommandService _commands;
    private readonly ICurrentUserAccessor _currentUser;

    public InternalTreasurySourcesController(ITreasuryMovementReadService read,
        ITreasuryMovementCommandService commands, ICurrentUserAccessor currentUser)
    { _read = read; _commands = commands; _currentUser = currentUser; }

    [HttpGet]
    public Task<ActionResult<TreasurySourceListDto>> ListAsync(Guid companyId, [FromQuery] string? status,
        [FromQuery] Guid? bankTransactionId, [FromQuery] int limit, CancellationToken cancellationToken) =>
        ExecuteAsync(() => _read.ListAsync(new(companyId, status, bankTransactionId, limit), cancellationToken));

    [HttpGet("{sourceType}/{sourceId:guid}")]
    public async Task<ActionResult<TreasurySourceDetailDto>> GetAsync(Guid companyId, string sourceType,
        Guid sourceId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _read.GetAsync(new(companyId, sourceType, sourceId), cancellationToken);
            return result is null ? NotFound(Problem("Treasury source was not found.", 404)) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (TreasuryMovementException ex) { return TreasuryProblem(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, 400)); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("transfers")]
    public Task<ActionResult<TreasurySourceDetailDto>> CreateTransferAsync(Guid companyId,
        [FromBody] CreateTreasuryTransferRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.CreateTransferAsync(new(companyId, request.SourceIdentity, request.FromBankAccountId,
                request.ToBankAccountId, request.Amount, request.FeeAmount, request.Currency,
                request.FeeFinanceAccountId, request.MaterialityThreshold, request.CorrectionOfTransferId,
                request.OutboundBankTransactionId, request.InboundBankTransactionId, request.Evidence ?? [],
                Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("bank-adjustments")]
    public Task<ActionResult<TreasurySourceDetailDto>> CreateBankAdjustmentAsync(Guid companyId,
        [FromBody] CreateBankAdjustmentRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.CreateBankAdjustmentAsync(new(companyId, request.SourceIdentity, request.AdjustmentKind,
                request.BankAccountId, request.BankTransactionId, request.CounterpartFinanceAccountId,
                request.Amount, request.Currency, request.Description, request.MaterialityThreshold,
                request.CorrectionOfAdjustmentId, request.Evidence ?? [], Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("card-settlements")]
    public Task<ActionResult<TreasurySourceDetailDto>> CreateCardSettlementAsync(Guid companyId,
        [FromBody] CreateCardSettlementRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.CreateCardSettlementAsync(new(companyId, request.SourceIdentity, request.ProviderBatchReference,
                request.BankAccountId, request.ReceivableFinanceAccountId, request.GrossAmount, request.FeeAmount,
                request.NetAmount, request.Currency, request.MaterialityThreshold, request.CorrectionOfSettlementId,
                request.BankTransactionId, request.Evidence ?? [], Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("payout-settlements")]
    public Task<ActionResult<TreasurySourceDetailDto>> CreatePayoutSettlementAsync(Guid companyId,
        [FromBody] CreatePayoutSettlementRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.CreatePayoutSettlementAsync(new(companyId, request.SourceIdentity, request.ProviderBatchReference,
                request.BankAccountId, request.PayoutClearingFinanceAccountId, request.GrossAmount, request.FeeAmount,
                request.NetAmount, request.Currency, request.MaterialityThreshold, request.CorrectionOfSettlementId,
                request.BankTransactionId, request.Evidence ?? [], Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{sourceType}/{sourceId:guid}/bank-evidence")]
    public Task<ActionResult<TreasurySourceDetailDto>> LinkBankEvidenceAsync(Guid companyId, string sourceType,
        Guid sourceId, [FromBody] LinkTreasuryBankEvidenceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.LinkBankEvidenceAsync(new(companyId, sourceType, sourceId, request.BankTransactionId,
                request.TransferLegRole, request.ExpectedVersion, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{sourceType}/{sourceId:guid}/approval")]
    public Task<ActionResult<TreasurySourceDetailDto>> BindApprovalAsync(Guid companyId, string sourceType,
        Guid sourceId, [FromBody] BindTreasuryApprovalRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.BindApprovalAsync(new(companyId, sourceType, sourceId, request.ApprovalRequestId,
                request.ExpectedVersion, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{sourceType}/{sourceId:guid}/preview")]
    public Task<ActionResult<TreasuryPostingPreviewDto>> PreviewAsync(Guid companyId, string sourceType,
        Guid sourceId, [FromBody] PreviewTreasuryPostingRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.PreviewAsync(new(companyId, sourceType, sourceId, request.FiscalPeriodId,
                request.PostingDate, Actor()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{sourceType}/{sourceId:guid}/post")]
    public Task<ActionResult<TreasurySourceDetailDto>> PostAsync(Guid companyId, string sourceType,
        Guid sourceId, [FromBody] PostTreasurySourceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.PostAsync(new(companyId, sourceType, sourceId, request.FiscalPeriodId, request.PostingDate,
                request.ExpectedVersion, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{sourceType}/{sourceId:guid}/reverse")]
    public Task<ActionResult<TreasurySourceDetailDto>> ReverseAsync(Guid companyId, string sourceType,
        Guid sourceId, [FromBody] ReverseTreasurySourceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _commands.ReverseAsync(new(companyId, sourceType, sourceId, request.FiscalPeriodId, request.PostingDate,
                request.ExpectedVersion, request.Reason, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceValidationException ex) { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase)) { Title = "Treasury validation failed", Detail = ex.Message, Status = 400 }); }
        catch (TreasuryMovementException ex) { return TreasuryProblem(ex); }
        catch (AccountingPostingException ex) { var status = ex.IsConflict ? 409 : 400; var problem = Problem(ex.Message, status); problem.Extensions["reasonCode"] = ex.ReasonCode; return StatusCode(status, problem); }
        catch (DbUpdateConcurrencyException) { return Conflict(Problem("The treasury source changed after it was opened.", 409)); }
        catch (DbUpdateException) { return Conflict(Problem("The treasury source conflicts with existing company evidence.", 409)); }
        catch (KeyNotFoundException ex) { return NotFound(Problem(ex.Message, 404)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return Conflict(Problem(ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, 400)); }
    }

    private ActionResult TreasuryProblem(TreasuryMovementException ex)
    {
        var status = ex.IsConflict ? 409 : 400; var problem = Problem(ex.Message, status);
        problem.Extensions["reasonCode"] = ex.ReasonCode; return StatusCode(status, problem);
    }
    private Guid Actor() => _currentUser.UserId ?? throw new UnauthorizedAccessException("A resolved company user is required.");
    private ProblemDetails Problem(string detail, int status) => new() { Title = "Treasury request failed", Detail = detail, Status = status, Instance = HttpContext.Request.Path };
}

public sealed record CreateTreasuryTransferRequest(string SourceIdentity, Guid FromBankAccountId, Guid ToBankAccountId,
    decimal Amount, decimal FeeAmount, string Currency, Guid? FeeFinanceAccountId, decimal MaterialityThreshold,
    Guid? CorrectionOfTransferId, Guid? OutboundBankTransactionId, Guid? InboundBankTransactionId,
    IReadOnlyList<TreasuryEvidenceInputDto>? Evidence);
public sealed record CreateBankAdjustmentRequest(string SourceIdentity, string AdjustmentKind, Guid BankAccountId,
    Guid BankTransactionId, Guid CounterpartFinanceAccountId, decimal Amount, string Currency, string Description,
    decimal MaterialityThreshold, Guid? CorrectionOfAdjustmentId, IReadOnlyList<TreasuryEvidenceInputDto>? Evidence);
public sealed record CreateCardSettlementRequest(string SourceIdentity, string ProviderBatchReference, Guid BankAccountId,
    Guid ReceivableFinanceAccountId, decimal GrossAmount, decimal FeeAmount, decimal NetAmount, string Currency,
    decimal MaterialityThreshold, Guid? CorrectionOfSettlementId, Guid? BankTransactionId,
    IReadOnlyList<TreasuryEvidenceInputDto>? Evidence);
public sealed record CreatePayoutSettlementRequest(string SourceIdentity, string ProviderBatchReference, Guid BankAccountId,
    Guid PayoutClearingFinanceAccountId, decimal GrossAmount, decimal FeeAmount, decimal NetAmount, string Currency,
    decimal MaterialityThreshold, Guid? CorrectionOfSettlementId, Guid? BankTransactionId,
    IReadOnlyList<TreasuryEvidenceInputDto>? Evidence);
public sealed record LinkTreasuryBankEvidenceRequest(Guid BankTransactionId, string? TransferLegRole, long ExpectedVersion);
public sealed record BindTreasuryApprovalRequest(Guid ApprovalRequestId, long ExpectedVersion);
public sealed record PreviewTreasuryPostingRequest(Guid FiscalPeriodId, DateOnly PostingDate);
public sealed record PostTreasurySourceRequest(Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion);
public sealed record ReverseTreasurySourceRequest(Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion, string Reason);
