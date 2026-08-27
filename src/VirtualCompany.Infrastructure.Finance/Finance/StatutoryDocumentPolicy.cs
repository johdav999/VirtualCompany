using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class StatutoryDocumentPolicy : IStatutoryDocumentPolicy
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingPolicyPackResolver _packs;

    public StatutoryDocumentPolicy(VirtualCompanyDbContext db, IAccountingPolicyPackResolver packs)
    {
        _db = db;
        _packs = packs;
    }

    public async Task<StatutoryDocumentPolicyDecisionDto> EvaluateAsync(PreviewStatutoryDocumentQuery query, CancellationToken cancellationToken)
    {
        var issues = new List<StatutoryDocumentPolicyIssueDto>();
        var input = query.Document;
        var type = NormalizeType(input.DocumentType, issues);
        var authority = NormalizeAuthority(input.Authority, issues);
        var configuration = await _db.AccountingConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var profile = await _db.CompanyStatutoryProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        IAccountingPolicyPack? pack = null;
        if (configuration is null || !_packs.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out pack) || pack is null)
            Add(StatutoryDocumentReasonCodes.ConfigurationUnavailable, "Complete accounting setup with a compatible Swedish policy pack before validating statutory documents.");
        else if (!pack.Definition.SupportedCapabilities.Contains("native_statutory_invoice_issuance", StringComparer.OrdinalIgnoreCase))
            Add(StatutoryDocumentReasonCodes.NativeIssuanceUnavailable, "The selected policy pack does not support the statutory document workflow.");

        if (profile is null || !profile.IsFormatComplete || !profile.IsUserAttested)
            Add(StatutoryDocumentReasonCodes.ConfigurationUnavailable, "Complete and attest the Swedish statutory identity before issuing or registering a statutory document.", "seller_identity");
        else if (profile.VatRegistrationStatus == StatutoryVatRegistrationStatusValues.Registered && string.IsNullOrWhiteSpace(profile.VatRegistrationNumber))
            Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "The seller VAT registration number is required for a VAT-registered seller.", "seller_vat_identifier");
        else
        {
            if (profile.OrganisationRegistrationEffectiveFrom is null || profile.OrganisationRegistrationEffectiveFrom > input.IssueDate)
                Add(StatutoryDocumentReasonCodes.DateInvalid, "The statutory identity must be effective on the issue date.", "issue_date");
            if (profile.VatRegistrationStatus == StatutoryVatRegistrationStatusValues.Registered &&
                (profile.VatRegistrationEffectiveFrom is null || profile.VatRegistrationEffectiveFrom > input.IssueDate ||
                 profile.VatRegistrationEffectiveTo is not null && profile.VatRegistrationEffectiveTo < input.IssueDate))
                Add(StatutoryDocumentReasonCodes.DateInvalid, "The stored VAT registration period must cover the issue date.", "issue_date");
        }
        if (configuration is not null && configuration.PolicyPackEffectiveFrom > input.IssueDate)
            Add(StatutoryDocumentReasonCodes.DateInvalid, "The selected policy pack is not effective on the issue date.", "issue_date");

        if (authority == StatutoryDocumentAuthorities.Native && type is not (StatutoryDocumentTypes.CustomerInvoice or StatutoryDocumentTypes.CustomerCredit))
            Add(StatutoryDocumentReasonCodes.NativeIssuanceUnavailable, "Native issuance is limited to customer invoices and customer credit notes.", "authority");
        if (authority != StatutoryDocumentAuthorities.Native && string.IsNullOrWhiteSpace(input.ProviderDocumentNumber))
            Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "An imported or provider-issued document must retain its original document number.", "provider_document_number");
        if (input.CounterpartyId == Guid.Empty) Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "A counterparty is required.", "counterparty_id");
        Required(input.CounterpartyLegalName, "Counterparty legal name is required.", "counterparty_legal_name");
        Required(input.CounterpartyAddressLine1, "Counterparty address is required.", "counterparty_address_line_1");
        Required(input.CounterpartyPostalCode, "Counterparty postal code is required.", "counterparty_postal_code");
        Required(input.CounterpartyCity, "Counterparty city is required.", "counterparty_city");
        if (string.IsNullOrWhiteSpace(input.CounterpartyCountryCode) || input.CounterpartyCountryCode.Trim().Length != 2)
            Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "A two-letter counterparty country code is required.", "counterparty_country_code");
        Required(input.Currency, "Currency is required.", "currency");
        Required(input.PaymentTerms, "Payment terms are required.", "payment_terms");
        Required(input.ExplanatoryText, "Explanatory document text is required.", "explanatory_text");
        if (configuration is not null && !string.Equals(input.Currency?.Trim(), configuration.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "The launch scope supports documents in the accounting currency only.", "currency");
        if (input.DueDate < input.IssueDate)
            Add(StatutoryDocumentReasonCodes.DateInvalid, "Due date cannot be earlier than the issue date.", "due_date");
        if (input.AccountingDate < input.IssueDate)
            Add(StatutoryDocumentReasonCodes.DateInvalid, "Accounting date cannot be earlier than the issue date in this launch scope.", "accounting_date");
        if (input.SupplyDate > input.IssueDate)
            Add(StatutoryDocumentReasonCodes.DateInvalid, "Supply date cannot be later than the issue date in this launch scope.", "supply_date");

        if (input.Lines is null || input.Lines.Count == 0)
            Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "At least one line item is required.", "lines");
        else
        {
            foreach (var (line, index) in input.Lines.Select((value, index) => (value, index)))
            {
                if (string.IsNullOrWhiteSpace(line.Description)) Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Every line needs a description.", $"lines[{index}].description");
                if (line.Quantity <= 0m) Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Every line quantity must be greater than zero.", $"lines[{index}].quantity");
                if (line.NetAmount < 0m || line.VatAmount < 0m || line.VatRate < 0m)
                    Add(StatutoryDocumentReasonCodes.TotalsMismatch, "Line net, VAT rate, and VAT amount cannot be negative; credit direction is represented by the document type.", $"lines[{index}]");
                if (!Equal(line.NetAmount, line.Quantity * line.UnitPrice))
                    Add(StatutoryDocumentReasonCodes.TotalsMismatch, "Line net amount must equal quantity multiplied by unit price.", $"lines[{index}].net_amount");
                if (!Equal(line.VatAmount, line.NetAmount * line.VatRate))
                    Add(StatutoryDocumentReasonCodes.TotalsMismatch, "Line VAT amount does not match the stated basis and rate.", $"lines[{index}].vat_amount");
            }
            if (!Equal(input.NetTotal, input.Lines.Sum(x => x.NetAmount)) || !Equal(input.VatTotal, input.Lines.Sum(x => x.VatAmount)) || !Equal(input.GrossTotal, input.NetTotal + input.VatTotal))
                Add(StatutoryDocumentReasonCodes.TotalsMismatch, "Document net, VAT, and gross totals must match the line totals.", "totals");
        }

        if (type is StatutoryDocumentTypes.CustomerCredit or StatutoryDocumentTypes.SupplierCredit && input.OriginalIssuedDocumentId is null)
            Add(StatutoryDocumentReasonCodes.CreditReferenceRequired, "A credit note must unambiguously reference the original issued document.", "original_issued_document_id");

        return new(issues.Count == 0, issues);

        void Required(string? value, string explanation, string field) { if (string.IsNullOrWhiteSpace(value)) Add(StatutoryDocumentReasonCodes.RequiredFieldMissing, explanation, field); }
        void Add(string code, string explanation, string? field = null) => issues.Add(new(code, explanation, field));
    }

    private static bool Equal(decimal left, decimal right) => decimal.Round(left, 2, MidpointRounding.AwayFromZero) == decimal.Round(right, 2, MidpointRounding.AwayFromZero);
    private static string NormalizeType(string value, List<StatutoryDocumentPolicyIssueDto> issues)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is not (StatutoryDocumentTypes.CustomerInvoice or StatutoryDocumentTypes.CustomerCredit or StatutoryDocumentTypes.SupplierInvoice or StatutoryDocumentTypes.SupplierCredit))
            issues.Add(new(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Document type is not supported.", "document_type"));
        return normalized;
    }
    private static string NormalizeAuthority(string value, List<StatutoryDocumentPolicyIssueDto> issues)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is not (StatutoryDocumentAuthorities.Native or StatutoryDocumentAuthorities.Provider or StatutoryDocumentAuthorities.Imported))
            issues.Add(new(StatutoryDocumentReasonCodes.RequiredFieldMissing, "Document number authority is not supported.", "authority"));
        return normalized;
    }
}
