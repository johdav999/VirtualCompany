using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyCashSettlementPostingService : IFinanceCashSettlementPostingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IAccountingPostingService _postingService;
    private readonly IAccountingAccountRoleResolver _accountRoleResolver;
    private readonly TimeProvider _timeProvider;

    public CompanyCashSettlementPostingService(VirtualCompanyDbContext dbContext) : this(dbContext, null) { }

    public CompanyCashSettlementPostingService(VirtualCompanyDbContext dbContext, ICompanyContextAccessor? companyContextAccessor)
        : this(
            dbContext,
            companyContextAccessor,
            new AccountingPostingService(dbContext, new AccountingJournalReadService(dbContext), new AuditEventWriter(dbContext), TimeProvider.System),
            new AccountingAccountRoleResolver(dbContext),
            TimeProvider.System)
    {
    }

    public CompanyCashSettlementPostingService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        IAccountingPostingService postingService,
        IAccountingAccountRoleResolver accountRoleResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _postingService = postingService;
        _accountRoleResolver = accountRoleResolver;
        _timeProvider = timeProvider;
    }

    public Task<CashSettlementPostingResultDto> PostCashSettlementAsync(PostCashSettlementCommand command, CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(() => PostCashSettlementCoreAsync(command, cancellationToken), cancellationToken);

    private async Task<CashSettlementPostingResultDto> PostCashSettlementCoreAsync(PostCashSettlementCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        if (command.PaymentId == Guid.Empty) throw Validation(nameof(command.PaymentId), "Payment id is required.");

        var sourceType = FinanceCashPostingSourceTypes.Normalize(command.SourceType);
        var sourceId = NormalizeSourceId(command.SourceId);
        var amount = NormalizeMoney(command.SettledAmount);
        var postedAtUtc = NormalizeUtc(command.SettledAtUtc);
        var payment = await _dbContext.Payments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance payment was not found.");
        if (!string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            throw Validation(nameof(command.PaymentId), "Only completed payments can be posted to the ledger.");
        if (amount > payment.Amount) throw Validation(nameof(command.SettledAmount), "Settled amount cannot exceed the payment amount.");
        if (sourceType == FinanceCashPostingSourceTypes.BankTransaction)
            await EnsureMatchedBankTransactionSourceAsync(command.CompanyId, sourceId, cancellationToken);

        var period = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.StartUtc <= postedAtUtc && x.EndUtc > postedAtUtc, cancellationToken)
            ?? throw new AccountingPostingException(AccountingPostingReasonCodes.PeriodNotFound, "No accounting period covers the settlement date.");
        var bank = await _accountRoleResolver.ResolveRequiredAsync(command.CompanyId, AccountingAccountRoleKeys.Bank, cancellationToken);
        var settlementRole = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? AccountingAccountRoleKeys.AccountsReceivable
            : AccountingAccountRoleKeys.AccountsPayable;
        var settlement = await _accountRoleResolver.ResolveRequiredAsync(command.CompanyId, settlementRole, cancellationToken);
        var priorLedgerEntryIds = await _dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.PaymentId == payment.Id &&
                !(x.SourceType == sourceType && x.SourceId == sourceId && x.PostedAtUtc == postedAtUtc))
            .Select(x => x.LedgerEntryId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (priorLedgerEntryIds.Length > 0)
        {
            var priorControlLines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && priorLedgerEntryIds.Contains(x.LedgerEntryId) &&
                    x.FinanceAccountId == settlement.FinanceAccountId)
                .ToListAsync(cancellationToken);
            var priorSettledAmount = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
                ? priorControlLines.Sum(x => x.CreditAmount - x.DebitAmount)
                : priorControlLines.Sum(x => x.DebitAmount - x.CreditAmount);
            if (NormalizeMoneyValue(Math.Max(0m, priorSettledAmount) + amount) > payment.Amount)
                throw Validation(nameof(command.SettledAmount),
                    "This settlement would exceed the payment amount after existing cash postings are included.");
        }
        var description = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ? $"Customer cash settlement for {payment.CounterpartyReference}"
            : $"Supplier cash settlement for {payment.CounterpartyReference}";
        IReadOnlyList<ProposedAccountingLine> lines = string.Equals(payment.PaymentType, PaymentTypes.Incoming, StringComparison.OrdinalIgnoreCase)
            ?
            [
                new(bank.FinanceAccountId, amount, 0m, payment.Currency, description),
                new(settlement.FinanceAccountId, 0m, amount, payment.Currency, description)
            ]
            :
            [
                new(settlement.FinanceAccountId, amount, 0m, payment.Currency, description),
                new(bank.FinanceAccountId, 0m, amount, payment.Currency, description)
            ];
        var idempotencyKey = $"cash-settlement:{command.CompanyId:N}:{sourceType}:{sourceId}:{payment.Id:N}:{postedAtUtc.Ticks}";
        var sourceVersion = $"{payment.UpdatedUtc.Ticks}:{amount:0.00}";
        var posted = await _postingService.PostAsync(new PostAccountingEntryCommand(
            new ProposedAccountingEntry(
                command.CompanyId,
                period.Id,
                "B",
                DateOnly.FromDateTime(postedAtUtc),
                DateOnly.FromDateTime(postedAtUtc),
                LedgerPostingTypeValues.CashSettlement,
                description,
                sourceType,
                sourceId,
                sourceVersion,
                idempotencyKey,
                lines,
                Guid.Empty,
                PolicyFacts: new Dictionary<string, string>
                {
                    ["paymentId"] = payment.Id.ToString("N"),
                    ["settledAmount"] = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                },
                ActorType: AuditActorTypes.System,
                EffectivePostedAtUtc: postedAtUtc)), cancellationToken);

        var ledgerEntryId = posted.Journal.Id;
        var linkExists = await _dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.PaymentId == payment.Id && x.LedgerEntryId == ledgerEntryId, cancellationToken);
        if (!linkExists)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(
                Guid.NewGuid(), command.CompanyId, payment.Id, ledgerEntryId, sourceType, sourceId, postedAtUtc, now));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new CashSettlementPostingResultDto(command.CompanyId, ledgerEntryId, sourceType, sourceId, amount,
            posted.Journal.PostedAtUtc ?? postedAtUtc, !posted.IsIdempotentReplay);
    }

    private async Task EnsureMatchedBankTransactionSourceAsync(Guid companyId, string sourceId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sourceId, out var bankTransactionId))
            throw Validation(nameof(PostCashSettlementCommand.SourceId), "Bank transaction posting sources must use the bank transaction id.");
        var hasMatch = await _dbContext.BankTransactionPaymentLinks.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId, cancellationToken);
        var wasManuallyClassified = !hasMatch && await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId &&
                x.MatchingStatus == BankTransactionMatchingStatuses.ManuallyClassified, cancellationToken);
        if (!hasMatch && !wasManuallyClassified)
            throw Validation(nameof(PostCashSettlementCommand.SourceId), "Unmatched bank transactions cannot create receivable or payable settlement journals.");
    }

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null) return await action();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContextAccessor?.CompanyId is Guid current && current != companyId)
            throw new UnauthorizedAccessException("Cash settlement posting is scoped to the active company context.");
    }

    private static string NormalizeSourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Source id is required.", nameof(value));
        var normalized = value.Trim();
        return normalized.Length <= 128 ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static decimal NormalizeMoney(decimal value) => value > 0m
        ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
        : throw Validation(nameof(PostCashSettlementCommand.SettledAmount), "Settled amount must be greater than zero.");
    private static decimal NormalizeMoneyValue(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static FinanceValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] }, message);
}
