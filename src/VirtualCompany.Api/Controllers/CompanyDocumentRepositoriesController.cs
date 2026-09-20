using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/document-repositories")]
[Authorize(Policy = CompanyPolicies.CompanyAdmin)]
[RequireCompanyContext]
public sealed class CompanyDocumentRepositoriesController(ICompanyDocumentRepositoryService service, ICompanyDocumentRepositoryImportService imports, ICompanyDocumentRepositorySynchronizationService synchronization, ICompanyDocumentPublicationService publicationService) : ControllerBase
{
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

}
