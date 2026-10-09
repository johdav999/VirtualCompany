using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceAuditPackagesController : InternalFinanceControllerBase
{

    public InternalFinanceAuditPackagesController(
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAuditPackagesController> logger)
        : base(initializationProblems, logger)
    {
    }
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
