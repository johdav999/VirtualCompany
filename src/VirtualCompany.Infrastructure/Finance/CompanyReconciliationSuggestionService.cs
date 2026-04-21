using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyReconciliationSuggestionService :
    IReconciliationSuggestionReadService,
    IReconciliationSuggestionCommandService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly CompanyBankTransactionService _bankTransactionService;
    private readonly FinancePaymentAllocationService _paymentAllocationService;

    public CompanyReconciliationSuggestionService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null)
    {
    }

    public CompanyReconciliationSuggestionService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _bankTransactionService = new CompanyBankTransactionService(dbContext, companyContextAccessor);
        _paymentAllocationService = new FinancePaymentAllocationService(dbContext);
    }

    public async Task<ReconciliationSuggestionRecordDto> CreateSuggestionAsync(
        CreateReconciliationSuggestionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await ValidateActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);

        var normalizedSourceType = NormalizeRecordType(command.SourceRecordType, nameof(command.SourceRecordType));
        var normalizedTargetType = NormalizeRecordType(command.TargetRecordType, nameof(command.TargetRecordType));
        ValidateSupportedPair(normalizedSourceType, normalizedTargetType);

        await EnsureRecordExistsAsync(command.CompanyId, normalizedSourceType, command.SourceRecordId, nameof(command.SourceRecordId), cancellationToken);
        await EnsureRecordExistsAsync(command.CompanyId, normalizedTargetType, command.TargetRecordId, nameof(command.TargetRecordId), cancellationToken);

        var suggestion = new ReconciliationSuggestionRecord(
            Guid.NewGuid(),
            command.CompanyId,
            normalizedSourceType,
            command.SourceRecordId,
            normalizedTargetType,
            command.TargetRecordId,
            command.MatchType,
            command.ConfidenceScore,
            command.RuleBreakdown,
            command.ActorUserId);

        _dbContext.ReconciliationSuggestionRecords.Add(suggestion);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapSuggestion(suggestion);
    }

    public async Task<ReconciliationSuggestionPageDto> GetSuggestionsAsync(
        GetReconciliationSuggestionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        ValidateSuggestionListQuery(query);

        var normalizedEntityType = NormalizeOptionalRecordType(query.EntityType);
        var normalizedStatus = NormalizeOptionalStatus(query.Status) ?? ReconciliationSuggestionStatuses.Open;
        var normalizedMinConfidenceScore = NormalizeOptionalConfidenceScore(query.MinConfidenceScore);
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var rows = _dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == normalizedStatus);

        if (normalizedEntityType is not null)
        {
            rows = rows.Where(x =>
                x.SourceRecordType == normalizedEntityType ||
                x.TargetRecordType == normalizedEntityType);
        }

        if (normalizedMinConfidenceScore.HasValue)
        {
            rows = rows.Where(x => x.ConfidenceScore >= normalizedMinConfidenceScore.Value);
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var items = await rows
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ReconciliationSuggestionPageDto(
            totalCount,
            page,
            pageSize,
            totalPages,
            items.Select(MapSuggestion).ToList());
    }

    public async Task<IReadOnlyList<ReconciliationSuggestionRecordDto>> GetOpenSuggestionsAsync(
        GetOpenReconciliationSuggestionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var normalizedSourceType = NormalizeOptionalRecordType(query.SourceRecordType);
        var normalizedTargetType = NormalizeOptionalRecordType(query.TargetRecordType);
        var limit = NormalizeLimit(query.Limit);

        var rows = _dbContext.ReconciliationSuggestionRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == ReconciliationSuggestionStatuses.Open);

        if (normalizedSourceType is not null)
        {
            rows = rows.Where(x => x.SourceRecordType == normalizedSourceType);
        }

        if (query.SourceRecordId.HasValue && query.SourceRecordId.Value != Guid.Empty)
        {
            rows = rows.Where(x => x.SourceRecordId == query.SourceRecordId.Value);
        }

        if (normalizedTargetType is not null)
        {
            rows = rows.Where(x => x.TargetRecordType == normalizedTargetType);
        }

        if (query.TargetRecordId.HasValue && query.TargetRecordId.Value != Guid.Empty)
        {
            rows = rows.Where(x => x.TargetRecordId == query.TargetRecordId.Value);
        }

        var suggestions = await rows
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return suggestions.Select(MapSuggestion).ToList();
    }

    public Task<AcceptedReconciliationSuggestionDto> AcceptSuggestionAsync(
        AcceptReconciliationSuggestionCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId);
            await ValidateActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);

            var suggestion = await _dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.CompanyId == command.CompanyId && x.Id == command.SuggestionId,
                    cancellationToken);

            if (suggestion is null)
            {
                throw new KeyNotFoundException("Reconciliation suggestion was not found.");
            }
            EnsureCanAccept(suggestion);

            await ApplyAcceptedOutcomeAsync(suggestion, cancellationToken);
            var now = DateTime.UtcNow;
            var result = new ReconciliationResultRecord(
                Guid.NewGuid(),
                command.CompanyId,
                suggestion.Id,
                suggestion.SourceRecordType,
                suggestion.SourceRecordId,
                suggestion.TargetRecordType,
                suggestion.TargetRecordId,
                suggestion.MatchType,
                suggestion.ConfidenceScore,
                suggestion.RuleBreakdown,
                command.ActorUserId,
                now);

            _dbContext.ReconciliationResultRecords.Add(result);
            suggestion.Accept(command.ActorUserId, now);

            // Narrow supersede scope to the same exact pair, including inverse ordering, to avoid blocking future partial-allocation flows.
            var competingSuggestions = await _dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == command.CompanyId &&
                    x.Id != suggestion.Id &&
                    x.Status == ReconciliationSuggestionStatuses.Open)
                .ToListAsync(cancellationToken);

            var supersededCount = 0;
            foreach (var competingSuggestion in competingSuggestions.Where(x => ReferencesSamePair(x, suggestion)))
            {
                competingSuggestion.Supersede(command.ActorUserId, now);
                supersededCount++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new AcceptedReconciliationSuggestionDto(
                MapSuggestion(suggestion),
                MapResult(result),
                supersededCount);
        }, cancellationToken);

    public Task<ReconciliationSuggestionRecordDto> RejectSuggestionAsync(
        RejectReconciliationSuggestionCommand command,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(async () =>
        {
            EnsureTenant(command.CompanyId);
            await ValidateActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);

            var suggestion = await _dbContext.ReconciliationSuggestionRecords
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.CompanyId == command.CompanyId && x.Id == command.SuggestionId,
                    cancellationToken);

            if (suggestion is null)
            {
                throw new KeyNotFoundException("Reconciliation suggestion was not found.");
            }
            EnsureCanReject(suggestion);

            suggestion.Reject(command.ActorUserId, DateTime.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapSuggestion(suggestion);
        }, cancellationToken);

    private async Task ApplyAcceptedOutcomeAsync(
        ReconciliationSuggestionRecord suggestion,
        CancellationToken cancellationToken)
    {
        if (IsPaymentBankPair(suggestion.SourceRecordType, suggestion.TargetRecordType))
        {
            await ApplyPaymentBankAcceptanceAsync(suggestion, cancellationToken);
            return;
        }

        if (IsInvoicePaymentPair(suggestion.SourceRecordType, suggestion.TargetRecordType))
        {
            await ApplyPaymentAllocationAcceptanceAsync(suggestion, isInvoice: true, cancellationToken);
            return;
        }

        if (IsBillPaymentPair(suggestion.SourceRecordType, suggestion.TargetRecordType))
        {
            await ApplyPaymentAllocationAcceptanceAsync(suggestion, isInvoice: false, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported accepted reconciliation pair '{suggestion.SourceRecordType}' -> '{suggestion.TargetRecordType}'.");
    }

    private async Task ApplyPaymentBankAcceptanceAsync(
        ReconciliationSuggestionRecord suggestion,
        CancellationToken cancellationToken)
    {
        var paymentId = suggestion.SourceRecordType == ReconciliationRecordTypes.Payment
            ? suggestion.SourceRecordId
            : suggestion.TargetRecordId;
        var bankTransactionId = suggestion.SourceRecordType == ReconciliationRecordTypes.BankTransaction
            ? suggestion.SourceRecordId
            : suggestion.TargetRecordId;

        var existingLink = await _dbContext.BankTransactionPaymentLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == suggestion.CompanyId &&
                     x.BankTransactionId == bankTransactionId &&
                     x.PaymentId == paymentId,
                cancellationToken);

        if (existingLink is not null)
        {
            await _bankTransactionService.ReconcileWithinAmbientTransactionAsync(
                new ReconcileBankTransactionCommand(
                    suggestion.CompanyId,
                    bankTransactionId,
                    [new BankTransactionPaymentMatchDto(paymentId, existingLink.AllocatedAmount)]),
                cancellationToken);
            return;
        }

        var transaction = await LoadBankTransactionAsync(suggestion.CompanyId, bankTransactionId, cancellationToken);
        var payment = await LoadPaymentAsync(suggestion.CompanyId, paymentId, cancellationToken);
        var remainingOnTransaction = NormalizeMoney(Math.Max(0m, transaction.AbsoluteAmount - transaction.ReconciledAmount));
        var allocatedAmount = NormalizeMoney(Math.Min(payment.Amount, remainingOnTransaction));

        if (allocatedAmount <= 0m)
        {
            throw new InvalidOperationException("The selected bank transaction no longer has an open amount available for reconciliation.");
        }

        await _bankTransactionService.ReconcileWithinAmbientTransactionAsync(
            new ReconcileBankTransactionCommand(
                suggestion.CompanyId,
                bankTransactionId,
                [new BankTransactionPaymentMatchDto(paymentId, allocatedAmount)]),
            cancellationToken);
    }

    private async Task ApplyPaymentAllocationAcceptanceAsync(
        ReconciliationSuggestionRecord suggestion,
        bool isInvoice,
        CancellationToken cancellationToken)
    {
        var paymentId = suggestion.SourceRecordType == ReconciliationRecordTypes.Payment
            ? suggestion.SourceRecordId
            : suggestion.TargetRecordId;
        var documentId = suggestion.SourceRecordType == ReconciliationRecordTypes.Payment
            ? suggestion.TargetRecordId
            : suggestion.SourceRecordId;

        var existingAllocation = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == suggestion.CompanyId &&
                x.PaymentId == paymentId &&
                (isInvoice ? x.InvoiceId == documentId : x.BillId == documentId))
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAllocation is not null)
        {
            return;
        }

        var payment = await LoadPaymentAsync(suggestion.CompanyId, paymentId, cancellationToken);
        var remainingOnPayment = NormalizeMoney(Math.Max(
            0m,
            payment.Amount - await GetAllocatedToPaymentAsync(suggestion.CompanyId, paymentId, cancellationToken)));

        decimal remainingOnDocument;
        if (isInvoice)
        {
            var invoice = await LoadInvoiceAsync(suggestion.CompanyId, documentId, cancellationToken);
            remainingOnDocument = NormalizeMoney(Math.Max(
                0m,
                invoice.Amount - await GetAllocatedToInvoiceAsync(suggestion.CompanyId, documentId, cancellationToken)));
        }
        else
        {
            var bill = await LoadBillAsync(suggestion.CompanyId, documentId, cancellationToken);
            remainingOnDocument = NormalizeMoney(Math.Max(
                0m,
                bill.Amount - await GetAllocatedToBillAsync(suggestion.CompanyId, documentId, cancellationToken)));
        }

        var allocatedAmount = NormalizeMoney(Math.Min(remainingOnPayment, remainingOnDocument));
        if (allocatedAmount <= 0m)
        {
            throw new InvalidOperationException("The selected payment or document no longer has an open amount available for reconciliation.");
        }

        await _paymentAllocationService.CreateWithinAmbientTransactionAsync(
            new CreateFinancePaymentAllocationCommand(
                suggestion.CompanyId,
                new CreateFinancePaymentAllocationDto(
                    paymentId,
                    isInvoice ? documentId : null,
                    isInvoice ? null : documentId,
                    allocatedAmount,
                    payment.Currency)),
            cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Reconciliation suggestion operations are scoped to the active company context.");
        }
    }

    private async Task ValidateActorAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id is required.", nameof(userId));
        }

        var isActiveCompanyMember = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.UserId == userId &&
                     x.Status == CompanyMembershipStatus.Active,
                cancellationToken);

        if (!isActiveCompanyMember)
        {
            throw new UnauthorizedAccessException("Reconciliation suggestion actors must be active members of the target company.");
        }
    }

    private async Task EnsureRecordExistsAsync(
        Guid companyId,
        string recordType,
        Guid recordId,
        string fieldName,
        CancellationToken cancellationToken)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        var exists = recordType switch
        {
            var type when type == ReconciliationRecordTypes.Payment => await _dbContext.Payments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == recordId, cancellationToken),
            var type when type == ReconciliationRecordTypes.BankTransaction => await _dbContext.BankTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == recordId, cancellationToken),
            var type when type == ReconciliationRecordTypes.Invoice => await _dbContext.FinanceInvoices.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == recordId, cancellationToken),
            var type when type == ReconciliationRecordTypes.Bill => await _dbContext.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == recordId, cancellationToken),
            _ => false
        };

        if (!exists)
        {
            throw new KeyNotFoundException($"Record '{recordId}' was not found for reconciliation type '{recordType}'.");
        }
    }

    private static void ValidateSupportedPair(string sourceRecordType, string targetRecordType)
    {
        var supported =
            (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.BankTransaction) ||
            (sourceRecordType == ReconciliationRecordTypes.BankTransaction && targetRecordType == ReconciliationRecordTypes.Payment) ||
            (sourceRecordType == ReconciliationRecordTypes.Invoice && targetRecordType == ReconciliationRecordTypes.Payment) ||
            (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.Invoice) ||
            (sourceRecordType == ReconciliationRecordTypes.Bill && targetRecordType == ReconciliationRecordTypes.Payment) ||
            (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.Bill);

        if (!supported)
        {
            throw new ArgumentException($"Unsupported reconciliation pair '{sourceRecordType}' -> '{targetRecordType}'.");
        }
    }

    private static bool ReferencesSamePair(ReconciliationSuggestionRecord left, ReconciliationSuggestionRecord right) =>
        (left.SourceRecordType == right.SourceRecordType &&
         left.SourceRecordId == right.SourceRecordId &&
         left.TargetRecordType == right.TargetRecordType &&
         left.TargetRecordId == right.TargetRecordId) ||
        (left.SourceRecordType == right.TargetRecordType &&
         left.SourceRecordId == right.TargetRecordId &&
         left.TargetRecordType == right.SourceRecordType &&
         left.TargetRecordId == right.SourceRecordId);

    private static void ValidateSuggestionListQuery(GetReconciliationSuggestionsQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var normalizedEntityType = ReconciliationRecordTypes.Normalize(query.EntityType);
            if (!ReconciliationRecordTypes.IsSupported(normalizedEntityType))
            {
                errors[nameof(GetReconciliationSuggestionsQuery.EntityType)] =
                [
                    "Unsupported reconciliation record type."
                ];
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var normalizedStatus = ReconciliationSuggestionStatuses.Normalize(query.Status);
            if (!ReconciliationSuggestionStatuses.IsSupported(normalizedStatus))
            {
                errors[nameof(GetReconciliationSuggestionsQuery.Status)] =
                [
                    "Unsupported reconciliation suggestion status."
                ];
            }
        }

        if (query.MinConfidenceScore is < 0m or > 1m)
        {
            errors[nameof(GetReconciliationSuggestionsQuery.MinConfidenceScore)] =
            [
                "MinConfidenceScore must be between 0 and 1."
            ];
        }

        if (query.Page <= 0)
        {
            errors[nameof(GetReconciliationSuggestionsQuery.Page)] =
            [
                "Page must be 1 or greater."
            ];
        }

        if (query.PageSize <= 0 || query.PageSize > MaxPageSize)
        {
            errors[nameof(GetReconciliationSuggestionsQuery.PageSize)] =
            [
                $"PageSize must be between 1 and {MaxPageSize}."
            ];
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors, "Reconciliation suggestion query is invalid.");
        }
    }

    private static void EnsureCanAccept(ReconciliationSuggestionRecord suggestion)
    {
        var normalizedStatus = ReconciliationSuggestionStatuses.Normalize(suggestion.Status);
        if (normalizedStatus == ReconciliationSuggestionStatuses.Open)
        {
            return;
        }

        throw CreateStateTransitionValidationException(
            nameof(AcceptReconciliationSuggestionCommand.SuggestionId),
            suggestion.Id,
            normalizedStatus,
            "accepted");
    }

    private static void EnsureCanReject(ReconciliationSuggestionRecord suggestion)
    {
        var normalizedStatus = ReconciliationSuggestionStatuses.Normalize(suggestion.Status);
        if (normalizedStatus == ReconciliationSuggestionStatuses.Open)
        {
            return;
        }

        throw CreateStateTransitionValidationException(
            nameof(RejectReconciliationSuggestionCommand.SuggestionId),
            suggestion.Id,
            normalizedStatus,
            "rejected");
    }

    private async Task<Payment> LoadPaymentAsync(
        Guid companyId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == paymentId, cancellationToken);

        return payment ?? throw new KeyNotFoundException("Finance payment was not found.");
    }

    private async Task<BankTransaction> LoadBankTransactionAsync(
        Guid companyId,
        Guid bankTransactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.BankTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == bankTransactionId, cancellationToken);

        return transaction ?? throw new KeyNotFoundException("Bank transaction was not found.");
    }

    private async Task<FinanceInvoice> LoadInvoiceAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId, cancellationToken);

        return invoice ?? throw new KeyNotFoundException("Finance invoice was not found.");
    }

    private async Task<FinanceBill> LoadBillAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        var bill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken);

        return bill ?? throw new KeyNotFoundException("Finance bill was not found.");
    }

    private async Task<decimal> GetAllocatedToPaymentAsync(Guid companyId, Guid paymentId, CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.PaymentId == paymentId)
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private async Task<decimal> GetAllocatedToInvoiceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId)
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private async Task<decimal> GetAllocatedToBillAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.BillId == billId)
            .SumAsync(x => (decimal?)x.AllocatedAmount, cancellationToken) ?? 0m;

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsPaymentBankPair(string sourceRecordType, string targetRecordType) =>
        (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.BankTransaction) ||
        (sourceRecordType == ReconciliationRecordTypes.BankTransaction && targetRecordType == ReconciliationRecordTypes.Payment);

    private static bool IsInvoicePaymentPair(string sourceRecordType, string targetRecordType) =>
        (sourceRecordType == ReconciliationRecordTypes.Invoice && targetRecordType == ReconciliationRecordTypes.Payment) ||
        (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.Invoice);

    private static bool IsBillPaymentPair(string sourceRecordType, string targetRecordType) =>
        (sourceRecordType == ReconciliationRecordTypes.Bill && targetRecordType == ReconciliationRecordTypes.Payment) ||
        (sourceRecordType == ReconciliationRecordTypes.Payment && targetRecordType == ReconciliationRecordTypes.Bill);

    private static int NormalizeLimit(int limit) =>
        limit <= 0
            ? DefaultLimit
            : Math.Min(limit, MaxLimit);

    private static string NormalizeRecordType(string value, string name)
    {
        var normalized = ReconciliationRecordTypes.Normalize(value);
        if (!ReconciliationRecordTypes.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported reconciliation record type.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalRecordType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeRecordType(value, nameof(value));
    }

    private static string? NormalizeOptionalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = ReconciliationSuggestionStatuses.Normalize(value);
        if (!ReconciliationSuggestionStatuses.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported reconciliation suggestion status.");
        }

        return normalized;
    }

    private static decimal? NormalizeOptionalConfidenceScore(decimal? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
        if (normalized < 0m || normalized > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "MinConfidenceScore must be between 0 and 1.");
        }

        return normalized;
    }

    private static FinanceValidationException CreateStateTransitionValidationException(string fieldName, Guid suggestionId, string currentStatus, string targetAction) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [fieldName] = [$"Suggestion '{suggestionId}' cannot be {targetAction} because it is already {currentStatus}."] }, $"Reconciliation suggestion cannot be {targetAction} because it is already {currentStatus}.");

    private static ReconciliationSuggestionRecordDto MapSuggestion(ReconciliationSuggestionRecord suggestion) =>
        new(
            suggestion.Id,
            suggestion.CompanyId,
            suggestion.SourceRecordType,
            suggestion.SourceRecordId,
            suggestion.TargetRecordType,
            suggestion.TargetRecordId,
            suggestion.MatchType,
            suggestion.ConfidenceScore,
            CloneNodes(suggestion.RuleBreakdown),
            suggestion.Status,
            suggestion.CreatedUtc,
            suggestion.UpdatedUtc,
            suggestion.CreatedByUserId,
            suggestion.UpdatedByUserId,
            suggestion.AcceptedUtc,
            suggestion.RejectedUtc,
            suggestion.SupersededUtc);

    private static ReconciliationResultRecordDto MapResult(ReconciliationResultRecord result) =>
        new(
            result.Id,
            result.CompanyId,
            result.AcceptedSuggestionId,
            result.SourceRecordType,
            result.SourceRecordId,
            result.TargetRecordType,
            result.TargetRecordId,
            result.MatchType,
            result.ConfidenceScore,
            CloneNodes(result.RuleBreakdown),
            result.CreatedUtc,
            result.UpdatedUtc,
            result.CreatedByUserId,
            result.UpdatedByUserId);

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await action();
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
