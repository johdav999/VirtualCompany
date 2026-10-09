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
public sealed class InternalFinanceManualJournalsController : InternalFinanceControllerBase
{
    private readonly IManualJournalService _manualJournalService;

    public InternalFinanceManualJournalsController(
        IManualJournalService manualJournalService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceManualJournalsController> logger)
        : base(initializationProblems, logger)
    {
        _manualJournalService = manualJournalService;
    }
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/manual-journals/reference-data")]
    public async Task<ActionResult<ManualJournalReferenceDataDto>> GetManualJournalReferenceDataAsync(Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _manualJournalService.GetReferenceDataAsync(new(companyId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/manual-journals")]
    public async Task<ActionResult<ManualJournalDraftListResult>> ListManualJournalDraftsAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() => _manualJournalService.ListAsync(new(companyId, status, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/manual-journals/{draftId:guid}")]
    public async Task<ActionResult<ManualJournalDraftDto>> GetManualJournalDraftAsync(Guid companyId, Guid draftId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _manualJournalService.GetAsync(new(companyId, draftId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/manual-journals")]
    public async Task<ActionResult<ManualJournalDraftDto>> CreateManualJournalDraftAsync(Guid companyId,
        [FromBody] SaveManualJournalDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.CreateAsync(new(companyId, MapManualDraft(request),
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/manual-journals/{draftId:guid}")]
    public async Task<ActionResult<ManualJournalDraftDto>> UpdateManualJournalDraftAsync(Guid companyId, Guid draftId,
        [FromBody] SaveManualJournalDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.UpdateAsync(new(companyId, draftId, request.ExpectedVersion,
            MapManualDraft(request), request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/manual-journals/{draftId:guid}/discard")]
    public async Task<ActionResult<ManualJournalDraftDto>> DiscardManualJournalDraftAsync(Guid companyId, Guid draftId,
        [FromBody] ManualJournalVersionedActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.DiscardAsync(new(companyId, draftId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/manual-journals/{draftId:guid}/preview")]
    public async Task<ActionResult<ManualJournalPreviewDto>> PreviewManualJournalDraftAsync(Guid companyId, Guid draftId,
        [FromBody] ManualJournalPreviewRequest request, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _manualJournalService.PreviewAsync(new(companyId, draftId, request.ExpectedVersion,
            RequiredActor()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/manual-journals/{draftId:guid}/submit")]
    public async Task<ActionResult<ManualJournalSubmissionResult>> SubmitManualJournalDraftAsync(Guid companyId, Guid draftId,
        [FromBody] ManualJournalVersionedActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.SubmitAsync(new(companyId, draftId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/manual-journals/{draftId:guid}/post")]
    public async Task<ActionResult<ManualJournalPostingResult>> PostManualJournalDraftAsync(Guid companyId, Guid draftId,
        [FromBody] ManualJournalVersionedActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.PostAsync(new(companyId, draftId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/journals/{ledgerEntryId:guid}/adjustments")]
    public async Task<ActionResult<ManualJournalDraftDto>> CreateAdjustingJournalDraftAsync(Guid companyId, Guid ledgerEntryId,
        [FromBody] SaveManualJournalDraftRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _manualJournalService.CreateAdjustmentAsync(new(companyId, ledgerEntryId,
            MapManualDraft(request) with { OriginalLedgerEntryId = ledgerEntryId }, request.IdempotencyKey,
            RequiredActor(), ResolveCorrelationId()), cancellationToken));

    private static ManualJournalDraftInput MapManualDraft(SaveManualJournalDraftRequest request) => new(
        request.FiscalPeriodId, request.VoucherSeriesCode, request.DocumentDate, request.PostingDate,
        request.Explanation, request.Currency,
        (request.Lines ?? []).Select(line => new ManualJournalLineInput(line.FinanceAccountId, line.DebitAmount,
            line.CreditAmount, line.Description, line.CostCenterId, line.TaxFacts, line.DimensionFacts)).ToArray(),
        request.EvidenceDocumentIds ?? [], request.OriginalLedgerEntryId, request.CorrectionReason,
        request.SourceRecords?.Select(source => new ManualJournalSourceReferenceInput(
            source.SourceType, source.RecordId, source.SourceVersion)).ToArray());

}
