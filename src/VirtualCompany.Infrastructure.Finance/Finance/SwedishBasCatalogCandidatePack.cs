using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SwedishCandidateAccountingPolicyPack : IAccountingPolicyPack
{
    public const string ChartTemplateKey = SwedishDomesticVatCandidatePackV1_1.ChartTemplateKey;
    public const string DomesticSales25RuleKey = SwedishDomesticVatCandidatePackV1_1.DomesticSales25RuleKey;
    public const string DomesticPurchase25RuleKey = SwedishDomesticVatCandidatePackV1_1.DomesticPurchase25RuleKey;

    public SwedishCandidateAccountingPolicyPack()
    {
        var prior = new SwedishStatutoryArchiveCandidatePack().Definition;
        var metadata = new Dictionary<string, string>(
            prior.PolicyMetadata ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
        {
            ["chart_catalog_key"] = AccountingChartCatalogDefaults.Bas2026CatalogKey,
            ["chart_catalog_version"] = AccountingChartCatalogDefaults.Bas2026CatalogVersion,
            ["chart_catalog_sha256"] = Bas2026AccountingChartCatalog.ExpectedCatalogSha256,
            ["chart_catalog_source_sha256"] = Bas2026AccountingChartCatalog.ExpectedSourceSha256,
            ["chart_catalog_scope"] = "account_selection_source_not_automatic_role_assignment",
            ["chart_catalog_review_state"] = "implementation_verified_reviewer_validation_pending"
        };

        Definition = prior with
        {
            Version = AccountingPolicyPackDefaults.SwedishCandidateVersion,
            DisplayName = "Sweden accounting and BAS 2026 catalogue candidate",
            ComplianceNotice = "This Swedish candidate is not statutorily validated. The exact BAS 2026 catalogue and source workbook hashes are bound to this immutable version; catalogue account semantics and company suitability still require explicit accountant confirmation.",
            PolicyMetadata = metadata
        };
        DefinitionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Definition))))
            .ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
