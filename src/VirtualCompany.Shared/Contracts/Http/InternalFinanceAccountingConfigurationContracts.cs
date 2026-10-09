using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
public sealed class ApplyAccountingPolicyPackRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public long ExpectedVersion { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}

public sealed class PreviewAccountingPolicyPackRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}

public sealed class CreateAccountingConfigurationRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public int FiscalYearStartMonth { get; set; } = 1;
    public int FiscalYearStartDay { get; set; } = 1;
    public string? PolicyPackKey { get; set; }
    public string? PolicyPackVersion { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public int RoundingPrecision { get; set; } = 2;
    public string? RoundingMode { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}

public sealed class SaveCompanyStatutoryProfileRequest
{
    public long? ExpectedVersion { get; set; }
    public string? LegalName { get; set; }
    public string? SwedishOrganisationNumber { get; set; }
    public string? VatRegistrationNumber { get; set; }
    public string VatRegistrationStatus { get; set; } = "not_registered";
    public StatutoryAddressDto RegisteredAddress { get; set; } = new(null, null, null, null, null);
    public StatutoryAddressDto? CorrespondenceAddress { get; set; }
    public string CountryCode { get; set; } = "SE";
    public string AccountingCurrency { get; set; } = "SEK";
    public string FiscalYearBasis { get; set; } = "calendar_year";
    public string BookkeepingMethod { get; set; } = "not_specified";
    public DateOnly? OrganisationRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveFrom { get; set; }
    public DateOnly? VatRegistrationEffectiveTo { get; set; }
    public bool IsUserAttested { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string SourceKind { get; set; } = "user_entry";
    public string? SourceReference { get; set; }
    public DateTime? SourceCapturedUtc { get; set; }
    public string? ExternalVerifier { get; set; }
    public DateTime? ExternallyVerifiedUtc { get; set; }
}
