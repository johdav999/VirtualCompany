using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/payment-batches")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalPaymentBatchesController : ControllerBase
{
    private readonly IPaymentBatchService _service;
    private readonly ICurrentUserAccessor _currentUser;
    public InternalPaymentBatchesController(IPaymentBatchService service, ICurrentUserAccessor currentUser)
    { _service = service; _currentUser = currentUser; }

    [HttpGet]
    public Task<ActionResult<PaymentBatchListDto>> ListAsync(Guid companyId, [FromQuery] string? status,
        [FromQuery] int limit, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.ListAsync(new(companyId, status, limit), cancellationToken));

    [HttpGet("eligible-obligations")]
    public Task<ActionResult<IReadOnlyList<EligiblePaymentObligationDto>>> EligibleAsync(Guid companyId,
        [FromQuery] int limit, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.ListEligibleObligationsAsync(new(companyId, limit <= 0 ? 200 : limit), cancellationToken));

    [HttpGet("{batchId:guid}")]
    public async Task<ActionResult<PaymentBatchDetailDto>> GetAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetAsync(new(companyId, batchId), cancellationToken);
            return result is null ? NotFound(Problem("Payment batch was not found.", 404)) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("{batchId:guid}/preview")]
    public Task<ActionResult<PaymentBatchPreviewDto>> PreviewAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.PreviewAsync(new(companyId, batchId), cancellationToken));

    [HttpGet("{batchId:guid}/send-readiness")]
    public Task<ActionResult<PaymentBatchSendReadinessDto>> SendReadinessAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.CheckSendReadinessAsync(new(companyId, batchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("beneficiaries")]
    public Task<ActionResult<PaymentBeneficiaryProfileDto>> RegisterBeneficiaryAsync(Guid companyId,
        [FromBody] RegisterPaymentBeneficiaryRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.RegisterBeneficiaryAsync(new(companyId, request.PartyType, request.PartyId,
                request.DisplayName, request.Rail, request.Destination, request.MaskedDestination,
                request.Currency, request.VerificationEvidenceReference,
                request.VerificationEvidenceHash, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost]
    public Task<ActionResult<PaymentBatchDetailDto>> CreateAsync(Guid companyId,
        [FromBody] CreatePaymentBatchRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.CreateAsync(new(companyId, request.Name, request.PlannedExecutionDate,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/obligations")]
    public Task<ActionResult<PaymentBatchDetailDto>> AddObligationAsync(Guid companyId, Guid batchId,
        [FromBody] AddPaymentBatchObligationRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.AddObligationAsync(new(companyId, batchId, request.ObligationType, request.SourceId,
                request.ExpectedVersion, request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/obligations/{obligationLinkId:guid}/remove")]
    public Task<ActionResult<PaymentBatchDetailDto>> RemoveObligationAsync(Guid companyId, Guid batchId,
        Guid obligationLinkId, [FromBody] PaymentBatchVersionedRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(() => _service.RemoveObligationAsync(new(
            companyId, batchId, obligationLinkId, request.ExpectedVersion, request.IdempotencyKey,
            Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/validate")]
    public Task<ActionResult<PaymentBatchDetailDto>> ValidateAsync(Guid companyId, Guid batchId,
        [FromBody] PaymentBatchVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.ValidateAsync(new(companyId, batchId, request.ExpectedVersion, request.IdempotencyKey,
                Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/submit")]
    public Task<ActionResult<PaymentBatchDetailDto>> SubmitAsync(Guid companyId, Guid batchId,
        [FromBody] PaymentBatchVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.SubmitAsync(new(companyId, batchId, request.ExpectedVersion, request.IdempotencyKey,
                Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{batchId:guid}/approve")]
    public Task<ActionResult<PaymentBatchDetailDto>> ApproveAsync(Guid companyId, Guid batchId,
        [FromBody] DecidePaymentBatchRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.ApproveAsync(new(companyId, batchId, request.ExpectedVersion, request.Comment,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{batchId:guid}/reject")]
    public Task<ActionResult<PaymentBatchDetailDto>> RejectAsync(Guid companyId, Guid batchId,
        [FromBody] DecidePaymentBatchRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.RejectAsync(new(companyId, batchId, request.ExpectedVersion, request.Comment,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/cancel")]
    public Task<ActionResult<PaymentBatchDetailDto>> CancelAsync(Guid companyId, Guid batchId,
        [FromBody] CancelPaymentBatchRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.CancelAsync(new(companyId, batchId, request.ExpectedVersion, request.Reason,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{batchId:guid}/regenerate")]
    public Task<ActionResult<PaymentBatchDetailDto>> RegenerateAsync(Guid companyId, Guid batchId,
        [FromBody] PaymentBatchVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            _service.RegenerateAsync(new(companyId, batchId, request.ExpectedVersion,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (PaymentBatchException ex)
        {
            var status = ex.ReasonCode is PaymentBatchReasonCodes.BatchNotFound or PaymentBatchReasonCodes.ObligationNotFound
                ? 404 : ex.IsConflict ? 409 : 400;
            var problem = Problem(ex.Message, status); problem.Extensions["reasonCode"] = ex.ReasonCode;
            if (ex.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = ex.CurrentVersion.Value;
            return StatusCode(status, problem);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(Problem("The payment batch changed after it was opened.", 409)); }
        catch (DbUpdateException) { return Conflict(Problem("The request conflicts with existing company payment evidence.", 409)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return Conflict(Problem(ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, 400)); }
    }
    private Guid Actor() => _currentUser.UserId ?? throw new UnauthorizedAccessException("A resolved company user is required.");
    private ProblemDetails Problem(string detail, int status) => new() { Title = "Payment batch request failed", Detail = detail, Status = status, Instance = HttpContext.Request.Path };
}

public sealed record RegisterPaymentBeneficiaryRequest(string PartyType, Guid PartyId, string DisplayName,
    string Rail, string Destination, string MaskedDestination, string Currency,
    string VerificationEvidenceReference, string VerificationEvidenceHash);
public sealed record CreatePaymentBatchRequest(string Name, DateOnly PlannedExecutionDate, string IdempotencyKey);
public sealed record AddPaymentBatchObligationRequest(string ObligationType, Guid SourceId,
    long ExpectedVersion, string IdempotencyKey);
public sealed record PaymentBatchVersionedRequest(long ExpectedVersion, string IdempotencyKey);
public sealed record DecidePaymentBatchRequest(long ExpectedVersion, string Comment, string IdempotencyKey);
public sealed record CancelPaymentBatchRequest(long ExpectedVersion, string Reason, string IdempotencyKey);
