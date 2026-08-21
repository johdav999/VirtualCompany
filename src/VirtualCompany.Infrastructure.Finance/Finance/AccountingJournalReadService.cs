using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingJournalReadService : IAccountingJournalReadService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AccountingJournalReadService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<AccountingJournalListResult> ListAsync(ListAccountingJournalsQuery query, CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 250);
        var entries = _dbContext.LedgerEntries.AsNoTracking()
            .Where(entry => entry.CompanyId == query.CompanyId && entry.Status == LedgerEntryStatuses.Posted);
        if (query.From.HasValue) entries = entries.Where(entry => entry.PostingDate >= query.From.Value);
        if (query.To.HasValue) entries = entries.Where(entry => entry.PostingDate <= query.To.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            entries = entries.Where(entry => entry.EntryNumber.Contains(search) ||
                entry.Description != null && entry.Description.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.SourceType))
        {
            var sourceType = query.SourceType.Trim().ToLowerInvariant();
            entries = entries.Where(entry => entry.SourceType == sourceType);
        }
        if (!string.IsNullOrWhiteSpace(query.PostingType))
        {
            var postingType = query.PostingType.Trim().ToLowerInvariant();
            entries = entries.Where(entry => entry.PostingType == postingType);
        }
        if (!string.IsNullOrWhiteSpace(query.VoucherSeriesCode))
        {
            var seriesCode = query.VoucherSeriesCode.Trim().ToUpperInvariant();
            entries = entries.Where(entry => entry.VoucherSeries != null && entry.VoucherSeries.Code == seriesCode);
        }
        var total = await entries.CountAsync(cancellationToken);
        var page = await entries.OrderByDescending(entry => entry.PostingDate).ThenByDescending(entry => entry.VoucherSequenceNumber)
            .Skip(skip).Take(take).Select(entry => entry.Id).ToArrayAsync(cancellationToken);
        var items = new List<AccountingJournalDto>(page.Length);
        foreach (var id in page)
            items.Add(await GetAsync(new GetAccountingJournalQuery(query.CompanyId, id), cancellationToken));
        return new(items, total, skip, take);
    }

    public async Task<AccountingJournalDto> GetAsync(GetAccountingJournalQuery query, CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var entry = await BaseQuery().SingleOrDefaultAsync(item => item.CompanyId == query.CompanyId && item.Id == query.LedgerEntryId, cancellationToken)
            ?? throw new AccountingPostingException(AccountingPostingReasonCodes.JournalNotFound, "The journal entry could not be found.");
        var targetIds = new List<string> { entry.Id.ToString("N") };
        if (string.Equals(entry.SourceType, "manual_journal_draft", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.SourceId))
            targetIds.Add(entry.SourceId);
        var audit = await _dbContext.AuditEvents.AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && targetIds.Contains(x.TargetId))
            .OrderBy(x => x.OccurredUtc).Select(x => new AccountingJournalAuditEventDto(x.Id, x.ActorType, x.ActorId,
                x.Action, x.Outcome, x.RationaleSummary, x.OccurredUtc)).ToArrayAsync(cancellationToken);
        return Map(entry, audit);
    }

    public async Task<AccountingJournalDto?> GetBySourceAsync(GetAccountingJournalBySourceQuery query, CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var sourceType = Required(query.SourceType, nameof(query.SourceType), 64).ToLowerInvariant();
        var sourceId = Required(query.SourceId, nameof(query.SourceId), 128);
        var entries = BaseQuery().Where(item => item.CompanyId == query.CompanyId && item.SourceType == sourceType && item.SourceId == sourceId);
        if (!string.IsNullOrWhiteSpace(query.SourceVersion)) entries = entries.Where(item => item.SourceVersion == query.SourceVersion.Trim());
        var entry = await entries.OrderByDescending(item => item.PostedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (entry is null) return null;
        return await GetAsync(new GetAccountingJournalQuery(query.CompanyId, entry.Id), cancellationToken);
    }

    private IQueryable<LedgerEntry> BaseQuery() => _dbContext.LedgerEntries.AsNoTracking()
        .Include(entry => entry.VoucherSeries)
        .Include(entry => entry.Lines).ThenInclude(line => line.FinanceAccount)
        .Include(entry => entry.EvidenceLinks).ThenInclude(link => link.Document)
        .Include(entry => entry.ApprovalRequest)
        .Include(entry => entry.Corrections);

    private static AccountingJournalDto Map(LedgerEntry entry, IReadOnlyList<AccountingJournalAuditEventDto> audit)
    {
        var lines = entry.Lines.OrderBy(line => line.CreatedUtc).ThenBy(line => line.Id).Select(line => new AccountingJournalLineDto(
            line.Id, line.FinanceAccountId, line.FinanceAccount.Code, line.FinanceAccount.Name,
            line.DebitAmount, line.CreditAmount, line.Currency, line.CostCenterId, line.Description,
            Parse(line.TaxFactsJson), Parse(line.DimensionFactsJson))).ToArray();
        var evidence = entry.EvidenceLinks.OrderBy(link => link.Title).Select(link =>
            new AccountingJournalEvidenceDto(link.DocumentId, link.Title, link.ContentHash, link.Document.OriginalFileName)).ToArray();
        var approval = entry.ApprovalRequest is null ? null : new AccountingJournalApprovalDto(entry.ApprovalRequest.Id,
            entry.ApprovalRequest.Status.ToStorageValue(), entry.ApprovalRequest.ApprovalType,
            entry.ApprovalRequest.DecisionSummary, entry.ApprovalRequest.CreatedUtc, entry.ApprovalRequest.DecidedUtc);
        var corrections = entry.Corrections.OrderBy(correction => correction.PostingDate).Select(correction =>
            new AccountingJournalCorrectionDto(correction.Id, correction.EntryNumber, correction.PostingType ?? string.Empty,
                correction.PostingDate, correction.CorrectionReason, correction.Status)).ToArray();
        return new AccountingJournalDto(
            entry.Id, entry.CompanyId, entry.FiscalPeriodId, entry.EntryNumber, entry.Status,
            entry.VoucherSeries?.Code ?? string.Empty, entry.VoucherSequenceNumber, entry.VoucherFiscalYear,
            entry.DocumentDate, entry.PostingDate, entry.BaseCurrency ?? lines.FirstOrDefault()?.Currency ?? string.Empty,
            entry.PostingType, entry.Description, entry.SourceType, entry.SourceId, entry.SourceVersion,
            entry.PolicyPackKey, entry.PolicyPackVersion, entry.PostedByUserId, entry.ApprovalRequestId,
            entry.OriginalLedgerEntryId, entry.CorrectionReason, entry.PostedAtUtc,
            lines.Sum(line => line.DebitAmount), lines.Sum(line => line.CreditAmount), lines,
            evidence, approval, corrections, audit);
    }

    private static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    private static void ValidateCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new AccountingPostingException(AccountingPostingReasonCodes.JournalNotFound, "The journal entry could not be found.");
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new AccountingPostingException(AccountingPostingReasonCodes.InvalidSource, $"{name} is invalid.");
        return value.Trim();
    }
}
