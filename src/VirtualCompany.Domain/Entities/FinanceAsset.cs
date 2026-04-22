using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAsset : ICompanyOwnedEntity
{
    private FinanceAsset()
    {
    }

    public FinanceAsset(
        Guid id,
        Guid companyId,
        Guid counterpartyId,
        string referenceNumber,
        string name,
        string category,
        DateTime purchasedUtc,
        decimal amount,
        string currency,
        string fundingBehavior,
        string fundingSettlementStatus,
        string status,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (counterpartyId == Guid.Empty)
        {
            throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyId = counterpartyId;
        ReferenceNumber = NormalizeRequired(referenceNumber, nameof(referenceNumber), 64);
        Name = NormalizeRequired(name, nameof(name), 160);
        Category = NormalizeRequired(category, nameof(category), 64);
        PurchasedUtc = EntityTimestampNormalizer.NormalizeUtc(purchasedUtc, nameof(purchasedUtc));
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        FundingBehavior = NormalizeFundingBehavior(fundingBehavior);
        FundingSettlementStatus = NormalizeFundingSettlementStatus(fundingSettlementStatus);
        Status = NormalizeStatus(status);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? PurchasedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string ReferenceNumber { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public DateTime PurchasedUtc { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string FundingBehavior { get; private set; } = null!;
    public string FundingSettlementStatus { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceCounterparty Counterparty { get; private set; } = null!;

    public bool HasPayableExposure =>
        string.Equals(FundingBehavior, FinanceAssetFundingBehaviors.Payable, StringComparison.Ordinal) &&
        !string.Equals(FinanceSettlementStatuses.Normalize(FundingSettlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal) &&
        string.Equals(Status, FinanceAssetStatuses.Active, StringComparison.Ordinal);

    public void ApplyFundingSettlementStatus(string fundingSettlementStatus)
    {
        FundingSettlementStatus = NormalizeFundingSettlementStatus(fundingSettlementStatus);
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

    private static string NormalizeFundingBehavior(string value)
    {
        var normalized = FinanceAssetFundingBehaviors.Normalize(value);
        return FinanceAssetFundingBehaviors.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported asset funding behavior.");
    }

    private static string NormalizeFundingSettlementStatus(string value)
    {
        var normalized = FinanceSettlementStatuses.Normalize(value);
        return FinanceSettlementStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported funding settlement status.");
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = FinanceAssetStatuses.Normalize(value);
        return FinanceAssetStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported asset status.");
    }
}
