namespace VirtualCompany.Application.Finance;

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
