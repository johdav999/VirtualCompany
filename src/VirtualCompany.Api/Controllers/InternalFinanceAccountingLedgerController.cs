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
public sealed class InternalFinanceAccountingLedgerController : InternalFinanceControllerBase
{
    private readonly IAccountingJournalReadService _accountingJournalReadService;
    private readonly IAccountingPostingService _accountingPostingService;

    public InternalFinanceAccountingLedgerController(
        IAccountingJournalReadService accountingJournalReadService,
        IAccountingPostingService accountingPostingService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingLedgerController> logger)
        : base(initializationProblems, logger)
    {
        _accountingJournalReadService = accountingJournalReadService;
        _accountingPostingService = accountingPostingService;
    }
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
                line.CostCenterId, line.TaxFacts, line.DimensionFacts, line.DocumentDebitAmount,
                line.DocumentCreditAmount, line.DocumentCurrency, line.ExchangeRate, line.ExchangeRateDate,
                line.ExchangeRateConversionId, line.ExchangeRateIdentity, line.ConversionRoundingResidual,
                line.DimensionMemberIds)).ToArray(),
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required to post a journal."),
            request.ApprovalRequestId,
            request.RequiresApproval,
            request.PolicyFacts,
            request.Action);

}
