namespace VirtualCompany.Domain.Entities;

public static class AccountingAuthorityValues
{
    public const string InternalLedger = "internal_ledger";
    public const string ExternalProvider = "external_provider";
    public const string Migration = "migration";

    public static string Normalize(string value) =>
        NormalizeToken(value, nameof(value)) switch
        {
            InternalLedger => InternalLedger,
            ExternalProvider => ExternalProvider,
            Migration => Migration,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting authority is not supported.")
        };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Accounting authority is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class AccountingSetupStateValues
{
    public const string Incomplete = "incomplete";
    public const string Ready = "ready";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Accounting setup state is required.", nameof(value))
            : value.Trim().ToLowerInvariant() switch
            {
                Incomplete => Incomplete,
                Ready => Ready,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting setup state is not supported.")
            };
}

public static class AccountingRoundingModeValues
{
    public const string MidpointToEven = "midpoint_to_even";
    public const string AwayFromZero = "away_from_zero";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Rounding mode is required.", nameof(value))
            : value.Trim().Replace('-', '_').ToLowerInvariant() switch
            {
                MidpointToEven => MidpointToEven,
                AwayFromZero => AwayFromZero,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Rounding mode is not supported.")
            };
}

public sealed class AccountingConfiguration : ICompanyOwnedEntity
{
    private AccountingConfiguration()
    {
    }

    public AccountingConfiguration(
        Guid id,
        Guid companyId,
        string baseCurrency,
        int fiscalYearStartMonth,
        int fiscalYearStartDay,
        string policyPackKey,
        string policyPackVersion,
        DateOnly policyPackEffectiveFrom,
        int roundingPrecision,
        string roundingMode,
        Guid createdByUserId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BaseCurrency = NormalizeCurrency(baseCurrency);
        ValidateFiscalYearStart(fiscalYearStartMonth, fiscalYearStartDay);
        FiscalYearStartMonth = fiscalYearStartMonth;
        FiscalYearStartDay = fiscalYearStartDay;
        Authority = AccountingAuthorityValues.InternalLedger;
        SetupState = AccountingSetupStateValues.Incomplete;
        PolicyPackKey = NormalizeRequired(policyPackKey, nameof(policyPackKey), 96).ToLowerInvariant();
        PolicyPackVersion = NormalizeRequired(policyPackVersion, nameof(policyPackVersion), 32);
        PolicyPackEffectiveFrom = policyPackEffectiveFrom;
        ValidateRoundingPrecision(roundingPrecision);
        RoundingPrecision = roundingPrecision;
        RoundingMode = AccountingRoundingModeValues.Normalize(roundingMode);
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string BaseCurrency { get; private set; } = null!;
    public int FiscalYearStartMonth { get; private set; }
    public int FiscalYearStartDay { get; private set; }
    public string Authority { get; private set; } = null!;
    public string SetupState { get; private set; } = null!;
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public DateOnly PolicyPackEffectiveFrom { get; private set; }
    public int RoundingPrecision { get; private set; }
    public string RoundingMode { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<AccountingConfigurationAccountRole> AccountRoles { get; } = new List<AccountingConfigurationAccountRole>();
    public ICollection<AccountingPolicyPackSelection> PolicyPackSelections { get; } = new List<AccountingPolicyPackSelection>();

    public void ApplyPolicyPack(
        string policyPackKey,
        string policyPackVersion,
        DateOnly effectiveFrom,
        Guid actorUserId,
        DateTime updatedUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        PolicyPackKey = NormalizeRequired(policyPackKey, nameof(policyPackKey), 96).ToLowerInvariant();
        PolicyPackVersion = NormalizeRequired(policyPackVersion, nameof(policyPackVersion), 32);
        PolicyPackEffectiveFrom = effectiveFrom;
        SetupState = AccountingSetupStateValues.Incomplete;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void SetSetupState(string setupState, Guid actorUserId, DateTime updatedUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        var normalized = AccountingSetupStateValues.Normalize(setupState);
        var normalizedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        if (SetupState == normalized)
        {
            return;
        }

        SetupState = normalized;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = normalizedUtc;
        Version++;
    }

    public void SetAuthority(string authority, Guid actorUserId, DateTime updatedUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        var normalized = AccountingAuthorityValues.Normalize(authority);
        var normalizedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        if (Authority == normalized)
        {
            return;
        }

        Authority = normalized;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = normalizedUtc;
        Version++;
    }

    private static string NormalizeCurrency(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 3).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Base currency must be a three-letter alphabetic code.", nameof(value));
        }

        return normalized;
    }

    private static void ValidateFiscalYearStart(int month, int day)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Fiscal year start month must be between 1 and 12.");
        }

        if (day < 1 || day > DateTime.DaysInMonth(2000, month))
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Fiscal year start day is not valid for the selected month.");
        }
    }

    private static void ValidateRoundingPrecision(int precision)
    {
        if (precision is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(precision), "Rounding precision must be between 0 and 6 decimal places.");
        }
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }
}
