using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    private IAuditPackageService AuditPackages => HttpContext.RequestServices.GetRequiredService<IAuditPackageService>();

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/audit-packages")]
    public Task<ActionResult<AuditPackageWorkspaceDto>> ListAuditPackagesAsync(Guid companyId,
        [FromQuery] Guid? fiscalPeriodId, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        AuditPackages.ListAsync(new(companyId, fiscalPeriodId, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/audit-packages/{packageId:guid}")]
    public Task<ActionResult<AuditPackageDto>> GetAuditPackageAsync(Guid companyId, Guid packageId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        AuditPackages.GetAsync(companyId, packageId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/audit-packages")]
    public Task<ActionResult<AuditPackageDto>> RequestAuditPackageAsync(Guid companyId,
        [FromBody] RequestAuditPackageRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        AuditPackages.RequestAsync(new(companyId, request.FiscalPeriodId,
            ResolveActorId() ?? throw new UnauthorizedAccessException(), "resolved_server_side",
            request.IdempotencyKey, request.ScopeKey, request.ScopeVersion), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/audit-packages/{packageId:guid}/approve")]
    public Task<ActionResult<AuditPackageDto>> ApproveAuditPackageAsync(Guid companyId, Guid packageId,
        [FromBody] ApproveAuditPackageRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        AuditPackages.ApproveAsync(new(companyId, packageId,
            ResolveActorId() ?? throw new UnauthorizedAccessException(), request.Reason, request.ExpectedVersion), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/audit-packages/{packageId:guid}/cancel")]
    public Task<ActionResult<AuditPackageDto>> CancelAuditPackageAsync(Guid companyId, Guid packageId,
        [FromBody] CancelAuditPackageRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        AuditPackages.CancelAsync(new(companyId, packageId,
            ResolveActorId() ?? throw new UnauthorizedAccessException(), request.ExpectedVersion), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/audit-packages/{packageId:guid}/download-authorizations")]
    public Task<ActionResult<AuditPackageDownloadAuthorizationDto>> AuthorizeAuditPackageDownloadAsync(
        Guid companyId, Guid packageId, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        AuditPackages.AuthorizeDownloadAsync(new(companyId, packageId,
            ResolveActorId() ?? throw new UnauthorizedAccessException()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/audit-packages/{packageId:guid}/download")]
    public async Task<IActionResult> DownloadAuditPackageAsync(Guid companyId, Guid packageId,
        [FromQuery] string token, CancellationToken cancellationToken)
    {
        try
        {
            var download = await AuditPackages.DownloadAsync(new(companyId, packageId,
                ResolveActorId() ?? throw new UnauthorizedAccessException(), token), cancellationToken);
            Response.Headers["X-Content-SHA256"] = download.PackageChecksum;
            Response.Headers["X-Manifest-SHA256"] = download.ManifestChecksum;
            Response.Headers.CacheControl = "private, no-store";
            return File(download.Content, download.MediaType, download.FileName, enableRangeProcessing: false);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(CreateProblemDetails(ex.Message, "Audit package was not found.", StatusCodes.Status404NotFound)); }
        catch (AuditPackageException ex)
        {
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                "Audit package download was rejected", ex.Message)) { StatusCode = status };
        }
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/audit-packages/{packageId:guid}/verify")]
    public Task<ActionResult<AuditPackageVerificationDto>> VerifyAuditPackageAsync(Guid companyId,
        Guid packageId, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        AuditPackages.VerifyAsync(new(companyId, packageId,
            ResolveActorId() ?? throw new UnauthorizedAccessException()), cancellationToken));
}

public sealed class RequestAuditPackageRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = AuditPackageScopeValues.PeriodClose;
    public string ScopeVersion { get; set; } = AuditPackageScopeValues.CurrentVersion;
}

public sealed class ApproveAuditPackageRequest { public long ExpectedVersion { get; set; } public string? Reason { get; set; } }
public sealed class CancelAuditPackageRequest { public long ExpectedVersion { get; set; } }
