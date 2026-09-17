using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/presentation-presets")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesPresentationPresetsController(ISalesPresentationPresetService presets,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalesPresentationPresetListItemDto>>> ListAsync(
        [FromQuery] string? search, [FromQuery] bool includeArchived, CancellationToken ct) =>
        Ok(await presets.ListAsync(CompanyId(), UserId(), search, includeArchived, ct));

    [HttpGet("{presetId:guid}")]
    public async Task<ActionResult<SalesPresentationPresetDto>> GetAsync(Guid presetId, CancellationToken ct)
    { var result = await presets.GetAsync(CompanyId(), UserId(), presetId, ct); return result is null ? NotFound() : Ok(result); }

    [HttpGet("{presetId:guid}/cover/{slideId:guid}")]
    public async Task<IActionResult> CoverAsync(Guid presetId, Guid slideId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var cover = await presets.GetCoverAsync(CompanyId(), UserId(), presetId, slideId, ct);
        return cover is null ? NotFound() : File(cover.Content, cover.ContentType);
    }

    [HttpGet("{presetId:guid}/versions/{versionId:guid}/slides/{slideId:guid}/image")]
    public async Task<IActionResult> SlideImageAsync(Guid presetId, Guid versionId, Guid slideId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var image = await presets.GetSlideImageAsync(CompanyId(), UserId(), presetId, versionId, slideId, ct);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    [HttpPost]
    public async Task<ActionResult<SalesPresentationPresetDto>> CreateAsync(
        [FromBody] CreateSalesPresentationPresetRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.CreateAsync(CompanyId(), UserId(), request.ToCommand(),
            HttpContext.TraceIdentifier, ct), true));

    [HttpPut("{presetId:guid}/draft")]
    public async Task<ActionResult<SalesPresentationPresetDto>> UpdateDraftAsync(Guid presetId,
        [FromBody] UpdateSalesPresentationPresetDraftRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.UpdateDraftAsync(CompanyId(), UserId(), presetId,
            request.ToCommand(), HttpContext.TraceIdentifier, ct), false));

    [HttpPost("{presetId:guid}/versions/{versionId:guid}/asset")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(28_311_552)]
    public async Task<ActionResult<SalesPresentationPresetAssetDto>> ImportAssetAsync(Guid presetId, Guid versionId,
        [FromForm] ImportSalesPresentationPresetAssetRequest request, CancellationToken ct)
    {
        if (request.File is null) return BadRequest(StableProblemDetails.CreateValidation(HttpContext,
            new Dictionary<string, string[]> { ["file"] = ["A PowerPoint presentation is required."] }, ApiProblemCodes.SalesRequestInvalid));
        try { await using var content = request.File.OpenReadStream(); var result = await presets.ImportAssetAsync(CompanyId(), UserId(), presetId, versionId, new(request.File.FileName, request.File.ContentType, request.File.Length, content), HttpContext.TraceIdentifier, ct); return result is null ? NotFound() : Accepted(result); }
        catch (SalesPresentationPresetValidationException e) { return Validation(e); }
        catch (SalesPresentationPresetConflictException e) { return ConflictResult(e); }
    }

    [HttpPost("{presetId:guid}/versions/{versionId:guid}/asset/retry")]
    public async Task<ActionResult<SalesPresentationPresetAssetDto>> RetryAsync(Guid presetId, Guid versionId, CancellationToken ct)
    {
        try { var result = await presets.RetryAssetAsync(CompanyId(), UserId(), presetId, versionId, HttpContext.TraceIdentifier, ct); return result is null ? NotFound() : Accepted(result); }
        catch (SalesPresentationPresetConflictException e) { return ConflictResult(e); }
    }

    [HttpGet("{presetId:guid}/versions/{versionId:guid}/readiness")]
    public async Task<ActionResult<SalesPresentationPresetReadinessDto>> ReadinessAsync(Guid presetId, Guid versionId, CancellationToken ct)
    { var result = await presets.EvaluateReadinessAsync(CompanyId(), UserId(), presetId, versionId, ct); return result is null ? NotFound() : Ok(result); }

    [HttpGet("{presetId:guid}/versions/{versionId:guid}/slides")]
    public async Task<ActionResult<IReadOnlyList<SalesPresentationPresetSlideDto>>> SlidesAsync(Guid presetId, Guid versionId, CancellationToken ct)
    { var result = await presets.GetSlidesAsync(CompanyId(), UserId(), presetId, versionId, ct); return result is null ? NotFound() : Ok(result); }

    [HttpPut("{presetId:guid}/versions/{versionId:guid}/slides/{slideId:guid}/speaker-notes")]
    public async Task<ActionResult<SalesPresentationPresetDto>> UpdateSpeakerNotesAsync(Guid presetId, Guid versionId, Guid slideId,
        [FromBody] UpdatePresetSpeakerNotesRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.UpdateSpeakerNotesAsync(CompanyId(), UserId(), presetId, versionId, slideId,
            request.ExpectedVersion, request.SpeakerNotes, HttpContext.TraceIdentifier, ct), false));

    [HttpPost("{presetId:guid}/versions/{versionId:guid}/publish")]
    public async Task<ActionResult<SalesPresentationPresetDto>> PublishAsync(Guid presetId, Guid versionId,
        [FromBody] PublishSalesPresentationPresetRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.PublishAsync(CompanyId(), UserId(), presetId, versionId,
            request.ExpectedPresetVersion, request.ExpectedVersion, HttpContext.TraceIdentifier, ct), false));

    [HttpPost("{presetId:guid}/versions")]
    public async Task<ActionResult<SalesPresentationPresetDto>> CreateVersionAsync(Guid presetId,
        [FromBody] CreateSalesPresentationPresetVersionRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.CreateNextDraftAsync(CompanyId(), UserId(), presetId,
            request.ExpectedPresetVersion, HttpContext.TraceIdentifier, ct), true));

    [HttpPost("{presetId:guid}/archive")]
    public async Task<ActionResult<SalesPresentationPresetDto>> ArchiveAsync(Guid presetId,
        [FromBody] ArchiveSalesPresentationPresetRequest request, CancellationToken ct) =>
        await ExecuteAsync(async () => (await presets.ArchiveAsync(CompanyId(), UserId(), presetId,
            request.ExpectedPresetVersion, request.Rationale, HttpContext.TraceIdentifier, ct), false));

    private async Task<ActionResult<SalesPresentationPresetDto>> ExecuteAsync(Func<Task<(SalesPresentationPresetDto? Value, bool Created)>> action)
    {
        try { var (value, created) = await action(); if (value is null) return NotFound(); return created ? CreatedAtAction(nameof(GetAsync), new { presetId = value.Id }, value) : Ok(value); }
        catch (SalesPresentationPresetValidationException e) { return Validation(e); }
        catch (SalesPresentationPresetConflictException e) { return ConflictResult(e); }
    }
    private ObjectResult Validation(SalesPresentationPresetValidationException e) => BadRequest(StableProblemDetails.CreateValidation(HttpContext, e.Errors, ApiProblemCodes.SalesRequestInvalid));
    private ObjectResult ConflictResult(SalesPresentationPresetConflictException e) => Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, e.Code, "Presentation preset conflict", e.Message));
    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}

public sealed record CreateSalesPresentationPresetRequest(string Name, string? Description, Guid OwnerUserId,
    Guid? DefaultPresenterAgentId, string Goal, string Audience, int DurationMinutes, string? DemoScenario,
    string ControlMode, string Language, IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope,
    string? BehaviorSettingsJson)
{
    public CreateSalesPresentationPresetCommand ToCommand() => new(Name, Description, OwnerUserId, DefaultPresenterAgentId,
        Goal, Audience, DurationMinutes, DemoScenario, ControlMode, Language, AllowedContextTypes, RequiredKnowledgeScope, BehaviorSettingsJson);
}
public sealed record UpdateSalesPresentationPresetDraftRequest(long ExpectedPresetVersion, long ExpectedDraftVersion,
    string Name, string? Description, Guid OwnerUserId, Guid? DefaultPresenterAgentId, string Goal, string Audience,
    int DurationMinutes, string? DemoScenario, string ControlMode, string Language,
    IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope, string? BehaviorSettingsJson)
{
    public UpdateSalesPresentationPresetDraftCommand ToCommand() => new(ExpectedPresetVersion, ExpectedDraftVersion,
        Name, Description, OwnerUserId, DefaultPresenterAgentId, Goal, Audience, DurationMinutes, DemoScenario, ControlMode,
        Language, AllowedContextTypes, RequiredKnowledgeScope, BehaviorSettingsJson);
}
public sealed class ImportSalesPresentationPresetAssetRequest { public IFormFile? File { get; init; } }
public sealed record PublishSalesPresentationPresetRequest(long ExpectedPresetVersion, long ExpectedVersion);
public sealed record UpdatePresetSpeakerNotesRequest(long ExpectedVersion, string? SpeakerNotes);
public sealed record CreateSalesPresentationPresetVersionRequest(long ExpectedPresetVersion);
public sealed record ArchiveSalesPresentationPresetRequest(long ExpectedPresetVersion, string? Rationale);
