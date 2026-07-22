using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
public sealed class FinanceAccount : ICompanyOwnedEntity
{
    private FinanceAccount()
    {
    }

    public FinanceAccount(
        Guid id,
        Guid companyId,
        string code,
        string name,
        string accountType,
        string currency,
        decimal openingBalance,
        DateTime openedUtc,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = NormalizeRequired(code, nameof(code), 32);
        Name = NormalizeRequired(name, nameof(name), 160);
        AccountType = NormalizeRequired(accountType, nameof(accountType), 64);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        OpeningBalance = openingBalance;
        OpenedUtc = EntityTimestampNormalizer.NormalizeUtc(openedUtc, nameof(openedUtc));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? OpenedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public void ApplySyncedSnapshot(string code, string name, string accountType, string currency, decimal openingBalance, DateTime openedUtc, DateTime updatedUtc)
    {
        Code = NormalizeRequired(code, nameof(code), 32);
        Name = NormalizeRequired(name, nameof(name), 160);
        AccountType = NormalizeRequired(accountType, nameof(accountType), 64);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        OpeningBalance = openingBalance;
        OpenedUtc = EntityTimestampNormalizer.NormalizeUtc(openedUtc, nameof(openedUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string AccountType { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal OpeningBalance { get; private set; }
    public DateTime OpenedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceBalance> Balances { get; } = new List<FinanceBalance>();
    public ICollection<FinancialStatementMapping> FinancialStatementMappings { get; } = new List<FinancialStatementMapping>();

    public void Rename(string name)
    {
        Name = NormalizeRequired(name, nameof(name), 160);
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

