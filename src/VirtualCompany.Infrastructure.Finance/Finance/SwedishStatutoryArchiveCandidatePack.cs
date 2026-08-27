using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SwedishStatutoryArchiveCandidatePack : IAccountingPolicyPack
{
    public const string Version = "1.3.0";

    public SwedishStatutoryArchiveCandidatePack()
    {
        var prior = new SwedishStatutoryDocumentCandidatePack().Definition;
        var exports = prior.SupportedExports.Concat([
            AccountingExportTypeValues.Sie4B,
            AccountingExportTypeValues.SwedishStatutoryArchive
        ]).Distinct(StringComparer.Ordinal).ToArray();
        var capabilities = prior.SupportedCapabilities.Concat([AccountingPolicyCapabilityKeys.StatutoryExport])
            .Distinct(StringComparer.Ordinal).ToArray();
        var states = new Dictionary<string, string>(prior.CapabilityStates ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [AccountingPolicyCapabilityKeys.StatutoryExport] = "supported_unvalidated_sie_4b"
        };
        var metadata = new Dictionary<string, string>(prior.PolicyMetadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["sie_specification"] = Sie4BSerializer.SpecificationVersion,
            ["statutory_archive_schema"] = "virtual-company.swedish-statutory-accounting-archive/1.0",
            ["statutory_export_review_state"] = "implementation_verified_reviewer_validation_pending"
        };
        Definition = prior with
        {
            Version = Version,
            DisplayName = "Sweden SIE 4B and statutory archive candidate",
            ComplianceNotice = "This Swedish candidate is not statutorily validated. SIE 4B and archive generation follow the checked-in format specification; qualified reviewer validation remains pending.",
            SupportedExports = exports,
            SupportedCapabilities = capabilities,
            PolicyMetadata = metadata,
            CapabilityStates = states
        };
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Definition)))).ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
