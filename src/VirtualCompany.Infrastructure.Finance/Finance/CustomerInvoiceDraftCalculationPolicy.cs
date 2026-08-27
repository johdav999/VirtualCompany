using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceDraftCalculationPolicy : ICustomerInvoiceDraftCalculationPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAccountingTaxDecisionPolicy _taxDecisionPolicy;

    public CustomerInvoiceDraftCalculationPolicy(VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver, IAccountingTaxDecisionPolicy taxDecisionPolicy)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _taxDecisionPolicy = taxDecisionPolicy;
    }

    public async Task<CustomerInvoiceDraftCalculation> CalculateAsync(Guid companyId,
        CustomerInvoiceDraftInput input, CancellationToken cancellationToken)
    {
        var blockers = new List<CustomerInvoiceDraftIssue>();
        var warnings = new List<CustomerInvoiceDraftIssue>();
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var customer = await _dbContext.CustomerBillingProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CounterpartyId == input.CustomerId, cancellationToken);
        var statutory = await _dbContext.CompanyStatutoryProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        IAccountingPolicyPack? pack = null;
        if (configuration is null)
        {
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                "Accounting must be configured before invoice tax can be previewed."));
        }
        else if (!_packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out pack) || pack is null)
        {
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                "The selected accounting policy pack is not available."));
        }

        if (statutory is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.StatutoryProfileMissing,
                "Complete the company statutory profile before preparing an invoice for issue."));
        if (customer is null)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.CustomerProfileMissing,
                "Complete the customer billing profile before preparing an invoice for issue.", input.CustomerId));

        var precision = configuration?.RoundingPrecision ?? 2;
        var roundingMode = configuration?.RoundingMode ?? AccountingRoundingModeValues.MidpointToEven;
        var packKey = pack?.Definition.PackKey ?? "unavailable";
        var packVersion = pack?.Definition.Version ?? "unavailable";
        var packHash = pack?.DefinitionHash ?? HashText("unavailable");
        var inputHash = ComputeInputHash(input, configuration?.Id, packHash, customer?.Version, statutory?.Version);
        var calculated = new List<CustomerInvoiceDraftCalculatedLine>(input.Lines.Count);

        if (input.Lines.Count == 0)
            blockers.Add(new(CustomerInvoiceDraftReasonCodes.LinesRequired,
                "Add at least one invoice line before previewing totals."));

        var seenSequences = new HashSet<int>();
        foreach (var line in input.Lines.OrderBy(x => x.Sequence))
        {
            if (line.Sequence <= 0 || !seenSequences.Add(line.Sequence))
                throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CalculationBlocked,
                    "Invoice line sequences must be positive and unique.");
            if (line.Quantity <= 0m || line.UnitPrice < 0m || line.DiscountPercent is < 0m or > 100m)
                throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CalculationBlocked,
                    $"Invoice line {line.Sequence} has an invalid quantity, price, or discount.");

            var extended = Round(line.Quantity * line.UnitPrice, precision, roundingMode);
            var discount = Round(extended * line.DiscountPercent / 100m, precision, roundingMode);
            var net = Round(extended - discount, precision, roundingMode);
            AccountingTaxDecision? decision = null;
            if (pack is not null && statutory is not null && customer is not null)
            {
                decision = _taxDecisionPolicy.Decide(pack, new AccountingTaxDecisionInput(
                    line.TaxRuleKey, input.IssueDate, AccountingTaxDirectionValues.Sales, input.DocumentType,
                    line.TaxClassification, net, precision, roundingMode, statutory.VatRegistrationStatus,
                    customer.BillingCountryCode, string.IsNullOrWhiteSpace(customer.VatIdentifier) ? "not_registered" : "registered",
                    CompanyCountryCode: statutory.CountryCode, AccountingCurrency: statutory.AccountingCurrency,
                    BookkeepingMethod: statutory.BookkeepingMethod, DocumentCurrency: input.Currency,
                    Evidence: line.TaxEvidence.Select(x => new AccountingTaxEvidenceInput(x.Classification, x.SourceReference)).ToArray()));
                if (!decision.IsAllowed)
                    blockers.Add(new(CustomerInvoiceDraftReasonCodes.UnsupportedTax,
                        $"Line {line.Sequence}: {decision.Explanation}"));
            }

            var tax = decision is { IsAllowed: true } ? decision.TaxAmount : 0m;
            var gross = decision is { IsAllowed: true } ? decision.GrossAmount : net;
            calculated.Add(new(line.Sequence, discount, net, decision?.RuleVersion ?? "unresolved",
                decision?.Rate ?? 0m, tax, gross, decision?.LiabilityAccountRoleKey,
                decision?.VatBoxMappings ?? []));
        }

        var netTotal = Round(calculated.Sum(x => x.NetAmount), precision, roundingMode);
        var discountTotal = Round(calculated.Sum(x => x.DiscountAmount), precision, roundingMode);
        var taxTotal = Round(calculated.Sum(x => x.TaxAmount), precision, roundingMode);
        var grossFromLines = calculated.Sum(x => x.GrossAmount);
        var grossTotal = Round(netTotal + taxTotal, precision, roundingMode);
        var roundingAmount = Round(grossTotal - grossFromLines, precision, roundingMode);
        if (roundingAmount != 0m)
            warnings.Add(new("customer_invoice_draft_rounding_applied",
                $"A rounding adjustment of {roundingAmount:0.######} {input.Currency} was applied to the invoice total."));

        var resultHash = HashText(JsonSerializer.Serialize(new
        {
            inputHash,
            packKey,
            packVersion,
            packHash,
            precision,
            roundingMode,
            Lines = calculated.Select(x => new { x.Sequence, DiscountAmount = Number(x.DiscountAmount),
                NetAmount = Number(x.NetAmount), x.TaxRuleVersion, TaxRate = Number(x.TaxRate),
                TaxAmount = Number(x.TaxAmount), GrossAmount = Number(x.GrossAmount), x.TaxAccountRoleKey,
                x.VatBoxMappings }),
            NetTotal = Number(netTotal),
            DiscountTotal = Number(discountTotal),
            TaxTotal = Number(taxTotal),
            GrossTotal = Number(grossTotal),
            RoundingAmount = Number(roundingAmount),
            Blockers = blockers.OrderBy(x => x.ReasonCode).ThenBy(x => x.Explanation),
            Warnings = warnings.OrderBy(x => x.ReasonCode).ThenBy(x => x.Explanation)
        }, JsonOptions));

        return new(inputHash, resultHash, packKey, packVersion, packHash, precision, roundingMode,
            netTotal, discountTotal, taxTotal, grossTotal, roundingAmount, calculated,
            warnings.ToArray(), blockers.ToArray());
    }

    private static string ComputeInputHash(CustomerInvoiceDraftInput input, Guid? configurationId,
        string packHash, long? customerVersion, long? statutoryVersion) => HashText(JsonSerializer.Serialize(new
    {
        input.CustomerId,
        input.DocumentType,
        input.IssueDate,
        input.SupplyDate,
        input.DueDate,
        Currency = input.Currency.Trim().ToUpperInvariant(),
        input.PaymentTermKind,
        input.PaymentTermDays,
        input.BuyerReference,
        input.SellerReference,
        input.Notes,
        input.DeliveryIntent,
        input.SourceKind,
        input.SourceReference,
        input.OriginalInvoiceId,
        Lines = input.Lines.OrderBy(x => x.Sequence).Select(x => new
        {
            x.Sequence,
            x.Description,
            Quantity = Number(x.Quantity),
            x.Unit,
            UnitPrice = Number(x.UnitPrice),
            DiscountPercent = Number(x.DiscountPercent),
            x.TaxRuleKey,
            x.TaxClassification,
            Evidence = x.TaxEvidence.OrderBy(y => y.Classification).ThenBy(y => y.SourceReference),
            Dimensions = (x.DimensionFacts ?? new Dictionary<string, string>()).OrderBy(y => y.Key),
            x.RevenueAccountRoleKey,
            x.SourceReference,
            x.OrderReference
        }),
        EvidenceDocuments = input.EvidenceDocumentIds.OrderBy(x => x),
        configurationId,
        packHash,
        customerVersion,
        statutoryVersion
    }, JsonOptions));

    private static decimal Round(decimal value, int precision, string mode) => decimal.Round(value, precision,
        mode == AccountingRoundingModeValues.AwayFromZero ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven);
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
