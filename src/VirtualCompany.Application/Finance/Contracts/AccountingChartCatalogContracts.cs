namespace VirtualCompany.Application.Finance;

public static class AccountingChartCatalogDefaults
{
    public const string Bas2026CatalogKey = "bas-2026";
    public const string Bas2026CatalogVersion = "1.1";
}

public sealed record AccountingChartCatalogAccountDefinition(
    string Code,
    string NameSv,
    IReadOnlyList<string> NameVariantsSv,
    bool IsK2Allowed,
    bool IsSubAccount,
    string? ParentAccountCode,
    string GroupCode,
    string GroupNameSv,
    string? SuggestedAccountClass,
    string? SuggestedNormalBalance);

public interface IAccountingChartCatalog
{
    string CatalogKey { get; }
    string CatalogVersion { get; }
    string DisplayName { get; }
    string Locale { get; }
    string SourceFileName { get; }
    string SourceSha256 { get; }
    IReadOnlyList<string> Limitations { get; }
    IReadOnlyList<AccountingChartCatalogAccountDefinition> Accounts { get; }
    bool TryGetAccount(string code, out AccountingChartCatalogAccountDefinition? account);
}

public interface IAccountingChartCatalogResolver
{
    IAccountingChartCatalog Resolve(string catalogKey, string catalogVersion);
    IReadOnlyList<IAccountingChartCatalog> GetAll();
}

public sealed record AccountingChartCatalogAccountDto(
    string Code,
    string NameSv,
    IReadOnlyList<string> NameVariantsSv,
    bool RequiresNameSelection,
    bool IsK2Allowed,
    bool IsSubAccount,
    string? ParentAccountCode,
    string GroupCode,
    string GroupNameSv,
    string? SuggestedAccountClass,
    string? SuggestedNormalBalance,
    bool RequiresSemanticsConfirmation,
    bool RequiresCompanySuitabilityConfirmation,
    bool IsAlreadyAdded);

public sealed record AccountingChartCatalogGroupDto(string Code, string NameSv);

public sealed record AccountingChartCatalogPageDto(
    string CatalogKey,
    string CatalogVersion,
    string DisplayName,
    string Locale,
    string SourceFileName,
    string SourceSha256,
    int TotalAccountCount,
    int MatchedAccountCount,
    int Skip,
    int Take,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<AccountingChartCatalogGroupDto> Groups,
    IReadOnlyList<AccountingChartCatalogAccountDto> Accounts);

public sealed record GetAccountingChartCatalogQuery(
    Guid CompanyId,
    string CatalogKey,
    string CatalogVersion,
    string? Search = null,
    string? GroupCode = null,
    bool K2Only = false,
    bool ExcludeExisting = false,
    int Skip = 0,
    int Take = 100);

public sealed record CreateAccountingAccountFromCatalogCommand(
    Guid CompanyId,
    string CatalogKey,
    string CatalogVersion,
    string Code,
    string? NameSv,
    string? AccountClass,
    string? NormalBalance,
    bool AccountingSemanticsConfirmed,
    bool CompanySuitabilityConfirmed,
    DateOnly EffectiveFrom,
    Guid ActorUserId,
    string? CorrelationId = null);
