namespace VirtualCompany.Application.Finance;
public sealed record AccountingChartCatalogAccountDto(string Code, string NameSv, IReadOnlyList<string> NameVariantsSv, bool RequiresNameSelection, bool IsK2Allowed, bool IsSubAccount, string? ParentAccountCode, string GroupCode, string GroupNameSv, string? SuggestedAccountClass, string? SuggestedNormalBalance, bool RequiresSemanticsConfirmation, bool RequiresCompanySuitabilityConfirmation, bool IsAlreadyAdded)
{
    public string Code { get; set; } = Code;
    public string NameSv { get; set; } = NameSv;
    public IReadOnlyList<string> NameVariantsSv { get; set; } = NameVariantsSv;
    public bool RequiresNameSelection { get; set; } = RequiresNameSelection;
    public bool IsK2Allowed { get; set; } = IsK2Allowed;
    public bool IsSubAccount { get; set; } = IsSubAccount;
    public string? ParentAccountCode { get; set; } = ParentAccountCode;
    public string GroupCode { get; set; } = GroupCode;
    public string GroupNameSv { get; set; } = GroupNameSv;
    public string? SuggestedAccountClass { get; set; } = SuggestedAccountClass;
    public string? SuggestedNormalBalance { get; set; } = SuggestedNormalBalance;
    public bool RequiresSemanticsConfirmation { get; set; } = RequiresSemanticsConfirmation;
    public bool RequiresCompanySuitabilityConfirmation { get; set; } = RequiresCompanySuitabilityConfirmation;
    public bool IsAlreadyAdded { get; set; } = IsAlreadyAdded;

    public AccountingChartCatalogAccountDto() : this(string.Empty, string.Empty, [], default !, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingChartCatalogPageDto(string CatalogKey, string CatalogVersion, string DisplayName, string Locale, string SourceFileName, string SourceSha256, int TotalAccountCount, int MatchedAccountCount, int Skip, int Take, IReadOnlyList<string> Limitations, IReadOnlyList<AccountingChartCatalogGroupDto> Groups, IReadOnlyList<AccountingChartCatalogAccountDto> Accounts)
{
    public string CatalogKey { get; set; } = CatalogKey;
    public string CatalogVersion { get; set; } = CatalogVersion;
    public string DisplayName { get; set; } = DisplayName;
    public string Locale { get; set; } = Locale;
    public string SourceFileName { get; set; } = SourceFileName;
    public string SourceSha256 { get; set; } = SourceSha256;
    public int TotalAccountCount { get; set; } = TotalAccountCount;
    public int MatchedAccountCount { get; set; } = MatchedAccountCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;
    public IReadOnlyList<string> Limitations { get; set; } = Limitations;
    public IReadOnlyList<AccountingChartCatalogGroupDto> Groups { get; set; } = Groups;
    public IReadOnlyList<AccountingChartCatalogAccountDto> Accounts { get; set; } = Accounts;

    public AccountingChartCatalogPageDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, [], [], [])
    {
    }
}

public sealed record AccountingChartCatalogGroupDto(string Code, string NameSv)
{
    public string Code { get; set; } = Code;
    public string NameSv { get; set; } = NameSv;

    public AccountingChartCatalogGroupDto() : this(string.Empty, string.Empty)
    {
    }
}

public static class AccountingChartCatalogDefaults
{
    public const string Bas2026CatalogKey = "bas-2026";
    public const string Bas2026CatalogVersion = "1.1";
}
