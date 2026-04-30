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

    public FinanceBillInboxController(IFinanceBillInboxService service)
    {
        _service = service;
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
        return detail is null ? NotFound() : Ok(detail);
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

public sealed record FinanceBillReviewActionRequest(string Rationale);
