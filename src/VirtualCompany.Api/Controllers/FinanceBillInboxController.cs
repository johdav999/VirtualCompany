using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/bill-inbox")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceBillInboxController : ControllerBase
{
    private readonly IFinanceBillInboxService _service;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<FinanceBillInboxController> _logger;

    public FinanceBillInboxController(
        IFinanceBillInboxService service,
        IAuthorizationService authorizationService,
        ILogger<FinanceBillInboxController> logger)
    {
        _service = service;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinanceBillInboxRowDto>>> GetInboxAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        Ok(await _service.GetInboxAsync(new GetFinanceBillInboxQuery(companyId, limit <= 0 ? 100 : limit), cancellationToken));

    [HttpGet("{billId:guid}")]
    public async Task<ActionResult<FinanceBillInboxDetailDto>> GetDetailAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        var detail = await _service.GetDetailAsync(new GetFinanceBillInboxDetailQuery(companyId, billId), cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var canSendDirect = (await _authorizationService.AuthorizeAsync(User, companyId, CompanyPolicies.FinanceApproval)).Succeeded;
        return Ok(ApplyFortnoxRegistrationPermissions(detail, canSendDirect));
    }

    [HttpPost("{billId:guid}/approve")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<FinanceBillReviewActionResultDto>> ApproveAsync(
        Guid companyId,
        Guid billId,
        [FromBody] FinanceBillReviewActionRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionAsync(
            request,
            () => _service.ApproveAsync(
                new ApproveFinanceBillCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName(), request.Rationale),
                cancellationToken),
            "Finance bill approval blocked");
    }

    [HttpPost("{billId:guid}/reject")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public Task<ActionResult<FinanceBillReviewActionResultDto>> RejectAsync(
        Guid companyId,
        Guid billId,
        [FromBody] FinanceBillReviewActionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteReviewActionAsync(
            request,
            () => _service.RejectAsync(
                new RejectFinanceBillCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName(), request.Rationale),
                cancellationToken),
            "Finance bill rejection blocked");

    [HttpPost("{billId:guid}/request-clarification")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public Task<ActionResult<FinanceBillReviewActionResultDto>> RequestClarificationAsync(
        Guid companyId,
        Guid billId,
        [FromBody] FinanceBillReviewActionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteReviewActionAsync(
            request,
            () => _service.RequestClarificationAsync(
                new RequestFinanceBillClarificationCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName(), request.Rationale),
                cancellationToken),
            "Finance bill clarification request blocked");

    [HttpPost("{billId:guid}/fortnox-registration/request")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public Task<ActionResult<FinanceBillFortnoxRegistrationDto>> RequestFortnoxRegistrationAsync(
        Guid companyId,
        Guid billId,
        [FromBody] FinanceBillReviewActionRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Bill inbox Fortnox registration request endpoint hit. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. HasRationale: {HasRationale}.",
            companyId,
            billId,
            ResolveActorId(),
            !string.IsNullOrWhiteSpace(request.Rationale));

        return ExecuteFortnoxRegistrationActionAsync(
            request,
            () => _service.RequestFortnoxRegistrationAsync(
                new RequestFinanceBillFortnoxRegistrationCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName(), request.Rationale),
                cancellationToken),
            "Fortnox registration request blocked");
    }

    [HttpPost("{billId:guid}/fortnox-registration/send")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public Task<ActionResult<FinanceBillFortnoxRegistrationDto>> SendFortnoxRegistrationAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Bill inbox Fortnox registration send endpoint hit. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}.",
            companyId,
            billId,
            ResolveActorId());

        return ExecuteFortnoxRegistrationActionAsync(
            new FinanceBillReviewActionRequest(string.Empty),
            () => _service.SendFortnoxRegistrationDirectAsync(
                new ExecuteFinanceBillFortnoxRegistrationCommand(companyId, billId, ResolveActorId()),
                cancellationToken),
            "Fortnox registration send blocked");
    }

    [HttpGet("{billId:guid}/approval-automation")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierApprovalAutomationDto>> GetApprovalAutomationAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken) =>
        Ok(await _service.GetApprovalAutomationAsync(
            new GetSupplierApprovalAutomationQuery(companyId, billId),
            cancellationToken));

    [HttpPut("{billId:guid}/approval-automation/{stage}")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierApprovalAutomationDto>> SetApprovalAutomationAsync(
        Guid companyId,
        Guid billId,
        string stage,
        [FromBody] SetSupplierApprovalAutomationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SetApprovalAutomationAsync(
                new SetSupplierApprovalAutomationCommand(
                    companyId,
                    billId,
                    stage,
                    request.Enabled,
                    ResolveActorId(),
                    ResolveActorDisplayName()),
                cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(stage)] = [ex.Message]
            })
            {
                Title = "Supplier approval automation could not be changed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
    }

    private static FinanceBillInboxDetailDto ApplyFortnoxRegistrationPermissions(FinanceBillInboxDetailDto detail, bool canSendDirect)
    {
        if (detail.FortnoxRegistration is null)
        {
            return detail;
        }

        var registration = detail.FortnoxRegistration;
        if (string.Equals(registration.ActionKind, "supplier_creation", StringComparison.OrdinalIgnoreCase))
        {
            return detail with
            {
                FortnoxRegistration = registration with
                {
                    CanSendDirect = canSendDirect && registration.CanSendDirect,
                    CanRequest = registration.CanRequest
                }
            };
        }

        var canSend = canSendDirect && registration.CanSendDirect;
        return detail with
        {
            FortnoxRegistration = registration with
            {
                CanSendDirect = canSend,
                CanRequest = !canSendDirect && registration.CanRequest
            }
        };
    }

    private async Task<ActionResult<FinanceBillReviewActionResultDto>> ExecuteReviewActionAsync(
        FinanceBillReviewActionRequest request,
        Func<Task<FinanceBillReviewActionResultDto>> action,
        string title)
    {
        try
        {
            return Ok(await action());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Rationale)] = [ex.Message]
            })
            {
                Title = title,
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
    }

    private async Task<ActionResult<FinanceBillFortnoxRegistrationDto>> ExecuteFortnoxRegistrationActionAsync(
        FinanceBillReviewActionRequest request,
        Func<Task<FinanceBillFortnoxRegistrationDto>> action,
        string title)
    {
        try
        {
            var result = await action();
            _logger.LogInformation(
                "Bill inbox Fortnox registration action completed. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}. CanExecute: {CanExecute}. HasPendingRequest: {HasPendingRequest}. HasExecuted: {HasExecuted}.",
                result.WriteRequestId,
                result.ApprovalId,
                result.Status,
                result.CanExecute,
                result.HasPendingRequest,
                result.HasExecuted);

            return Ok(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or FortnoxApprovalRequiredException or FortnoxApiException)
        {
            _logger.LogWarning(
                ex,
                "Bill inbox Fortnox registration action was blocked. Title: {Title}. Path: {Path}.",
                title,
                HttpContext.Request.Path);
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Rationale)] = [ex.Message]
            })
            {
                Title = title,
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
    }

    private Guid? ResolveActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private string ResolveActorDisplayName() =>
        User.Identity?.Name ??
        User.FindFirstValue("name") ??
        User.FindFirstValue(ClaimTypes.Email) ??
        "Finance user";
}

public sealed record SetSupplierApprovalAutomationRequest(bool Enabled);

public sealed record FinanceBillReviewActionRequest(string Rationale);
