using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class Payment : ICompanyOwnedEntity
{
    private Payment()
    {
    }

    public Payment(
        Guid id,
        Guid companyId,
        string paymentType,
        decimal amount,
        string currency,
        DateTime paymentDate,
        string method,
        string status,
        string counterpartyReference,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        PaymentType = PaymentTypes.Normalize(paymentType);
        if (!PaymentTypes.IsSupported(PaymentType))
        {
            throw new ArgumentOutOfRangeException(nameof(paymentType), paymentType, "Unsupported payment type.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        if (Currency.Length != 3 || !Currency.All(char.IsLetter))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Currency must be a three-letter ISO code.");
        }

        PaymentDate = EntityTimestampNormalizer.NormalizeUtc(paymentDate, nameof(paymentDate));
        Method = PaymentMethods.Normalize(method);
        if (!PaymentMethods.IsSupported(Method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported payment method.");
        }

        Status = PaymentStatuses.Normalize(status);
        if (!PaymentStatuses.IsSupported(Status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported payment status.");
        }

        CounterpartyReference = NormalizeRequired(counterpartyReference, nameof(counterpartyReference), 200);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? PaymentDate, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string PaymentType { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime PaymentDate { get; private set; }
    public string Method { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string CounterpartyReference { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<PaymentAllocation> Allocations { get; } = new List<PaymentAllocation>();
    public ICollection<PaymentCashLedgerLink> CashLedgerLinks { get; } = new List<PaymentCashLedgerLink>();

    public void ApplySyncedSnapshot(
        string paymentType,
        decimal amount,
        string currency,
        DateTime paymentDate,
        string method,
        string status,
        string counterpartyReference)
    {
        PaymentType = PaymentTypes.Normalize(paymentType);
        if (!PaymentTypes.IsSupported(PaymentType)) throw new ArgumentOutOfRangeException(nameof(paymentType), paymentType, "Unsupported payment type.");
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        PaymentDate = EntityTimestampNormalizer.NormalizeUtc(paymentDate, nameof(paymentDate));
        Method = PaymentMethods.Normalize(method);
        if (!PaymentMethods.IsSupported(Method)) throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported payment method.");
        Status = PaymentStatuses.Normalize(status);
        if (!PaymentStatuses.IsSupported(Status)) throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported payment status.");
        CounterpartyReference = NormalizeRequired(counterpartyReference, nameof(counterpartyReference), 200);
        UpdatedUtc = DateTime.UtcNow;
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
}

public sealed class PaymentCashLedgerLink : ICompanyOwnedEntity
{
    private PaymentCashLedgerLink()
    {
    }

    public PaymentCashLedgerLink(
        Guid id,
        Guid companyId,
        Guid paymentId,
        Guid ledgerEntryId,
        string sourceType,
        string sourceId,
        DateTime postedAtUtc,
        DateTime? createdUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        PaymentId = paymentId == Guid.Empty ? throw new ArgumentException("PaymentId is required.", nameof(paymentId)) : paymentId;
        LedgerEntryId = ledgerEntryId == Guid.Empty ? throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId)) : ledgerEntryId;
        SourceType = NormalizeRequired(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceId = NormalizeRequired(sourceId, nameof(sourceId), 128);
        PostedAtUtc = EntityTimestampNormalizer.NormalizeUtc(postedAtUtc, nameof(postedAtUtc));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? PostedAtUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public DateTime PostedAtUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Payment Payment { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;

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
}