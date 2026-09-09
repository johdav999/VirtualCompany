using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesPresentationDecksController(
    ISalesPresentationDeckService decks,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpPost("decks")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(28_311_552)]
    public async Task<ActionResult<SalesPresentationDeckDto>> ImportAsync(
        Guid sessionId,
        [FromForm] ImportSalesPresentationDeckRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext,
                new Dictionary<string, string[]> { ["file"] = ["A PowerPoint presentation is required."] },
                ApiProblemCodes.SalesRequestInvalid));
        }

        try
        {
            await using var content = request.File.OpenReadStream();
            var result = await decks.ImportAsync(
                CompanyId(),
                UserId(),
                sessionId,
                new ImportSalesPresentationDeckCommand(
                    request.AgentId,
                    request.Title,
                    request.File.FileName,
                    request.File.ContentType,
                    request.File.Length,
                    content),
                HttpContext.TraceIdentifier,
                cancellationToken);
            return AcceptedAtAction(nameof(GetAsync), new { sessionId, deckId = result.Id }, result);
        }
        catch (SalesPresentationValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext, exception.Errors, ApiProblemCodes.SalesRequestInvalid));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (SalesPresentationConflictException exception)
        {
            return ConflictProblem(exception);
        }
    }

    [HttpGet("decks")]
    public async Task<ActionResult<IReadOnlyList<SalesPresentationDeckDto>>> ListAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        Ok(await decks.ListAsync(CompanyId(), sessionId, cancellationToken));

    [HttpGet("decks/{deckId:guid}")]
    public async Task<ActionResult<SalesPresentationDeckDto>> GetAsync(
        Guid sessionId,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        var result = await decks.GetAsync(CompanyId(), sessionId, deckId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("decks/{deckId:guid}/slides/{slideNumber:int:min(1)}")]
    public async Task<ActionResult<SalesPresentationSlideDto>> GetSlideAsync(
        Guid sessionId,
        Guid deckId,
        int slideNumber,
        CancellationToken cancellationToken)
    {
        var result = await decks.GetSlideAsync(CompanyId(), sessionId, deckId, slideNumber, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("decks/{deckId:guid}/activate")]
    public Task<ActionResult<SalesPresentationDeckDto>> ActivateAsync(
        Guid sessionId,
        Guid deckId,
        CancellationToken cancellationToken) =>
        MutateAsync(() => decks.ActivateAsync(
            CompanyId(), UserId(), sessionId, deckId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("decks/{deckId:guid}/retry")]
    public Task<ActionResult<SalesPresentationDeckDto>> RetryAsync(
        Guid sessionId,
        Guid deckId,
        CancellationToken cancellationToken) =>
        MutateAsync(() => decks.RetryAsync(
            CompanyId(), UserId(), sessionId, deckId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpGet("brief")]
    public async Task<ActionResult<SalesMeetingBriefDto>> GetBriefAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await decks.GetBriefAsync(CompanyId(), sessionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("brief/regenerate")]
    public Task<ActionResult<SalesPresentationDeckDto>> RegenerateBriefAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        MutateAsync(() => decks.RequestBriefRegenerationAsync(
            CompanyId(), UserId(), sessionId, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<ActionResult<SalesPresentationDeckDto>> MutateAsync(
        Func<Task<SalesPresentationDeckDto?>> mutation)
    {
        try
        {
            var result = await mutation();
            return result is null ? NotFound() : Accepted(result);
        }
        catch (SalesPresentationValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext, exception.Errors, ApiProblemCodes.SalesRequestInvalid));
        }
        catch (SalesPresentationConflictException exception)
        {
            return ConflictProblem(exception);
        }
    }

    private ObjectResult ConflictProblem(SalesPresentationConflictException exception) =>
        Conflict(StableProblemDetails.Create(
            HttpContext,
            StatusCodes.Status409Conflict,
            exception.Code,
            "Sales presentation conflict",
            exception.Message));

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id
        : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id
        : throw new UnauthorizedAccessException("A resolved user is required.");
}

public sealed class ImportSalesPresentationDeckRequest
{
    public Guid AgentId { get; init; }
    public string? Title { get; init; }
    public IFormFile? File { get; init; }
}
