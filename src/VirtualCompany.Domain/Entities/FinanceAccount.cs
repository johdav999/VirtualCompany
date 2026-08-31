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
        bool restrictManualPosting = false,
        bool isReportable = true,
        string postingRestriction = FinanceAccountPostingRestrictionValues.None,
        Guid? replacementAccountId = null,
        string? lifecycleReason = null,
        long lifecycleVersion = 1)
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
        IsReportable = isReportable;
        PostingRestriction = FinanceAccountPostingRestrictionValues.Normalize(postingRestriction);
        ReplacementAccountId = NormalizeReplacement(replacementAccountId);
        LifecycleReason = NormalizeOptional(lifecycleReason, nameof(lifecycleReason), 512);
        LifecycleVersion = lifecycleVersion > 0 ? lifecycleVersion : throw new ArgumentOutOfRangeException(nameof(lifecycleVersion));
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
    public bool IsReportable { get; private set; }
    public string PostingRestriction { get; private set; } = FinanceAccountPostingRestrictionValues.None;
    public Guid? ReplacementAccountId { get; private set; }
    public string? LifecycleReason { get; private set; }
    public long LifecycleVersion { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceBalance> Balances { get; } = new List<FinanceBalance>();
    public ICollection<FinancialStatementMapping> FinancialStatementMappings { get; } = new List<FinancialStatementMapping>();
    public FinanceAccount? ReplacementAccount { get; private set; }
    public ICollection<AccountingAccountLifecycleHistory> LifecycleHistory { get; } = new List<AccountingAccountLifecycleHistory>();

    public void Rename(string name, DateTime? updatedUtc = null)
    {
        Name = NormalizeRequired(name, nameof(name), 160);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
        LifecycleVersion++;
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
        PostingRestriction = FinanceAccountPostingRestrictionValues.All;
        LifecycleVersion++;
    }

    public void ApplyGovernedLifecycle(
        string name,
        string accountClass,
        string normalBalance,
        bool isReportable,
        string postingRestriction,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? replacementAccountId,
        string reason,
        DateTime updatedUtc)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "EffectiveTo cannot be earlier than EffectiveFrom.");

        Name = NormalizeRequired(name, nameof(name), 160);
        AccountClass = FinanceAccountClassValues.NormalizeOptional(accountClass)
            ?? throw new ArgumentException("Account class is required.", nameof(accountClass));
        NormalBalance = FinanceNormalBalanceValues.NormalizeOptional(normalBalance)
            ?? throw new ArgumentException("Normal balance is required.", nameof(normalBalance));
        IsReportable = isReportable;
        PostingRestriction = FinanceAccountPostingRestrictionValues.Normalize(postingRestriction);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        ReplacementAccountId = NormalizeReplacement(replacementAccountId);
        LifecycleReason = NormalizeRequired(reason, nameof(reason), 512);
        IsPostingEnabled = PostingRestriction != FinanceAccountPostingRestrictionValues.All;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        LifecycleVersion++;
    }

    private Guid? NormalizeReplacement(Guid? replacementAccountId)
    {
        if (!replacementAccountId.HasValue) return null;
        if (replacementAccountId.Value == Guid.Empty) throw new ArgumentException("Replacement account cannot be empty.", nameof(replacementAccountId));
        if (replacementAccountId.Value == Id) throw new ArgumentException("An account cannot replace itself.", nameof(replacementAccountId));
        return replacementAccountId;
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

public static class FinanceAccountPostingRestrictionValues
{
    public const string None = "none";
    public const string Manual = "manual";
    public const string All = "all";

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? None
        : value.Trim().ToLowerInvariant() switch
        {
            None => None,
            Manual => Manual,
            All => All,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Posting restriction is not supported.")
        };
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

