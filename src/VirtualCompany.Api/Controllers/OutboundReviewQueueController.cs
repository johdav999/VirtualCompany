using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/review-queue/outbound")]
[Authorize(Policy = CompanyPolicies.CompanyManager)]
[RequireCompanyContext]
public sealed class OutboundReviewQueueController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IOutboundReviewQueueService _reviews;

    public OutboundReviewQueueController(ICompanyContextAccessor companyContextAccessor, IOutboundReviewQueueService reviews)
    {
        _companyContextAccessor = companyContextAccessor;
        _reviews = reviews;
    }

    [HttpGet]
    public Task<IReadOnlyList<OutboundReviewQueueItemResponse>> ListAsync(CancellationToken cancellationToken) =>
        _reviews.ListPendingAsync(CompanyId(), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OutboundReviewQueueDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _reviews.GetAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<OutboundReviewQueueDetailResponse>> ApproveAsync(Guid id, [FromBody] OutboundReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reviews.ApproveAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Review item could not be approved.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<OutboundReviewQueueDetailResponse>> RejectAsync(Guid id, [FromBody] OutboundReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reviews.RejectAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Review item could not be rejected.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("{id:guid}/edit-and-approve")]
    public async Task<ActionResult<OutboundReviewQueueDetailResponse>> EditAndApproveAsync(Guid id, [FromBody] OutboundEditAndApproveRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reviews.EditAndApproveAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Review item could not be edited.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(new Dictionary<string, string[]> { ["message"] = [ex.Message] });
        }
    }

    private Guid CompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty ? companyId : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty ? userId : throw new UnauthorizedAccessException("A resolved user is required.");

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed."
        });
}
