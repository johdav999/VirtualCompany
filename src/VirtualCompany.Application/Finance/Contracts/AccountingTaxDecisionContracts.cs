namespace VirtualCompany.Application.Finance;

public static class AccountingTaxDirectionValues
{
    public const string Sales = "sales";
    public const string Purchase = "purchase";
    public const string Both = "both";
}

public static class AccountingTaxTreatmentValues
{
    public const string Legacy = "legacy";
    public const string Taxable = "taxable";
    public const string Exempt = "exempt";
    public const string ReverseCharge = "reverse_charge";
}

public static class AccountingTaxRecoverabilityValues
{
    public const string Legacy = "legacy";
    public const string Full = "full";
    public const string None = "none";
    public const string Partial = "partial";
}

public static class AccountingTaxableBasisMethodValues
{
    public const string LineAmount = "line_amount";
}

public static class AccountingTaxDecisionReasonCodes
{
    public const string Allowed = "tax_decision_allowed";
    public const string RuleUnavailable = "tax_rule_unavailable";
    public const string UnsupportedScenario = "tax_scenario_unsupported";
    public const string EvidenceMissing = "tax_evidence_missing";
    public const string RegistrationRequired = "company_vat_registration_required";
    public const string CompanyJurisdictionUnsupported = "company_jurisdiction_unsupported";
    public const string AccountingCurrencyUnsupported = "accounting_currency_unsupported";
    public const string BookkeepingMethodUnsupported = "bookkeeping_method_unsupported";
    public const string DocumentCurrencyUnsupported = "document_currency_unsupported";
    public const string AmbiguousRule = "tax_rule_ambiguous";
    public const string InvalidRule = "tax_rule_invalid";
}

public sealed record AccountingTaxEvidenceInput(
    string Classification,
    string? SourceReference = null);

public sealed record AccountingTaxDecisionInput(
    string RequestedRuleKey,
    DateOnly AccountingDate,
    string Direction,
    string DocumentType,
    string LineClassification,
    decimal LineAmount,
    int RoundingPrecision,
    string RoundingMode,
    string CompanyVatRegistrationStatus = "unknown",
    string CounterpartyJurisdiction = "unknown",
    string CounterpartyVatStatus = "unknown",
    IReadOnlySet<string>? EvidenceClassifications = null,
    string CompanyCountryCode = "unknown",
    string AccountingCurrency = "unknown",
    string BookkeepingMethod = "unknown",
    string DocumentCurrency = "unknown",
    IReadOnlyList<AccountingTaxEvidenceInput>? Evidence = null);

public sealed record AccountingTaxDecision(
    bool IsAllowed,
    string ReasonCode,
    string Explanation,
    IReadOnlyList<string> RequiredEvidence,
    string? RuleKey,
    string? RuleVersion,
    decimal TaxableBasis,
    decimal TaxAmount,
    decimal GrossAmount,
    decimal? Rate,
    string? AmountMethod,
    string? Treatment,
    string? LiabilityAccountRoleKey,
    string? RecoverableAccountRoleKey,
    string Recoverability,
    IReadOnlyList<string> VatBoxMappings,
    string EvidenceClassification,
    IReadOnlyList<AccountingTaxEvidenceInput>? SuppliedEvidence = null);

public interface IAccountingTaxDecisionPolicy
{
    AccountingTaxDecision Decide(IAccountingPolicyPack pack, AccountingTaxDecisionInput input);
    IReadOnlyList<AccountingConfigurationIssueDto> Validate(IAccountingPolicyPack pack);
}
