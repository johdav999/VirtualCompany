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
public sealed class InternalFinanceStatutoryDocumentsController : InternalFinanceControllerBase
{

    public InternalFinanceStatutoryDocumentsController(
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceStatutoryDocumentsController> logger)
        : base(initializationProblems, logger)
    {
    }
    [HttpPost("accounting/statutory-documents/preview")]
    public async Task<ActionResult<StatutoryDocumentPolicyDecisionDto>> PreviewStatutoryDocumentAsync(
        Guid companyId, [FromBody] StatutoryDocumentRequest request, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.PreviewAsync(new(companyId, request.ToInput()), cancellationToken));

    [HttpGet("accounting/statutory-document-series")]
    public async Task<ActionResult<IReadOnlyList<StatutoryDocumentSeriesDto>>> ListStatutoryDocumentSeriesAsync(
        Guid companyId, [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.ListSeriesAsync(companyId, cancellationToken));

    [HttpPost("accounting/statutory-document-series")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentSeriesDto>> CreateStatutoryDocumentSeriesAsync(
        Guid companyId, [FromBody] CreateStatutoryDocumentSeriesRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.CreateSeriesAsync(new(companyId, request.Code, request.DocumentType,
            request.FiscalYearStart, request.FiscalYearEnd, request.Prefix, request.NumberWidth,
            request.FirstNumber, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPut("accounting/statutory-document-series/{seriesId:guid}")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentSeriesDto>> UpdateStatutoryDocumentSeriesAsync(
        Guid companyId, Guid seriesId, [FromBody] UpdateStatutoryDocumentSeriesRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.UpdateSeriesAsync(new(companyId, seriesId, request.ExpectedVersion,
            request.Prefix, request.NumberWidth, request.IsActive, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/statutory-document-allocations")]
    public async Task<ActionResult<IReadOnlyList<StatutoryDocumentAllocationDto>>> ListStatutoryDocumentAllocationsAsync(
        Guid companyId, [FromQuery] Guid? seriesId, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.ListAllocationsAsync(companyId, seriesId, cancellationToken));

    [HttpPost("accounting/statutory-document-series/{seriesId:guid}/gaps")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryDocumentAllocationDto>> RecordStatutoryDocumentGapAsync(
        Guid companyId, Guid seriesId, [FromBody] RecordStatutoryDocumentGapRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.RecordGapAsync(new(companyId, seriesId, request.BusinessKey,
            request.SourceVersion, request.Reason, ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("accounting/statutory-documents/issue-native")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> IssueNativeStatutoryDocumentAsync(
        Guid companyId, [FromBody] IssueNativeStatutoryDocumentRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.IssueNativeCustomerAsync(new(companyId, request.SeriesId,
            request.BusinessKey, request.Document.ToInput(), ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpPost("accounting/statutory-documents/register-imported")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> RegisterImportedStatutoryDocumentAsync(
        Guid companyId, [FromBody] RegisterImportedStatutoryDocumentRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.RegisterImportedAsync(new(companyId, request.SourceRecordId,
            request.BusinessKey, request.Document.ToInput(), ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

    [HttpGet("accounting/statutory-documents/{issuedDocumentId:guid}")]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> GetIssuedStatutoryDocumentAsync(
        Guid companyId, Guid issuedDocumentId, [FromServices] IStatutoryDocumentService service,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => service.GetIssuedAsync(companyId, issuedDocumentId, cancellationToken));

    [HttpPost("accounting/statutory-documents/{issuedDocumentId:guid}/evidence")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<StatutoryIssuedDocumentDto>> AttachStatutoryDocumentEvidenceAsync(
        Guid companyId, Guid issuedDocumentId, [FromBody] AttachStatutoryDocumentEvidenceRequest request,
        [FromServices] IStatutoryDocumentService service, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => service.AttachEvidenceAsync(new(companyId, issuedDocumentId,
            request.ExpectedEvidenceVersion, request.RenderedEvidenceReference, request.DeliveryEvidenceReference,
            ResolveRequiredAccountingActorId(), ResolveCorrelationId()), cancellationToken));

}
