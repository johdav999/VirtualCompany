using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/journals/preview")]
    public async Task<ActionResult<AccountingPostingPreview>> PreviewAccountingJournalAsync(
        Guid companyId, [FromBody] ProposedAccountingEntryRequest request, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingPostingService.PreviewAsync(
            new PreviewAccountingEntryCommand(MapProposedEntry(companyId, request)), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/journals")]
    public async Task<ActionResult<PostedAccountingJournal>> PostAccountingJournalAsync(
        Guid companyId, [FromBody] ProposedAccountingEntryRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingPostingService.PostAsync(
            new PostAccountingEntryCommand(MapProposedEntry(companyId, request), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/journals/{ledgerEntryId:guid}/reversal")]
    public async Task<ActionResult<PostedAccountingJournal>> ReverseAccountingJournalAsync(
        Guid companyId, Guid ledgerEntryId, [FromBody] ReverseAccountingEntryRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingPostingService.ReverseAsync(
            new ReverseAccountingEntryCommand(
                companyId, ledgerEntryId, request.FiscalPeriodId, request.VoucherSeriesCode, request.PostingDate,
                request.Reason, request.SourceVersion, request.IdempotencyKey,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required to reverse a journal."),
                request.ApprovalRequestId, ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/journals")]
    public async Task<ActionResult<AccountingJournalListResult>> ListAccountingJournalsAsync(
        Guid companyId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        [FromQuery] string? search = null, [FromQuery] string? sourceType = null, [FromQuery] string? postingType = null,
        [FromQuery] string? voucherSeriesCode = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() => _accountingJournalReadService.ListAsync(
            new ListAccountingJournalsQuery(companyId, from, to, skip, take, search, sourceType, postingType, voucherSeriesCode), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/journals/{ledgerEntryId:guid}")]
    public async Task<ActionResult<AccountingJournalDto>> GetAccountingJournalAsync(
        Guid companyId, Guid ledgerEntryId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingJournalReadService.GetAsync(
            new GetAccountingJournalQuery(companyId, ledgerEntryId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/journals/by-source")]
    public async Task<ActionResult<AccountingJournalDto?>> GetAccountingJournalBySourceAsync(
        Guid companyId, [FromQuery] string sourceType, [FromQuery] string sourceId, [FromQuery] string? sourceVersion,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingJournalReadService.GetBySourceAsync(
            new GetAccountingJournalBySourceQuery(companyId, sourceType, sourceId, sourceVersion), cancellationToken));

    private ProposedAccountingEntry MapProposedEntry(Guid companyId, ProposedAccountingEntryRequest request) =>
        new(
            companyId,
            request.FiscalPeriodId,
            request.VoucherSeriesCode,
            request.DocumentDate,
            request.PostingDate,
            request.PostingType,
            request.Description,
            request.SourceType,
            request.SourceId,
            request.SourceVersion,
            request.IdempotencyKey,
            request.Lines.Select(line => new ProposedAccountingLine(
                line.FinanceAccountId, line.DebitAmount, line.CreditAmount, line.Currency, line.Description,
                line.CostCenterId, line.TaxFacts, line.DimensionFacts)).ToArray(),
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required to post a journal."),
            request.ApprovalRequestId,
            request.RequiresApproval,
            request.PolicyFacts,
            request.Action);
}

public sealed class ProposedAccountingEntryRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string PostingType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<ProposedAccountingLineRequest> Lines { get; set; } = [];
    public Guid? ApprovalRequestId { get; set; }
    public bool RequiresApproval { get; set; }
    public Dictionary<string, string>? PolicyFacts { get; set; }
    public string Action { get; set; } = "post";
}

public sealed class ProposedAccountingLineRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
}

public sealed class ReverseAccountingEntryRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}
