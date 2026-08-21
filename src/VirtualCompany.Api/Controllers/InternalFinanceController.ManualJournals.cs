using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

    private Guid RequiredActor() => ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.");

    private static ManualJournalDraftInput MapManualDraft(SaveManualJournalDraftRequest request) => new(
        request.FiscalPeriodId, request.VoucherSeriesCode, request.DocumentDate, request.PostingDate,
        request.Explanation, request.Currency,
        (request.Lines ?? []).Select(line => new ManualJournalLineInput(line.FinanceAccountId, line.DebitAmount,
            line.CreditAmount, line.Description, line.CostCenterId, line.TaxFacts, line.DimensionFacts)).ToArray(),
        request.EvidenceDocumentIds ?? [], request.OriginalLedgerEntryId, request.CorrectionReason);
}

public sealed class SaveManualJournalDraftRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public List<ManualJournalLineRequest>? Lines { get; set; } = [];
    public List<Guid>? EvidenceDocumentIds { get; set; } = [];
    public Guid? OriginalLedgerEntryId { get; set; }
    public string? CorrectionReason { get; set; }
}

public sealed class ManualJournalLineRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
}

public sealed class ManualJournalVersionedActionRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ManualJournalPreviewRequest
{
    public long ExpectedVersion { get; set; }
}
