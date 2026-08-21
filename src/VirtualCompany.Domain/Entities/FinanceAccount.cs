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
        DateTime? updatedUtc = null,
        string? accountClass = null,
        string? normalBalance = null,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        bool isPostingEnabled = false,
        string? controlAccountRole = null,
        bool restrictManualPosting = false)
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
        ApplyAccountingSemantics(
            accountClass,
            normalBalance,
            effectiveFrom,
            effectiveTo,
            isPostingEnabled,
            controlAccountRole,
            restrictManualPosting,
            UpdatedUtc);
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
    public string? AccountClass { get; private set; }
    public string? NormalBalance { get; private set; }
    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsPostingEnabled { get; private set; }
    public string? ControlAccountRole { get; private set; }
    public bool RestrictManualPosting { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceBalance> Balances { get; } = new List<FinanceBalance>();
    public ICollection<FinancialStatementMapping> FinancialStatementMappings { get; } = new List<FinancialStatementMapping>();

    public void Rename(string name, DateTime? updatedUtc = null)
    {
        Name = NormalizeRequired(name, nameof(name), 160);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void Deactivate(DateOnly effectiveTo, DateTime? updatedUtc = null)
    {
        if (EffectiveFrom.HasValue && effectiveTo < EffectiveFrom.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "EffectiveTo cannot be earlier than EffectiveFrom.");
        }

        EffectiveTo = effectiveTo;
        IsPostingEnabled = false;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void ApplyAccountingSemantics(
        string? accountClass,
        string? normalBalance,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo,
        bool isPostingEnabled,
        string? controlAccountRole,
        bool restrictManualPosting,
        DateTime? updatedUtc = null)
    {
        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value < effectiveFrom.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "EffectiveTo cannot be earlier than EffectiveFrom.");
        }

        AccountClass = FinanceAccountClassValues.NormalizeOptional(accountClass);
        NormalBalance = FinanceNormalBalanceValues.NormalizeOptional(normalBalance);
        if ((AccountClass is null) != (NormalBalance is null))
        {
            throw new ArgumentException("Account class and normal balance must be configured together.");
        }

        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsPostingEnabled = isPostingEnabled;
        ControlAccountRole = NormalizeOptional(controlAccountRole, nameof(controlAccountRole), 96)?.ToLowerInvariant();
        RestrictManualPosting = restrictManualPosting;
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

}

public static class FinanceAccountClassValues
{
    public const string Asset = "asset";
    public const string Liability = "liability";
    public const string Equity = "equity";
    public const string Income = "income";
    public const string Expense = "expense";

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                Asset => Asset,
                Liability => Liability,
                Equity => Equity,
                Income => Income,
                "revenue" => Income,
                Expense => Expense,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Account class is not supported.")
            };
}

public static class FinanceNormalBalanceValues
{
    public const string Debit = "debit";
    public const string Credit = "credit";

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                Debit => Debit,
                Credit => Credit,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Normal balance is not supported.")
            };
}

