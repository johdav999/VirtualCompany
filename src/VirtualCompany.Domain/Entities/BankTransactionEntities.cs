using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyBankAccount : ICompanyOwnedEntity
{
    private CompanyBankAccount()
    {
    }

    public CompanyBankAccount(
        Guid id,
        Guid companyId,
        Guid financeAccountId,
        string displayName,
        string bankName,
        string maskedAccountNumber,
        string currency,
        string? externalCode = null,
        bool isPrimary = false,
        bool isActive = true,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FinanceAccountId = financeAccountId == Guid.Empty ? throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId)) : financeAccountId;
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 120);
        BankName = NormalizeRequired(bankName, nameof(bankName), 120);
        MaskedAccountNumber = NormalizeRequired(maskedAccountNumber, nameof(maskedAccountNumber), 64);
        Currency = NormalizeCurrency(currency, nameof(currency));
        ExternalCode = NormalizeOptional(externalCode, nameof(externalCode), 64);
        IsPrimary = isPrimary;
        IsActive = isActive;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string BankName { get; private set; } = null!;
    public string MaskedAccountNumber { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string? ExternalCode { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
    public ICollection<BankTransaction> Transactions { get; } = new List<BankTransaction>();

    public void SetPrimary(bool isPrimary, DateTime? updatedUtc = null)
    {
        IsPrimary = isPrimary;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void SetActive(bool isActive, DateTime? updatedUtc = null)
    {
        IsActive = isActive;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string NormalizeCurrency(string value, string name)
    {
        var normalized = NormalizeRequired(value, name, 3).ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw new ArgumentOutOfRangeException(name, "Currency must be a three-letter ISO code.");
        }

        return normalized;
    }
}

public sealed class BankTransaction : ICompanyOwnedEntity
{
    private BankTransaction()
    {
    }

    public BankTransaction(
        Guid id,
        Guid companyId,
        Guid bankAccountId,
        DateTime bookingDate,
        DateTime valueDate,
        decimal amount,
        string currency,
        string referenceText,
        string counterparty,
        string? externalReference = null,
        string? importSource = null,
        decimal reconciledAmount = 0m,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankAccountId = bankAccountId == Guid.Empty ? throw new ArgumentException("BankAccountId is required.", nameof(bankAccountId)) : bankAccountId;
        BookingDate = EntityTimestampNormalizer.NormalizeUtc(bookingDate, nameof(bookingDate));
        ValueDate = EntityTimestampNormalizer.NormalizeUtc(valueDate, nameof(valueDate));
        Amount = NormalizeAmount(amount, nameof(amount));
        Currency = NormalizeCurrency(currency, nameof(currency));
        ReferenceText = NormalizeOptionalOrFallback(referenceText, nameof(referenceText), 240, "Bank transaction");
        Counterparty = NormalizeOptionalOrFallback(counterparty, nameof(counterparty), 200, "Unknown counterparty");
        ExternalReference = NormalizeOptional(externalReference, nameof(externalReference), 128);
        ImportSource = NormalizeOptional(importSource, nameof(importSource), 64);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? BookingDate, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
        ApplyReconciliation(reconciledAmount, UpdatedUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public DateTime ValueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string ReferenceText { get; private set; } = null!;
    public string Counterparty { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal ReconciledAmount { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? ImportSource { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyBankAccount BankAccount { get; private set; } = null!;
    public ICollection<BankTransactionPaymentLink> PaymentLinks { get; } = new List<BankTransactionPaymentLink>();
    public ICollection<BankTransactionCashLedgerLink> CashLedgerLinks { get; } = new List<BankTransactionCashLedgerLink>();
    public BankTransactionPostingStateRecord? PostingStateRecord { get; private set; }

    public decimal AbsoluteAmount => Math.Abs(Amount);

    public void ApplyReconciliation(decimal reconciledAmount, DateTime? updatedUtc = null)
    {
        var normalized = NormalizeMoney(Math.Max(0m, reconciledAmount));
        if (normalized > AbsoluteAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciledAmount), "Reconciled amount cannot exceed the bank transaction amount.");
        }

        ReconciledAmount = normalized;
        Status = BankTransactionReconciliationStatuses.Resolve(AbsoluteAmount, ReconciledAmount);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    private static decimal NormalizeAmount(decimal value, string name)
    {
        if (value == 0m)
        {
            throw new ArgumentOutOfRangeException(name, "Amount cannot be zero.");
        }

        return NormalizeMoney(value);
    }

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeCurrency(string value, string name)
    {
        var normalized = NormalizeOptionalOrFallback(value, name, 3, string.Empty).ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw new ArgumentOutOfRangeException(name, "Currency must be a three-letter ISO code.");
        }

        return normalized;
    }

    private static string NormalizeOptionalOrFallback(string? value, string name, int maxLength, string fallback)
    {
        var normalized = NormalizeOptional(value, name, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class BankTransactionPaymentLink : ICompanyOwnedEntity
{
    private BankTransactionPaymentLink()
    {
    }

    public BankTransactionPaymentLink(
        Guid id,
        Guid companyId,
        Guid bankTransactionId,
        Guid paymentId,
        decimal allocatedAmount,
        string currency,
        DateTime? createdUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankTransactionId = bankTransactionId == Guid.Empty ? throw new ArgumentException("BankTransactionId is required.", nameof(bankTransactionId)) : bankTransactionId;
        PaymentId = paymentId == Guid.Empty ? throw new ArgumentException("PaymentId is required.", nameof(paymentId)) : paymentId;
        AllocatedAmount = decimal.Round(allocatedAmount, 2, MidpointRounding.AwayFromZero);
        if (AllocatedAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedAmount), "Allocated amount must be greater than zero.");
        }

        Currency = string.IsNullOrWhiteSpace(currency)
            ? throw new ArgumentException("Currency is required.", nameof(currency))
            : currency.Trim().ToUpperInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankTransactionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankTransaction BankTransaction { get; private set; } = null!;
    public Payment Payment { get; private set; } = null!;
}

public sealed class BankTransactionCashLedgerLink : ICompanyOwnedEntity
{
    private BankTransactionCashLedgerLink()
    {
    }

    public BankTransactionCashLedgerLink(
        Guid id,
        Guid companyId,
        Guid bankTransactionId,
        Guid ledgerEntryId,
        string idempotencyKey,
        DateTime? createdUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankTransactionId = bankTransactionId == Guid.Empty ? throw new ArgumentException("BankTransactionId is required.", nameof(bankTransactionId)) : bankTransactionId;
        LedgerEntryId = ledgerEntryId == Guid.Empty ? throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId)) : ledgerEntryId;
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new ArgumentException("IdempotencyKey is required.", nameof(idempotencyKey))
            : idempotencyKey.Trim();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankTransactionId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankTransaction BankTransaction { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;
}

public sealed class BankTransactionPostingStateRecord : ICompanyOwnedEntity
{
    private BankTransactionPostingStateRecord()
    {
    }

    public BankTransactionPostingStateRecord(
        Guid id,
        Guid companyId,
        Guid bankTransactionId,
        string matchingStatus,
        string postingState,
        int linkedPaymentCount,
        DateTime lastEvaluatedUtc,
        string? unmatchedReason = null,
        string? conflictCode = null,
        string? conflictDetails = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankTransactionId = bankTransactionId == Guid.Empty ? throw new ArgumentException("BankTransactionId is required.", nameof(bankTransactionId)) : bankTransactionId;
        MatchingStatus = NormalizeMatchingStatus(matchingStatus);
        PostingState = NormalizePostingState(postingState);
        LinkedPaymentCount = NormalizeLinkedPaymentCount(linkedPaymentCount);
        LastEvaluatedUtc = EntityTimestampNormalizer.NormalizeUtc(lastEvaluatedUtc, nameof(lastEvaluatedUtc));
        UnmatchedReason = NormalizeOptional(unmatchedReason, nameof(unmatchedReason), 128);
        ConflictCode = NormalizeOptional(conflictCode, nameof(conflictCode), 64);
        ConflictDetails = NormalizeOptional(conflictDetails, nameof(conflictDetails), 512);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? LastEvaluatedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankTransactionId { get; private set; }
    public string MatchingStatus { get; private set; } = null!;
    public string PostingState { get; private set; } = null!;
    public int LinkedPaymentCount { get; private set; }
    public DateTime LastEvaluatedUtc { get; private set; }
    public string? UnmatchedReason { get; private set; }
    public string? ConflictCode { get; private set; }
    public string? ConflictDetails { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankTransaction BankTransaction { get; private set; } = null!;

    public void SyncSnapshot(
        string matchingStatus,
        string postingState,
        int linkedPaymentCount,
        DateTime evaluatedUtc,
        string? unmatchedReason = null,
        string? conflictCode = null,
        string? conflictDetails = null)
    {
        MatchingStatus = NormalizeMatchingStatus(matchingStatus);
        PostingState = NormalizePostingState(postingState);
        LinkedPaymentCount = NormalizeLinkedPaymentCount(linkedPaymentCount);
        LastEvaluatedUtc = EntityTimestampNormalizer.NormalizeUtc(evaluatedUtc, nameof(evaluatedUtc));
        UnmatchedReason = NormalizeOptional(unmatchedReason, nameof(unmatchedReason), 128);
        ConflictCode = NormalizeOptional(conflictCode, nameof(conflictCode), 64);
        ConflictDetails = NormalizeOptional(conflictDetails, nameof(conflictDetails), 512);
        UpdatedUtc = LastEvaluatedUtc;
    }

    private static string NormalizeMatchingStatus(string value)
    {
        var normalized = BankTransactionMatchingStatuses.Normalize(value);
        if (!BankTransactionMatchingStatuses.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Unsupported bank transaction matching status.");
        }

        return normalized;
    }

    private static string NormalizePostingState(string value)
    {
        var normalized = BankTransactionPostingStates.Normalize(value);
        if (!BankTransactionPostingStates.IsSupported(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Unsupported bank transaction posting state.");
        }

        return normalized;
    }

    private static int NormalizeLinkedPaymentCount(int value) =>
        value < 0
            ? throw new ArgumentOutOfRangeException(nameof(value), "Linked payment count cannot be negative.")
            : value;

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}