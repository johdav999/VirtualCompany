namespace VirtualCompany.Domain.Entities;

public static class FinancePlanningVersions
{
    public const string Baseline = "baseline";
}

public sealed class Budget : ICompanyOwnedEntity
{
    private Budget()
    {
    }

    public Budget(
        Guid id,
        Guid companyId,
        Guid financeAccountId,
        DateTime periodStartUtc,
        string version,
        decimal amount,
        string currency,
        Guid? costCenterId = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("CostCenterId cannot be empty.", nameof(costCenterId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FinanceAccountId = financeAccountId;
        PeriodStartUtc = FinancePlanningEntityRules.NormalizePeriodStart(periodStartUtc, nameof(periodStartUtc));
        Version = FinancePlanningEntityRules.NormalizeVersion(version);
        Amount = amount;
        Currency = FinancePlanningEntityRules.NormalizeCurrency(currency);
        CostCenterId = costCenterId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public string Version { get; private set; } = null!;
    public Guid? CostCenterId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    public void Update(
        Guid financeAccountId,
        DateTime periodStartUtc,
        string version,
        decimal amount,
        string currency,
        Guid? costCenterId = null)
    {
        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("CostCenterId cannot be empty.", nameof(costCenterId));
        }

        FinanceAccountId = financeAccountId;
        PeriodStartUtc = FinancePlanningEntityRules.NormalizePeriodStart(periodStartUtc, nameof(periodStartUtc));
        Version = FinancePlanningEntityRules.NormalizeVersion(version);
        Amount = amount;
        Currency = FinancePlanningEntityRules.NormalizeCurrency(currency);
        CostCenterId = costCenterId;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class Forecast : ICompanyOwnedEntity
{
    private Forecast()
    {
    }

    public Forecast(
        Guid id,
        Guid companyId,
        Guid financeAccountId,
        DateTime periodStartUtc,
        string version,
        decimal amount,
        string currency,
        Guid? costCenterId = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("CostCenterId cannot be empty.", nameof(costCenterId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FinanceAccountId = financeAccountId;
        PeriodStartUtc = FinancePlanningEntityRules.NormalizePeriodStart(periodStartUtc, nameof(periodStartUtc));
        Version = FinancePlanningEntityRules.NormalizeVersion(version);
        Amount = amount;
        Currency = FinancePlanningEntityRules.NormalizeCurrency(currency);
        CostCenterId = costCenterId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public string Version { get; private set; } = null!;
    public Guid? CostCenterId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    public void Update(
        Guid financeAccountId,
        DateTime periodStartUtc,
        string version,
        decimal amount,
        string currency,
        Guid? costCenterId = null)
    {
        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("CostCenterId cannot be empty.", nameof(costCenterId));
        }

        FinanceAccountId = financeAccountId;
        PeriodStartUtc = FinancePlanningEntityRules.NormalizePeriodStart(periodStartUtc, nameof(periodStartUtc));
        Version = FinancePlanningEntityRules.NormalizeVersion(version);
        Amount = amount;
        Currency = FinancePlanningEntityRules.NormalizeCurrency(currency);
        CostCenterId = costCenterId;
        UpdatedUtc = DateTime.UtcNow;
    }
}

internal static class FinancePlanningEntityRules
{
    public static DateTime NormalizePeriodStart(DateTime value, string name)
    {
        var normalized = EntityTimestampNormalizer.NormalizeUtc(value, name);
        return new DateTime(normalized.Year, normalized.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Version is required.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Version must be 64 characters or fewer.");
        }

        return trimmed;
    }

    public static string NormalizeCurrency(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Currency is required.", nameof(value));
        }

        var trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.Length != 3 || !trimmed.All(char.IsLetter))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Currency must be a three-letter ISO code.");
        }

        return trimmed;
    }
}