using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/closing")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesMeetingClosingController(
    ISalesMeetingClosingService closing,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpPost("prepare")]
    public Task<ActionResult<SalesMeetingClosingSnapshotDto>> PrepareAsync(Guid sessionId,
        [FromBody] PrepareSalesMeetingClosingRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.PrepareAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("regenerate")]
    public Task<ActionResult<SalesMeetingClosingSnapshotDto>> RegenerateAsync(Guid sessionId,
        [FromBody] PrepareSalesMeetingClosingRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.PrepareAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("customer-minutes")]
    public Task<IReadOnlyList<SalesMeetingMinutesDto>> ListMinutesAsync(Guid sessionId, CancellationToken cancellationToken) =>
        closing.ListMinutesAsync(CompanyId(), UserId(), sessionId, cancellationToken);

    [HttpGet("customer-minutes/{minutesId:guid}")]
    public Task<ActionResult<SalesMeetingMinutesDto>> GetMinutesAsync(Guid sessionId, Guid minutesId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.GetMinutesAsync(CompanyId(), UserId(), sessionId, minutesId, cancellationToken));

    [HttpGet("customer-preview/{minutesId:guid}")]
    public Task<ActionResult<SalesMeetingCustomerMinutesPreviewDto>> GetCustomerPreviewAsync(Guid sessionId, Guid minutesId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.GetCustomerPreviewAsync(CompanyId(), UserId(), sessionId, minutesId, cancellationToken));

    [HttpPut("customer-minutes/{minutesId:guid}")]
    public Task<ActionResult<SalesMeetingMinutesDto>> EditMinutesAsync(Guid sessionId, Guid minutesId,
        [FromBody] EditSalesMeetingMinutesRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.EditMinutesAsync(CompanyId(), UserId(), sessionId, minutesId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("customer-minutes/{minutesId:guid}/submit-review")]
    public Task<ActionResult<SalesMeetingMinutesDto>> SubmitMinutesAsync(Guid sessionId, Guid minutesId,
        [FromBody] ReviewSalesMeetingClosingArtifactRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.SubmitMinutesAsync(CompanyId(), UserId(), sessionId, minutesId, request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("customer-minutes/{minutesId:guid}/approve")]
    public Task<ActionResult<SalesMeetingMinutesDto>> ApproveMinutesAsync(Guid sessionId, Guid minutesId,
        [FromBody] ReviewSalesMeetingClosingArtifactRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.ApproveMinutesAsync(CompanyId(), UserId(), sessionId, minutesId, request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("internal-intelligence/{intelligenceId:guid}")]
    public Task<ActionResult<SalesMeetingInternalIntelligenceDto>> GetInternalAsync(Guid sessionId, Guid intelligenceId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.GetInternalAsync(CompanyId(), UserId(), sessionId, intelligenceId, cancellationToken));

    [HttpPut("internal-intelligence/{intelligenceId:guid}")]
    public Task<ActionResult<SalesMeetingInternalIntelligenceDto>> EditInternalAsync(Guid sessionId, Guid intelligenceId,
        [FromBody] EditSalesMeetingInternalIntelligenceRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.EditInternalAsync(CompanyId(), UserId(), sessionId, intelligenceId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("internal-intelligence/{intelligenceId:guid}/submit-review")]
    public Task<ActionResult<SalesMeetingInternalIntelligenceDto>> SubmitInternalAsync(Guid sessionId, Guid intelligenceId,
        [FromBody] ReviewSalesMeetingClosingArtifactRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.SubmitInternalAsync(CompanyId(), UserId(), sessionId, intelligenceId, request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("internal-intelligence/{intelligenceId:guid}/approve")]
    public Task<ActionResult<SalesMeetingInternalIntelligenceDto>> ApproveInternalAsync(Guid sessionId, Guid intelligenceId,
        [FromBody] ReviewSalesMeetingClosingArtifactRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.ApproveInternalAsync(CompanyId(), UserId(), sessionId, intelligenceId, request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("complete")]
    public Task<ActionResult<SalesMeetingSessionResponse>> CompleteAsync(Guid sessionId,
        [FromBody] CompleteSalesMeetingClosingRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => closing.CompleteAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T?>> action) where T : class
    {
        try { var result = await action(); return result is null ? NotFound() : Ok(result); }
        catch (SalesMeetingClosingValidationException exception) { return BadRequest(StableProblemDetails.CreateValidation(HttpContext, exception.Errors, SalesMeetingClosingProblemCodes.InvalidRequest)); }
        catch (SalesMeetingClosingConflictException exception) { return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, exception.Code, "Sales meeting closing conflict", exception.Message)); }
        catch (ArgumentException exception) { return BadRequest(StableProblemDetails.CreateValidation(HttpContext, new Dictionary<string, string[]> { ["request"] = [exception.Message] }, SalesMeetingClosingProblemCodes.InvalidRequest)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
