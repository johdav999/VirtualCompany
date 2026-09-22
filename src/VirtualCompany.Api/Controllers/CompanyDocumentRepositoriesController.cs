using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/document-repositories")]
[Authorize(Policy = CompanyPolicies.CompanyAdmin)]
[RequireCompanyContext]
public sealed class CompanyDocumentRepositoriesController(ICompanyDocumentRepositoryService service, ICompanyDocumentRepositoryImportService imports, ICompanyDocumentRepositorySynchronizationService synchronization, ICompanyDocumentPublicationService publicationService, IDocumentRepositoryMicrosoftOnboardingService onboarding) : ControllerBase
{
    [HttpPost("microsoft/onboarding")]
    public async Task<ActionResult<Microsoft365OnboardingStartDto>> BeginMicrosoftOnboardingAsync(Guid companyId, [FromBody] BeginMicrosoft365OnboardingCommand command, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.BeginAsync(companyId, command, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid Microsoft 365 setup request", Detail = exception.Message, Instance = Request.Path }); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}")]
    public async Task<ActionResult<Microsoft365OnboardingStatusDto>> GetMicrosoftOnboardingAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        var status = await onboarding.GetStatusAsync(companyId, sessionHandle, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/source-kinds")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<IReadOnlyList<Microsoft365SourceKindDto>>> GetMicrosoftSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.GetSourceKindsAsync(companyId, sessionHandle, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/sources/onedrive")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<Microsoft365SourcePageDto>> GetMicrosoftOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.GetOneDriveSourcesAsync(companyId, sessionHandle, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/sources/sharepoint/sites")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<Microsoft365SourcePageDto>> SearchMicrosoftSharePointSitesAsync(Guid companyId, string sessionHandle, [FromQuery] string query, [FromQuery] string? pageHandle, [FromQuery] int maxItems = 25, CancellationToken cancellationToken = default)
    {
        try { return Ok(await onboarding.SearchSharePointSitesAsync(companyId, sessionHandle, query, pageHandle, maxItems, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/sources/sharepoint/libraries")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<Microsoft365SourcePageDto>> GetMicrosoftSharePointLibrariesAsync(Guid companyId, string sessionHandle, [FromQuery] string siteHandle, [FromQuery] string? pageHandle, [FromQuery] int maxItems = 25, CancellationToken cancellationToken = default)
    {
        try { return Ok(await onboarding.GetSharePointLibrariesAsync(companyId, sessionHandle, siteHandle, pageHandle, maxItems, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/folders")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<Microsoft365FolderPageDto>> BrowseMicrosoftFoldersAsync(Guid companyId, string sessionHandle, [FromQuery] string sourceHandle, [FromQuery] string? folderHandle, [FromQuery] string? pageHandle, [FromQuery] int maxItems = 50, CancellationToken cancellationToken = default)
    {
        try { return Ok(await onboarding.BrowseFoldersAsync(companyId, sessionHandle, sourceHandle, folderHandle, pageHandle, maxItems, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpPost("microsoft/onboarding/{sessionHandle}/selection")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<Microsoft365RepositorySelectionDto>> SelectMicrosoftRootAsync(Guid companyId, string sessionHandle, [FromBody] SelectMicrosoft365RepositoryRootCommand command, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.SelectRootAsync(companyId, sessionHandle, command, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpPost("microsoft/onboarding/{sessionHandle}/cancel")]
    public async Task<IActionResult> CancelMicrosoftOnboardingAsync(Guid companyId, string sessionHandle, [FromBody] CancelMicrosoftOnboardingRequest request, CancellationToken cancellationToken)
    {
        try { await onboarding.CancelAsync(companyId, sessionHandle, request.ExpectedConcurrencyVersion, cancellationToken); return NoContent(); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    public sealed record CancelMicrosoftOnboardingRequest(long ExpectedConcurrencyVersion);

    [HttpPost("microsoft/onboarding/{sessionHandle}/access")]
    public async Task<ActionResult<Microsoft365RepositoryAccessDraftDto>> ConfigureMicrosoftAccessAsync(Guid companyId, string sessionHandle, [FromBody] ConfigureMicrosoft365RepositoryAccessCommand command, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.ConfigureAccessAsync(companyId, sessionHandle, command, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/review")]
    public async Task<ActionResult<Microsoft365RepositoryReviewDto>> GetMicrosoftReviewAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.GetReviewAsync(companyId, sessionHandle, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpPost("microsoft/onboarding/{sessionHandle}/finalize")]
    public async Task<ActionResult<Microsoft365RepositoryProvisioningDto>> FinalizeMicrosoftRepositoryAsync(Guid companyId, string sessionHandle, [FromBody] FinalizeMicrosoft365RepositoryCommand command, CancellationToken cancellationToken)
    {
        try { return Accepted(await onboarding.FinalizeAsync(companyId, sessionHandle, command, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpGet("microsoft/onboarding/{sessionHandle}/provisioning")]
    public async Task<ActionResult<Microsoft365RepositoryProvisioningDto>> GetMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        var value = await onboarding.GetProvisioningAsync(companyId, sessionHandle, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost("microsoft/onboarding/{sessionHandle}/provisioning/retry")]
    public async Task<ActionResult<Microsoft365RepositoryProvisioningDto>> RetryMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken)
    {
        try { return Accepted(await onboarding.RetryProvisioningAsync(companyId, sessionHandle, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }

    [HttpPost("microsoft/onboarding/{sessionHandle}/provisioning/cleanup")]
    public async Task<ActionResult<Microsoft365RepositoryProvisioningDto>> CleanupMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, [FromBody] CleanupMicrosoft365RepositoryProvisioningCommand command, CancellationToken cancellationToken)
    {
        try { return Ok(await onboarding.CleanupProvisioningAsync(companyId, sessionHandle, command, cancellationToken)); }
        catch (DocumentRepositoryOnboardingException exception) { return OnboardingProblem(exception); }
    }
    [HttpPost]
    public async Task<ActionResult<DocumentRepositoryConnectionDto>> CreateAsync(Guid companyId, [FromBody] ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await service.CreateAsync(companyId, command, cancellationToken);
        return CreatedAtAction(nameof(GetAsync), new { companyId, connectionId = connection.Id }, connection);
    }

    [HttpPut("{connectionId:guid}")]
    public Task<DocumentRepositoryConnectionDto> UpdateAsync(Guid companyId, Guid connectionId, [FromBody] ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken) =>
        service.UpdateAsync(companyId, connectionId, command, cancellationToken);

    [HttpGet]
    public Task<IReadOnlyList<DocumentRepositoryConnectionDto>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        service.ListAsync(companyId, cancellationToken);

    [HttpGet("{connectionId:guid}")]
    public async Task<ActionResult<DocumentRepositoryConnectionDto>> GetAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await service.GetAsync(companyId, connectionId, cancellationToken);
        return connection is null ? NotFound() : Ok(connection);
    }

    [HttpPost("{connectionId:guid}/validate")]
    public Task<DocumentRepositoryValidationResult> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken) =>
        service.ValidateAsync(companyId, connectionId, cancellationToken);

    [HttpGet("{connectionId:guid}/browse")]
    public Task<DocumentRepositoryBrowseResult> BrowseAsync(Guid companyId, Guid connectionId, [FromQuery] string? parentItemId, [FromQuery] int maxItems = 100, CancellationToken cancellationToken = default) =>
        service.BrowseAsync(companyId, connectionId, parentItemId, maxItems, cancellationToken);

    [HttpPost("{connectionId:guid}/disconnect")]
    public async Task<IActionResult> DisconnectAsync(Guid companyId, Guid connectionId, [FromBody] DisconnectDocumentRepositoryRequest request, CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(companyId, connectionId, request.ExpectedConcurrencyVersion, cancellationToken);
        return NoContent();
    }

    public sealed record DisconnectDocumentRepositoryRequest(long ExpectedConcurrencyVersion);

    [HttpPost("{connectionId:guid}/pause")]
    public Task<DocumentRepositoryConnectionDto> SetPauseAsync(Guid companyId, Guid connectionId,
        [FromBody] SetDocumentRepositoryPauseCommand command, CancellationToken cancellationToken) =>
        service.SetPauseAsync(companyId, connectionId, command, cancellationToken);

    [HttpPost("{connectionId:guid}/imports")]
    public async Task<ActionResult<DocumentRepositoryImportJobDto>> StartImportAsync(Guid companyId, Guid connectionId, [FromBody] StartDocumentRepositoryImportCommand command, CancellationToken cancellationToken)
    {
        var job = await imports.StartAsync(companyId, connectionId, command, cancellationToken);
        return AcceptedAtAction(nameof(GetImportAsync), new { companyId, connectionId, jobId = job.Id }, job);
    }

    [HttpGet("{connectionId:guid}/imports/{jobId:guid}")]
    public async Task<ActionResult<DocumentRepositoryImportJobDto>> GetImportAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await imports.GetAsync(companyId, connectionId, jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("{connectionId:guid}/synchronizations")]
    public async Task<ActionResult<DocumentRepositorySynchronizationJobDto>> StartSynchronizationAsync(Guid companyId, Guid connectionId, [FromBody] StartDocumentRepositorySynchronizationCommand command, CancellationToken cancellationToken)
    {
        var job = await synchronization.StartAsync(companyId, connectionId, command, cancellationToken);
        return AcceptedAtAction(nameof(GetSynchronizationAsync), new { companyId, connectionId, jobId = job.Id }, job);
    }

    [HttpGet("{connectionId:guid}/synchronizations/{jobId:guid}")]
    public async Task<ActionResult<DocumentRepositorySynchronizationJobDto>> GetSynchronizationAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await synchronization.GetAsync(companyId, connectionId, jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("{connectionId:guid}/synchronizations/{jobId:guid}/cancel")]
    public async Task<IActionResult> CancelSynchronizationAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken)
    {
        await synchronization.CancelAsync(companyId, connectionId, jobId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{connectionId:guid}/recovery/retry-failed-items")]
    public async Task<ActionResult<DocumentRepositorySynchronizationJobDto>> RetryFailedItemsAsync(Guid companyId, Guid connectionId,
        [FromBody] RetryDocumentRepositoryFailuresCommand command, CancellationToken cancellationToken)
    {
        var job = await synchronization.RetryFailedItemsAsync(companyId, connectionId, command, cancellationToken);
        return AcceptedAtAction(nameof(GetSynchronizationAsync), new { companyId, connectionId, jobId = job.Id }, job);
    }
    [HttpGet("publications/{publicationRequestId:guid}")]
    public async Task<ActionResult<DocumentPublicationRequestDto>> GetPublicationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var publication = await publicationService.GetAsync(companyId, publicationRequestId, cancellationToken);
        return publication is null ? NotFound() : Ok(publication);
    }

    [HttpGet("publications/{publicationRequestId:guid}/content")]
    public async Task<IActionResult> DownloadPublicationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var publication = await publicationService.GetAsync(companyId, publicationRequestId, cancellationToken);
        if (publication is null) return NotFound();
        var content = await publicationService.OpenStagedAsync(companyId, publicationRequestId, cancellationToken);
        return File(content, publication.ContentType ?? "application/octet-stream", publication.FileName, enableRangeProcessing: true);
    }

    [HttpGet("publications/{publicationRequestId:guid}/original")]
    public async Task<IActionResult> DownloadPublicationOriginalAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var publication = await publicationService.GetAsync(companyId, publicationRequestId, cancellationToken);
        if (publication is null) return NotFound();
        var content = await publicationService.OpenOriginalAsync(companyId, publicationRequestId, cancellationToken);
        return File(content, publication.ContentType ?? "application/octet-stream", $"original-{publication.FileName}", enableRangeProcessing: true);
    }

    [HttpGet("publications/{publicationRequestId:guid}/review")]
    public async Task<IActionResult> ReviewPublicationUpdateAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var review = await publicationService.GetUpdateReviewAsync(companyId, publicationRequestId, cancellationToken);
        if (!review.HasTextDiff || string.IsNullOrWhiteSpace(review.UnifiedDiff)) return Ok(review);
        return Content(review.UnifiedDiff, "text/plain; charset=utf-8");
    }

    [HttpPost("publications/{publicationRequestId:guid}/reconcile")]
    public async Task<ActionResult<DocumentPublicationRequestDto>> ReconcilePublicationAsync(Guid companyId,
        Guid publicationRequestId, CancellationToken cancellationToken)
    {
        var publication = await publicationService.RequestReconciliationAsync(companyId, publicationRequestId, cancellationToken);
        return Accepted(publication);
    }

    private ObjectResult OnboardingProblem(DocumentRepositoryOnboardingException exception)
    {
        var status = exception.Code == "configuration_unavailable" || exception.Code == "provider_unavailable" ? StatusCodes.Status503ServiceUnavailable :
            exception.Code == "provider_throttled" ? StatusCodes.Status429TooManyRequests :
            exception.Code == "discovery_access_denied" ? StatusCodes.Status403Forbidden :
            exception.Code == "source_unavailable" ? StatusCodes.Status404NotFound :
            exception.Code is "invalid_selection" or "invalid_page_size" or "invalid_discovery_query" ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict;
        var problem = new ProblemDetails { Status = status, Title = "Microsoft 365 connection unavailable", Detail = exception.SafeMessage, Instance = Request.Path };
        problem.Extensions["code"] = exception.Code;
        return StatusCode(status, problem);
    }

}
