using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record CustomerInvoiceAccountingLinePlan(
    int Sequence, string Description, string TaxRuleKey, string TaxMethod, decimal TaxRate,
    decimal NetAmount, decimal TaxAmount, decimal GrossAmount, decimal NetBaseAmount, decimal TaxBaseAmount,
    Guid? TaxPayableAccountId, string TaxTreatment = AccountingTaxTreatmentValues.Legacy,
    string TaxRuleVersion = "1", IReadOnlyList<string>? VatBoxMappings = null,
    string EvidenceClassification = "none", decimal InputAmount = 0m,
    string LineClassification = "unknown", string CounterpartyJurisdiction = "unknown",
    string CounterpartyVatStatus = "unknown", IReadOnlyList<AccountingTaxEvidenceInput>? SuppliedEvidence = null,
    string? LiabilityAccountRoleKey = null, string? RecoverableAccountRoleKey = null,
    string Recoverability = AccountingTaxRecoverabilityValues.Legacy);

internal sealed record CustomerInvoiceAccountingPlan(
    FinanceInvoice Invoice,
    AccountingConfiguration? Configuration,
    IAccountingPolicyPack? PolicyPack,
    CustomerInvoiceAccountingProfile? ExistingProfile,
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    decimal ExchangeRate,
    DateOnly ExchangeRateDate,
    string ExchangeRatePurpose,
    string ExchangeRateIdentity,
    IReadOnlyList<ExchangeRateLookupLeg> ExchangeRateLegs,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    decimal NetBaseAmount,
    decimal TaxBaseAmount,
    decimal GrossBaseAmount,
    decimal RoundingBaseAmount,
    Guid ReceivableAccountId,
    Guid RevenueAccountId,
    string TaxMethod,
    long SourceVersion,
    string PayloadHash,
    IReadOnlyList<CustomerInvoiceAccountingLinePlan> Lines,
    IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines,
    IReadOnlyList<ProposedAccountingEvidence> Evidence,
    IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues)
{
    public bool IsReady => Issues.All(x => !x.IsBlocking);
}

public sealed class CustomerInvoiceAccountingPolicy : ICustomerInvoiceAccountingPolicy
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAccountingTaxDecisionPolicy _taxPolicy;
    private readonly AccountingOperationsTelemetry? _telemetry;
    private readonly IExchangeRateService? _exchangeRates;

    public CustomerInvoiceAccountingPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver,
        IAccountingTaxDecisionPolicy taxPolicy, AccountingOperationsTelemetry telemetry,
        IExchangeRateService exchangeRates)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _taxPolicy = taxPolicy;
        _telemetry = telemetry;
        _exchangeRates = exchangeRates;
    }

    public CustomerInvoiceAccountingPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _taxPolicy = new AccountingTaxDecisionPolicy();
    }

    public async Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(
        PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(query, cancellationToken);
        return ToPreview(plan);
    }

    internal async Task<CustomerInvoiceAccountingPlan> BuildPlanAsync(
        PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.InvoiceId == Guid.Empty || query.ActorUserId == Guid.Empty)
            throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The customer invoice could not be found.");

        var invoice = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The customer invoice could not be found.");
        var existing = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.InvoiceId == query.InvoiceId, cancellationToken);
        var issues = new List<CustomerInvoiceAccountingIssueDto>();

        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.AccountRoles).ThenInclude(x => x.FinanceAccount)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        IAccountingPolicyPack? pack = null;
        if (configuration is null || configuration.SetupState != AccountingSetupStateValues.Ready)
            Add(issues, CustomerInvoiceAccountingReasonCodes.ConfigurationUnavailable, "Complete accounting setup before posting customer invoices.");
        else
        {
            if (configuration.Authority != AccountingAuthorityValues.InternalLedger)
                Add(issues, CustomerInvoiceAccountingReasonCodes.AuthorityUnavailable, "The internal ledger is not the accounting authority for this period.");
            if (!_packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out pack))
                Add(issues, CustomerInvoiceAccountingReasonCodes.ConfigurationUnavailable, "The selected accounting policy is not available.");
        }

        if (!string.Equals(invoice.Counterparty.CounterpartyType, "customer", StringComparison.OrdinalIgnoreCase))
            Add(issues, CustomerInvoiceAccountingReasonCodes.CounterpartyInvalid, "The invoice must belong to a customer.");
        if (!IsApproved(invoice.Status))
            Add(issues, CustomerInvoiceAccountingReasonCodes.InvoiceNotApproved, "Approve the customer invoice before preparing its accounting entry.");
        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber) || invoice.IssuedUtc == default || string.IsNullOrWhiteSpace(invoice.Currency))
            Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "The invoice number, issue date, customer, currency, and line items are required.");
        var duplicate = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == query.CompanyId && x.Id != invoice.Id && x.InvoiceNumber == invoice.InvoiceNumber, cancellationToken);
        if (duplicate) Add(issues, CustomerInvoiceAccountingReasonCodes.DuplicateDocumentNumber, "Another customer invoice uses this document number.");

        var input = query.Input ?? throw Error(CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "Accounting details are required.");
        if (input.FiscalPeriodId == Guid.Empty)
            Add(issues, CustomerInvoiceAccountingReasonCodes.PeriodUnavailable, "Select the accounting period that contains the invoice date.");
        var period = input.FiscalPeriodId == Guid.Empty ? null : await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == input.FiscalPeriodId, cancellationToken);
        var issueDate = DateOnly.FromDateTime(invoice.IssuedUtc);
        var statutoryProfile = pack?.Definition.CountryOrRegion == "SE"
            ? await _dbContext.CompanyStatutoryProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            : null;
        var vatRegistrationStatus = IsVatRegistered(statitoryProfile: statutoryProfile, issueDate)
            ? StatutoryVatRegistrationStatusValues.Registered
            : statutoryProfile?.VatRegistrationStatus ?? "unknown";
        if (period is null || period.IsClosed || period.IsReportingLocked || issueDate < DateOnly.FromDateTime(period.StartUtc) || issueDate >= DateOnly.FromDateTime(period.EndUtc))
            Add(issues, CustomerInvoiceAccountingReasonCodes.PeriodUnavailable, "The invoice date must fall in an open accounting period.");
        var seriesCode = string.IsNullOrWhiteSpace(input.VoucherSeriesCode) ? "G" : input.VoucherSeriesCode.Trim().ToUpperInvariant();
        var seriesAvailable = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == query.CompanyId && x.Code == seriesCode && x.IsActive, cancellationToken);
        if (!seriesAvailable) Add(issues, CustomerInvoiceAccountingReasonCodes.VoucherSeriesUnavailable, "Select an active voucher series.");

        var baseCurrency = configuration?.BaseCurrency ?? invoice.Currency;
        var ratePurpose = ExchangeRateLookupPurposes.TransactionDate;
        var rateDate = issueDate;
        ExchangeRateLookupResult rateLookup;
        if (string.Equals(invoice.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            rateLookup = new(ExchangeRateDecisionStatuses.Ready, ExchangeRateReasonCodes.IdentityConversion,
                "Document and functional currency are identical.", invoice.Currency, baseCurrency, rateDate,
                ratePurpose, 1m, rateDate, []);
        else if (_exchangeRates is null)
            rateLookup = new(ExchangeRateDecisionStatuses.Blocked, ExchangeRateReasonCodes.ProviderUnavailable,
                "The authoritative exchange-rate service is unavailable.", invoice.Currency, baseCurrency,
                rateDate, ratePurpose, null, null, []);
        else
            rateLookup = await _exchangeRates.LookupAsync(new(query.CompanyId, invoice.Currency, baseCurrency,
                rateDate, ratePurpose), cancellationToken);
        var exchangeRate = rateLookup.IsReady && rateLookup.EffectiveRate.HasValue
            ? rateLookup.EffectiveRate.Value : 1m;
        if (!rateLookup.IsReady || !rateLookup.EffectiveRate.HasValue)
            Add(issues, CustomerInvoiceAccountingReasonCodes.CurrencyConversionMissing, rateLookup.Explanation);
        if (input.ExchangeRate.HasValue && input.ExchangeRate.Value > 0m && input.ExchangeRate.Value != exchangeRate)
            Add(issues, CustomerInvoiceAccountingReasonCodes.CurrencyConversionMissing,
                "The supplied rate does not match the authoritative historical rate. Refresh the preview and use the retained rate evidence.");
        var rateIdentity = rateLookup.IsReady
            ? DocumentCurrencyFacts.RateIdentity(rateLookup)
            : $"blocked:{rateLookup.ReasonCode}";

        var receivable = FindRole(configuration, "accounts_receivable", issues);
        var revenue = FindRole(configuration, "revenue", issues);
        var documentKind = invoice.DocumentKind;
        var isCredit = string.Equals(documentKind, FinanceDocumentKinds.CreditNote, StringComparison.OrdinalIgnoreCase);
        if (pack is not null && !pack.Definition.InvoicePolicy.SupportedDocumentTypes.Contains(
                isCredit ? "credit_note" : "invoice", StringComparer.OrdinalIgnoreCase))
            Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "The selected accounting policy does not support this document type.");
        if (pack?.Definition.SupportedCapabilities.Contains("native_statutory_invoice_issuance", StringComparer.OrdinalIgnoreCase) == true)
        {
            var issued = await _dbContext.IssuedStatutoryDocuments.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == query.CompanyId && x.SourceRecordId == invoice.Id &&
                x.DocumentType == (isCredit ? StatutoryDocumentTypes.CustomerCredit : StatutoryDocumentTypes.CustomerInvoice), cancellationToken);
            if (!issued)
                Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing,
                    "Register or issue the immutable statutory customer document before posting it.");
        }

        var calculated = new List<CustomerInvoiceAccountingLinePlan>();
        if (input.Lines is null || input.Lines.Count == 0)
            Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "Add at least one invoice line.");
        else
        {
            var sequence = 0;
            foreach (var line in input.Lines)
            {
                sequence++;
                if (string.IsNullOrWhiteSpace(line.Description) || line.Amount <= 0m)
                {
                    Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, $"Invoice line {sequence} needs a description and a positive amount.");
                    continue;
                }

                if (pack is null)
                {
                    Add(issues, CustomerInvoiceAccountingReasonCodes.TaxRuleUnsupported, $"Invoice line {sequence} uses an unavailable tax rule.");
                    continue;
                }
                var decision = _taxPolicy.Decide(pack, new(
                    line.TaxRuleKey, issueDate, AccountingTaxDirectionValues.Sales,
                    isCredit ? "customer_credit_note" : "customer_invoice", line.LineClassification ?? "unknown", line.Amount,
                    configuration?.RoundingPrecision ?? 2, configuration?.RoundingMode ?? AccountingRoundingModeValues.MidpointToEven,
                    vatRegistrationStatus, CounterpartyJurisdiction: line.CounterpartyJurisdiction ?? "unknown",
                    CounterpartyVatStatus: line.CounterpartyVatStatus ?? "unknown",
                    CompanyCountryCode: statutoryProfile?.CountryCode ?? "unknown",
                    AccountingCurrency: statutoryProfile?.AccountingCurrency ?? configuration?.BaseCurrency ?? "unknown",
                    BookkeepingMethod: statutoryProfile?.BookkeepingMethod ?? "unknown",
                    DocumentCurrency: invoice.Currency,
                    Evidence: line.TaxEvidence));
                if (!decision.IsAllowed)
                {
                    _telemetry?.TaxDecisionBlocked(query.CompanyId, AccountingTaxDirectionValues.Sales,
                        decision.ReasonCode, pack.Definition.PackKey, pack.Definition.Version);
                    issues.Add(new(CustomerInvoiceAccountingReasonCodes.TaxRuleUnsupported,
                        $"Invoice line {sequence}: {decision.Explanation}", PolicyReasonCode: decision.ReasonCode));
                    continue;
                }
                if (decision.TaxAmount > 0m && string.IsNullOrWhiteSpace(decision.LiabilityAccountRoleKey))
                    Add(issues, CustomerInvoiceAccountingReasonCodes.AccountRoleMissing, "The selected tax rule does not identify a payable-tax account role.");
                var taxAccount = decision.TaxAmount > 0m ? FindRole(configuration, decision.LiabilityAccountRoleKey!, issues) : null;
                calculated.Add(new(sequence, line.Description.Trim(), decision.RuleKey!, decision.AmountMethod!, decision.Rate!.Value,
                    decision.TaxableBasis, decision.TaxAmount, decision.GrossAmount,
                    Round(decision.TaxableBasis * exchangeRate, configuration), Round(decision.TaxAmount * exchangeRate, configuration), taxAccount?.Id,
                    decision.Treatment!, decision.RuleVersion!, decision.VatBoxMappings, decision.EvidenceClassification,
                    line.Amount, line.LineClassification ?? "unknown", line.CounterpartyJurisdiction ?? "unknown",
                    line.CounterpartyVatStatus ?? "unknown", decision.SuppliedEvidence,
                    decision.LiabilityAccountRoleKey, decision.RecoverableAccountRoleKey, decision.Recoverability));
            }
        }

        var netAmount = calculated.Sum(x => x.NetAmount);
        var taxAmount = calculated.Sum(x => x.TaxAmount);
        var grossAmount = calculated.Sum(x => x.GrossAmount);
        var expectedGross = Round(Math.Abs(invoice.Amount), configuration);
        if (grossAmount != expectedGross)
            Add(issues, CustomerInvoiceAccountingReasonCodes.AmountMismatch,
                $"Invoice lines total {grossAmount.ToString("0.00", CultureInfo.InvariantCulture)} {invoice.Currency}, but the document total is {expectedGross.ToString("0.00", CultureInfo.InvariantCulture)} {invoice.Currency}.");

        var netBase = calculated.Sum(x => x.NetBaseAmount);
        var taxBase = calculated.Sum(x => x.TaxBaseAmount);
        var grossBase = Round(expectedGross * exchangeRate, configuration);
        var rounding = Round(grossBase - netBase - taxBase, configuration);
        var journalLines = new List<CustomerInvoiceAccountingJournalLineDto>();
        if (receivable is not null && revenue is not null && grossBase > 0m)
        {
            journalLines.Add(new(receivable.Id, "accounts_receivable", receivable.Code, receivable.Name,
                isCredit ? 0m : grossBase, isCredit ? grossBase : 0m, baseCurrency, $"{invoice.InvoiceNumber} · {invoice.Counterparty.Name}",
                DocumentDebitAmount: isCredit ? 0m : grossAmount,
                DocumentCreditAmount: isCredit ? grossAmount : 0m, DocumentCurrency: invoice.Currency));
            foreach (var line in calculated)
            {
                var baseNet = line.NetBaseAmount + (line.Sequence == calculated.Last().Sequence ? rounding : 0m);
                journalLines.Add(new(revenue.Id, "revenue", revenue.Code, revenue.Name,
                    isCredit ? baseNet : 0m, isCredit ? 0m : baseNet, baseCurrency, line.Description, line.TaxRuleKey,
                    line.TaxRuleVersion, line.VatBoxMappings, line.EvidenceClassification,
                    isCredit ? line.NetAmount : 0m, isCredit ? 0m : line.NetAmount, invoice.Currency));
                if (line.TaxBaseAmount > 0m && pack is not null)
                {
                    var taxAccount = configuration?.AccountRoles.FirstOrDefault(x => x.FinanceAccountId == line.TaxPayableAccountId)?.FinanceAccount;
                    if (taxAccount is not null)
                        journalLines.Add(new(taxAccount.Id, line.LiabilityAccountRoleKey ?? "tax_payable", taxAccount.Code, taxAccount.Name,
                            isCredit ? line.TaxBaseAmount : 0m, isCredit ? 0m : line.TaxBaseAmount, baseCurrency, "Tax", line.TaxRuleKey,
                            line.TaxRuleVersion, line.VatBoxMappings, line.EvidenceClassification,
                            isCredit ? line.TaxAmount : 0m, isCredit ? 0m : line.TaxAmount, invoice.Currency));
                }
            }
        }

        var evidence = new List<ProposedAccountingEvidence>();
        if (invoice.DocumentId.HasValue)
        {
            var document = await _dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == invoice.DocumentId.Value, cancellationToken);
            var checksum = document?.Metadata.TryGetValue("checksum_sha256", out var checksumNode) == true ? checksumNode?.ToString() : null;
            if (document is not null && !string.IsNullOrWhiteSpace(checksum))
                evidence.Add(new(document.Id, checksum, document.Title));
        }
        if (pack?.Definition.RetentionAndLockPolicy.RequiresEvidenceForPosting == true && evidence.Count == 0)
            Add(issues, CustomerInvoiceAccountingReasonCodes.EvidenceRequired, "Attach the source invoice document before posting.");

        if (existing?.Status == CustomerInvoiceAccountingStatuses.Posted)
            Add(issues, CustomerInvoiceAccountingReasonCodes.AlreadyPosted, "This invoice is already posted to the native ledger.");
        var taxMethod = calculated.Select(x => x.TaxMethod).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() is { Length: 1 } methods
            ? methods[0]
            : "mixed";
        var payloadHash = ComputeHash(invoice, input.FiscalPeriodId, seriesCode, exchangeRate, rateDate,
            ratePurpose, rateIdentity, calculated,
            configuration?.PolicyPackKey, configuration?.PolicyPackVersion, existing?.OriginalInvoiceId);
        var sourceVersion = existing is null ? 1 : string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase) ? existing.Version : existing.Version + 1;

        return new(invoice, configuration, pack, existing, input.FiscalPeriodId, seriesCode, exchangeRate,
            rateDate, ratePurpose, rateIdentity, rateLookup.Legs,
            netAmount, taxAmount, grossAmount, netBase, taxBase, grossBase, rounding, receivable?.Id ?? Guid.Empty, revenue?.Id ?? Guid.Empty, taxMethod,
            sourceVersion, payloadHash, calculated, journalLines, evidence, issues);
    }

    internal static CustomerInvoiceAccountingPreviewDto ToPreview(CustomerInvoiceAccountingPlan plan) => new(
        plan.Invoice.Id, plan.IsReady,
        plan.IsReady ? CustomerInvoiceAccountingStatuses.ReadyToPost : CustomerInvoiceAccountingStatuses.Blocked,
        plan.Invoice.DocumentKind, plan.NetAmount, plan.TaxAmount, plan.GrossAmount, plan.Invoice.Currency,
        plan.ExchangeRate, plan.NetBaseAmount, plan.TaxBaseAmount, plan.GrossBaseAmount,
        plan.RoundingBaseAmount, plan.Configuration?.BaseCurrency ?? plan.Invoice.Currency,
        plan.Configuration?.PolicyPackKey ?? string.Empty, plan.Configuration?.PolicyPackVersion ?? string.Empty,
        plan.SourceVersion, plan.PayloadHash, plan.JournalLines, plan.Issues,
        plan.ExchangeRateDate, plan.ExchangeRateIdentity, plan.ExchangeRateLegs);

    private static FinanceAccount? FindRole(AccountingConfiguration? configuration, string roleKey, ICollection<CustomerInvoiceAccountingIssueDto> issues)
    {
        var account = configuration?.AccountRoles.FirstOrDefault(x => string.Equals(x.RoleKey, roleKey, StringComparison.OrdinalIgnoreCase))?.FinanceAccount;
        if (account is null)
            Add(issues, CustomerInvoiceAccountingReasonCodes.AccountRoleMissing, $"Accounting setup is missing the {roleKey.Replace('_', ' ')} account.");
        return account;
    }

    private static decimal Round(decimal amount, AccountingConfiguration? configuration) =>
        decimal.Round(amount, configuration?.RoundingPrecision ?? 2,
            configuration?.RoundingMode == AccountingRoundingModeValues.AwayFromZero ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven);

    private static bool IsApproved(string status) => status.Trim().ToLowerInvariant() is "approved" or "paid";
    private static bool IsVatRegistered(CompanyStatutoryProfile? statitoryProfile, DateOnly accountingDate) =>
        statitoryProfile is { IsUserAttested: true, VatRegistrationStatus: StatutoryVatRegistrationStatusValues.Registered } &&
        statitoryProfile.VatRegistrationEffectiveFrom <= accountingDate &&
        (!statitoryProfile.VatRegistrationEffectiveTo.HasValue || accountingDate <= statitoryProfile.VatRegistrationEffectiveTo.Value);
    private static void Add(ICollection<CustomerInvoiceAccountingIssueDto> issues, string code, string message) => issues.Add(new(code, message));
    private static CustomerInvoiceAccountingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private static string ComputeHash(FinanceInvoice invoice, Guid periodId, string seriesCode, decimal exchangeRate,
        DateOnly exchangeRateDate, string exchangeRatePurpose, string exchangeRateIdentity,
        IReadOnlyList<CustomerInvoiceAccountingLinePlan> lines, string? packKey, string? packVersion, Guid? originalInvoiceId)
    {
        var json = JsonSerializer.Serialize(new
        {
            invoice.Id, invoice.InvoiceNumber, invoice.IssuedUtc, invoice.Amount, invoice.Currency, invoice.DocumentKind,
            PeriodId = periodId, Series = seriesCode, ExchangeRate = exchangeRate,
            ExchangeRateDate = exchangeRateDate, ExchangeRatePurpose = exchangeRatePurpose,
            ExchangeRateIdentity = exchangeRateIdentity,
            PackKey = packKey, PackVersion = packVersion, OriginalInvoiceId = originalInvoiceId,
            Lines = lines.Select(x => new { x.Sequence, x.Description, x.TaxRuleKey, x.TaxRuleVersion, x.TaxMethod,
                x.TaxTreatment, x.TaxRate, x.InputAmount, x.NetAmount, x.TaxAmount, x.GrossAmount, x.VatBoxMappings,
                x.LineClassification, x.CounterpartyJurisdiction, x.CounterpartyVatStatus, x.SuppliedEvidence })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
