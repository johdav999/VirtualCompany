using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/capture")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesMeetingCaptureController(
    ISalesMeetingCaptureService capture,
    ISalesMeetingQuestionAnsweringService questions,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalesMeetingCaptureSnapshotDto>> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await capture.GetAsync(CompanyId(), UserId(), sessionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("autosave")]
    public Task<ActionResult<SalesMeetingCaptureSaveResultDto>> AutosaveAsync(Guid sessionId,
        [FromBody] AutosaveSalesMeetingCaptureRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => capture.AutosaveAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("questions")]
    public Task<IReadOnlyList<SalesMeetingQuestionDto>> ListQuestionsAsync(Guid sessionId, CancellationToken cancellationToken) =>
        questions.ListQuestionsAsync(CompanyId(), UserId(), sessionId, cancellationToken);

    [HttpGet("stage-answers")]
    public Task<IReadOnlyList<SalesMeetingStageAnswerDto>> ListStageAnswersAsync(Guid sessionId, CancellationToken cancellationToken) =>
        questions.ListStageAnswersAsync(CompanyId(), UserId(), sessionId, cancellationToken);

    [HttpGet("questions/{questionId:guid}")]
    public async Task<ActionResult<SalesMeetingQuestionDto>> GetQuestionAsync(Guid sessionId, Guid questionId, CancellationToken cancellationToken)
    {
        var result = await questions.GetQuestionAsync(CompanyId(), UserId(), sessionId, questionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("questions")]
    public Task<ActionResult<SalesMeetingQuestionDto>> AskAsync(Guid sessionId,
        [FromBody] AskSalesMeetingQuestionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => questions.AskAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("questions/{questionId:guid}/approve-for-stage")]
    public Task<ActionResult<SalesMeetingQuestionDto>> ApproveForStageAsync(Guid sessionId, Guid questionId,
        [FromBody] ApproveSalesMeetingAnswerForStageRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => questions.ApproveForStageAsync(CompanyId(), UserId(), sessionId, questionId,
            request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            var result = await action();
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesMeetingCaptureValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(HttpContext, exception.Errors, SalesMeetingCaptureProblemCodes.InvalidRequest));
        }
        catch (SalesMeetingCaptureConflictException exception)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, exception.Code,
                "Sales meeting capture conflict", exception.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
