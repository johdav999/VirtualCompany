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

public sealed class SupplierBillAccountingPolicy : ISupplierBillAccountingPolicy
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAccountingTaxDecisionPolicy _taxPolicy;
    private readonly AccountingOperationsTelemetry? _telemetry;

    public SupplierBillAccountingPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver,
        IAccountingTaxDecisionPolicy taxPolicy, AccountingOperationsTelemetry telemetry)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _taxPolicy = taxPolicy;
        _telemetry = telemetry;
    }

    public SupplierBillAccountingPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _taxPolicy = new AccountingTaxDecisionPolicy();
    }

    public async Task<SupplierBillAccountingPreviewDto> PreviewAsync(
        PreviewSupplierBillAccountingQuery query, CancellationToken cancellationToken) =>
        ToPreview(await BuildPlanAsync(query, cancellationToken));

    internal async Task<SupplierBillAccountingPlan> BuildPlanAsync(
        PreviewSupplierBillAccountingQuery query, CancellationToken cancellationToken)
    {
        var issues = new List<SupplierBillAccountingIssueDto>();
        var bill = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.BillNotFound, "The supplier bill could not be found.");
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.AccountRoles).ThenInclude(x => x.FinanceAccount)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        IAccountingPolicyPack? pack = null;
        if (configuration is null || configuration.SetupState != AccountingSetupStateValues.Ready)
            Add(issues, SupplierBillAccountingReasonCodes.ConfigurationUnavailable, "Complete accounting setup before preparing this supplier bill.");
        else if (configuration.Authority != AccountingAuthorityValues.InternalLedger)
            Add(issues, SupplierBillAccountingReasonCodes.AuthorityUnavailable, "Internal-ledger authority is required for native supplier-bill posting.");
        else
            pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);

        if (!IsApproved(bill.Status))
            Add(issues, SupplierBillAccountingReasonCodes.BillNotApproved, "Approve the supplier bill facts before preparing its accounting entry.");
        if (bill.CounterpartyId == Guid.Empty || string.IsNullOrWhiteSpace(bill.Counterparty?.Name))
            Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing, "Confirm the supplier before posting.");
        if (string.IsNullOrWhiteSpace(bill.BillNumber) || bill.Amount == 0m || string.IsNullOrWhiteSpace(bill.Currency))
            Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing, "The bill number, amount, and currency are required.");

        var input = query.Input ?? throw Error(SupplierBillAccountingReasonCodes.RequiredFieldMissing, "Accounting details are required.");
        if (input.FiscalPeriodId == Guid.Empty)
            Add(issues, SupplierBillAccountingReasonCodes.PeriodUnavailable, "Select the accounting period that contains the bill date.");
        var period = input.FiscalPeriodId == Guid.Empty ? null : await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == input.FiscalPeriodId, cancellationToken);
        var billDate = DateOnly.FromDateTime(bill.ReceivedUtc);
        var statutoryProfile = pack?.Definition.CountryOrRegion == "SE"
            ? await _dbContext.CompanyStatutoryProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            : null;
        var vatRegistrationStatus = IsVatRegistered(statutoryProfile, billDate)
            ? StatutoryVatRegistrationStatusValues.Registered
            : statutoryProfile?.VatRegistrationStatus ?? "unknown";
        if (period is null || period.IsClosed || period.IsReportingLocked ||
            billDate < DateOnly.FromDateTime(period.StartUtc) || billDate >= DateOnly.FromDateTime(period.EndUtc))
            Add(issues, SupplierBillAccountingReasonCodes.PeriodUnavailable, "The bill date must fall in an open accounting period.");

        var seriesCode = string.IsNullOrWhiteSpace(input.VoucherSeriesCode) ? "G" : input.VoucherSeriesCode.Trim().ToUpperInvariant();
        var seriesAvailable = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == query.CompanyId && x.Code == seriesCode && x.IsActive, cancellationToken);
        if (!seriesAvailable) Add(issues, SupplierBillAccountingReasonCodes.VoucherSeriesUnavailable, "Select an active voucher series.");

        var baseCurrency = configuration?.BaseCurrency ?? bill.Currency;
        var exchangeRate = string.Equals(bill.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : input.ExchangeRate.GetValueOrDefault();
        if (exchangeRate <= 0m)
        {
            Add(issues, SupplierBillAccountingReasonCodes.CurrencyConversionMissing,
                $"Enter the {bill.Currency}-to-{baseCurrency} exchange rate used for this bill.");
            exchangeRate = 1m;
        }

        var payable = FindRole(configuration, "accounts_payable", issues);
        var isCredit = string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierCreditNote, StringComparison.OrdinalIgnoreCase);
        if (pack is not null && !pack.Definition.InvoicePolicy.SupportedDocumentTypes.Contains(
                isCredit ? "credit_note" : "invoice", StringComparer.OrdinalIgnoreCase))
            Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing, "The selected accounting policy does not support this document type.");
        if (pack?.Definition.SupportedCapabilities.Contains("native_statutory_invoice_issuance", StringComparer.OrdinalIgnoreCase) == true)
        {
            var issued = await _dbContext.IssuedStatutoryDocuments.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == query.CompanyId && x.SourceRecordId == bill.Id &&
                x.DocumentType == (isCredit ? StatutoryDocumentTypes.SupplierCredit : StatutoryDocumentTypes.SupplierInvoice), cancellationToken);
            if (!issued)
                Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing,
                    "Register the immutable supplier document before posting it.");
        }

        var requestedAccountIds = (input.Lines ?? []).Select(x => x.CostAccountId).Where(x => x != Guid.Empty).Distinct().ToArray();
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && requestedAccountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var calculated = new List<SupplierBillAccountingLinePlan>();
        if (input.Lines is null || input.Lines.Count == 0)
            Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing, "Add at least one bill line.");
        else
        {
            var sequence = 0;
            foreach (var line in input.Lines)
            {
                sequence++;
                if (string.IsNullOrWhiteSpace(line.Description) || line.Amount <= 0m)
                {
                    Add(issues, SupplierBillAccountingReasonCodes.RequiredFieldMissing, $"Bill line {sequence} needs a description and a positive amount.");
                    continue;
                }

                if (line.CostAccountId == Guid.Empty || !accounts.TryGetValue(line.CostAccountId, out var costAccount))
                {
                    Add(issues, SupplierBillAccountingReasonCodes.CostAccountMissing,
                        $"Bill line {sequence} needs an explicitly selected expense or asset account.");
                    continue;
                }
                if (costAccount.AccountClass is not (FinanceAccountClassValues.Expense or FinanceAccountClassValues.Asset) ||
                    !costAccount.IsPostingEnabled || costAccount.EffectiveFrom > billDate || costAccount.EffectiveTo < billDate ||
                    costAccount.RestrictManualPosting)
                {
                    Add(issues, SupplierBillAccountingReasonCodes.CostAccountInvalid,
                        $"{costAccount.Name} is not an active posting-enabled expense or asset account for the bill date.");
                    continue;
                }

                if (pack is null)
                {
                    Add(issues, SupplierBillAccountingReasonCodes.TaxRuleUnsupported, $"Bill line {sequence} uses an unavailable tax rule.");
                    continue;
                }
                var decision = _taxPolicy.Decide(pack, new(
                    line.TaxRuleKey, billDate, AccountingTaxDirectionValues.Purchase,
                    isCredit ? "supplier_credit_note" : "supplier_invoice",
                    line.LineClassification ?? (pack.Definition.CountryOrRegion == "SE" ? "unknown" : costAccount.AccountClass!), line.Amount,
                    configuration?.RoundingPrecision ?? 2, configuration?.RoundingMode ?? AccountingRoundingModeValues.MidpointToEven,
                    vatRegistrationStatus, CounterpartyJurisdiction: line.CounterpartyJurisdiction ?? "unknown",
                    CounterpartyVatStatus: line.CounterpartyVatStatus ?? "unknown",
                    CompanyCountryCode: statutoryProfile?.CountryCode ?? "unknown",
                    AccountingCurrency: statutoryProfile?.AccountingCurrency ?? configuration?.BaseCurrency ?? "unknown",
                    BookkeepingMethod: statutoryProfile?.BookkeepingMethod ?? "unknown",
                    DocumentCurrency: bill.Currency,
                    Evidence: line.TaxEvidence));
                if (!decision.IsAllowed)
                {
                    _telemetry?.TaxDecisionBlocked(query.CompanyId, AccountingTaxDirectionValues.Purchase,
                        decision.ReasonCode, pack.Definition.PackKey, pack.Definition.Version);
                    issues.Add(new(SupplierBillAccountingReasonCodes.TaxRuleUnsupported,
                        $"Bill line {sequence}: {decision.Explanation}", PolicyReasonCode: decision.ReasonCode));
                    continue;
                }

                FinanceAccount? recoverableTaxAccount = null;
                var treatment = SupplierBillTaxTreatmentValues.Exempt;
                if (decision.TaxAmount > 0m && !string.IsNullOrWhiteSpace(decision.RecoverableAccountRoleKey))
                {
                    treatment = SupplierBillTaxTreatmentValues.Recoverable;
                    recoverableTaxAccount = FindRole(configuration, decision.RecoverableAccountRoleKey!, issues);
                }
                else if (decision.TaxAmount > 0m)
                {
                    treatment = SupplierBillTaxTreatmentValues.NonRecoverable;
                }

                var net = decision.TaxableBasis;
                var tax = decision.TaxAmount;
                var gross = decision.GrossAmount;
                var recoverable = treatment == SupplierBillTaxTreatmentValues.Recoverable ? tax : 0m;
                var nonRecoverable = treatment == SupplierBillTaxTreatmentValues.NonRecoverable ? tax : 0m;
                calculated.Add(new(sequence, line.Description.Trim(), costAccount.Id, costAccount.Code, costAccount.Name,
                    costAccount.AccountClass!, decision.RuleKey!, decision.AmountMethod!, treatment, decision.Rate!.Value, net, tax, recoverable,
                    nonRecoverable, gross, Round((net + nonRecoverable) * exchangeRate, configuration),
                    Round(recoverable * exchangeRate, configuration), recoverableTaxAccount?.Id,
                    decision.RuleVersion!, decision.VatBoxMappings, decision.EvidenceClassification,
                    line.Amount, line.LineClassification ?? (pack.Definition.CountryOrRegion == "SE" ? "unknown" : costAccount.AccountClass!),
                    line.CounterpartyJurisdiction ?? "unknown", line.CounterpartyVatStatus ?? "unknown", decision.SuppliedEvidence,
                    decision.LiabilityAccountRoleKey, decision.RecoverableAccountRoleKey, decision.Recoverability));
            }
        }

        var netAmount = calculated.Sum(x => x.NetAmount);
        var recoverableTaxAmount = calculated.Sum(x => x.RecoverableTaxAmount);
        var nonRecoverableTaxAmount = calculated.Sum(x => x.NonRecoverableTaxAmount);
        var grossAmount = calculated.Sum(x => x.GrossAmount);
        var expectedGross = Round(Math.Abs(bill.Amount), configuration);
        if (grossAmount != expectedGross)
            Add(issues, SupplierBillAccountingReasonCodes.AmountMismatch,
                $"Bill lines total {grossAmount.ToString("0.00", CultureInfo.InvariantCulture)} {bill.Currency}, but the document total is {expectedGross.ToString("0.00", CultureInfo.InvariantCulture)} {bill.Currency}.");

        var costBase = calculated.Sum(x => x.CostBaseAmount);
        var recoverableTaxBase = calculated.Sum(x => x.RecoverableTaxBaseAmount);
        var grossBase = Round(expectedGross * exchangeRate, configuration);
        var rounding = Round(grossBase - costBase - recoverableTaxBase, configuration);
        var journalLines = new List<SupplierBillAccountingJournalLineDto>();
        if (payable is not null && calculated.Count > 0 && grossBase > 0m)
        {
            journalLines.Add(new(payable.Id, "accounts_payable", payable.Code, payable.Name,
                isCredit ? grossBase : 0m, isCredit ? 0m : grossBase, baseCurrency,
                $"{bill.BillNumber} · {bill.Counterparty?.Name ?? "Supplier"}"));
            foreach (var line in calculated)
            {
                var cost = line.CostBaseAmount + (line.Sequence == calculated[^1].Sequence ? rounding : 0m);
                journalLines.Add(new(line.CostAccountId, line.AccountClassification, line.CostAccountCode,
                    line.CostAccountName, isCredit ? 0m : cost, isCredit ? cost : 0m, baseCurrency,
                    line.Description, line.TaxRuleKey, line.TaxTreatment, line.TaxRuleVersion,
                    line.VatBoxMappings, line.EvidenceClassification));
                if (line.RecoverableTaxBaseAmount > 0m && line.RecoverableTaxAccountId.HasValue)
                {
                    var taxAccount = configuration?.AccountRoles.FirstOrDefault(x =>
                        x.FinanceAccountId == line.RecoverableTaxAccountId.Value)?.FinanceAccount;
                    if (taxAccount is not null)
                        journalLines.Add(new(taxAccount.Id, line.RecoverableAccountRoleKey ?? "tax_recoverable", taxAccount.Code, taxAccount.Name,
                            isCredit ? 0m : line.RecoverableTaxBaseAmount,
                            isCredit ? line.RecoverableTaxBaseAmount : 0m, baseCurrency,
                            $"Tax · {line.Description}", line.TaxRuleKey, line.TaxTreatment,
                            line.TaxRuleVersion, line.VatBoxMappings, line.EvidenceClassification));
                }
            }
        }

        var duplicates = await FindDuplicatesAsync(bill, cancellationToken);
        if (duplicates.Count > 0)
            Add(issues, SupplierBillAccountingReasonCodes.DuplicateBill,
                "A matching supplier, bill number, amount, currency, and bill date already exists. Review the duplicate evidence before posting.");

        var (documentId, documentHash, documentTitle) = await ResolveDocumentEvidenceAsync(bill, cancellationToken);
        var evidence = new List<ProposedAccountingEvidence>();
        if (documentId.HasValue && !string.IsNullOrWhiteSpace(documentHash))
            evidence.Add(new(documentId.Value, documentHash, documentTitle));
        if (pack?.Definition.RetentionAndLockPolicy.RequiresEvidenceForPosting == true && evidence.Count == 0)
            Add(issues, SupplierBillAccountingReasonCodes.EvidenceRequired, "Attach the source bill document before posting.");

        var existing = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.BillId == query.BillId, cancellationToken);
        if (existing?.Status == SupplierBillAccountingStatuses.Posted)
            Add(issues, SupplierBillAccountingReasonCodes.AlreadyPosted, "This supplier bill is already posted in Virtual Company.");
        var taxTreatment = calculated.Select(x => x.TaxTreatment).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() is { Length: 1 } treatments
            ? treatments[0]
            : "mixed";
        var payloadHash = ComputeHash(bill, input.FiscalPeriodId, seriesCode, exchangeRate, calculated,
            configuration?.PolicyPackKey, configuration?.PolicyPackVersion, documentHash, existing?.OriginalBillId);
        var sourceVersion = existing is null ? 1 : string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase)
            ? existing.Version
            : existing.Version + 1;

        return new(bill, configuration, pack, existing, input.FiscalPeriodId, seriesCode, exchangeRate,
            netAmount, recoverableTaxAmount, nonRecoverableTaxAmount, grossAmount, costBase,
            recoverableTaxBase, grossBase, rounding, payable?.Id ?? Guid.Empty, taxTreatment,
            sourceVersion, payloadHash, documentHash, calculated, journalLines, duplicates, evidence, issues);
    }

    internal static SupplierBillAccountingPreviewDto ToPreview(SupplierBillAccountingPlan plan) => new(
        plan.Bill.Id, plan.IsReady,
        plan.IsReady ? SupplierBillAccountingStatuses.ReadyToPost : SupplierBillAccountingStatuses.Blocked,
        plan.Bill.DocumentKind, plan.NetAmount, plan.RecoverableTaxAmount, plan.NonRecoverableTaxAmount,
        plan.GrossAmount, plan.Bill.Currency, plan.ExchangeRate, plan.CostBaseAmount,
        plan.RecoverableTaxBaseAmount, plan.GrossBaseAmount, plan.RoundingBaseAmount,
        plan.Configuration?.BaseCurrency ?? plan.Bill.Currency,
        plan.Configuration?.PolicyPackKey ?? string.Empty, plan.Configuration?.PolicyPackVersion ?? string.Empty,
        plan.SourceVersion, plan.PayloadHash, plan.SourceDocumentHash, plan.JournalLines,
        plan.DuplicateEvidence, plan.Issues);

    private async Task<IReadOnlyList<SupplierBillDuplicateEvidenceDto>> FindDuplicatesAsync(
        FinanceBill bill, CancellationToken cancellationToken)
    {
        var normalizedNumber = bill.BillNumber.Trim();
        var billDate = bill.ReceivedUtc.Date;
        var authoritativeDuplicates = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.Id != bill.Id && x.CounterpartyId == bill.CounterpartyId &&
                x.BillNumber == normalizedNumber && x.Amount == bill.Amount && x.Currency == bill.Currency &&
                x.ReceivedUtc >= billDate && x.ReceivedUtc < billDate.AddDays(1))
            .Select(x => new SupplierBillDuplicateEvidenceDto(x.Id, x.BillNumber, x.Counterparty.Name,
                DateOnly.FromDateTime(x.ReceivedUtc), x.Amount, x.Currency,
                new[] { "Supplier", "Bill number", "Amount", "Currency", "Bill date" }))
            .ToListAsync(cancellationToken);
        var intakeChecks = await _dbContext.BillDuplicateChecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.IsDuplicate &&
                x.InvoiceNumber == normalizedNumber && x.TotalAmount == bill.Amount && x.Currency == bill.Currency)
            .OrderByDescending(x => x.CheckedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);
        authoritativeDuplicates.AddRange(intakeChecks.Select(check => new SupplierBillDuplicateEvidenceDto(
            check.GetMatchedBillIds().FirstOrDefault() is var matchedId && matchedId != Guid.Empty ? matchedId : check.Id,
            check.InvoiceNumber ?? bill.BillNumber,
            check.SupplierName ?? bill.Counterparty?.Name ?? "Supplier",
            DateOnly.FromDateTime(bill.ReceivedUtc),
            check.TotalAmount ?? bill.Amount,
            check.Currency ?? bill.Currency,
            ["Persisted intake duplicate check", check.CriteriaSummary])));
        return authoritativeDuplicates
            .GroupBy(x => new { x.MatchedBillId, x.BillNumber })
            .Select(x => x.First())
            .ToArray();
    }

    private async Task<(Guid? DocumentId, string? Hash, string Title)> ResolveDocumentEvidenceAsync(
        FinanceBill bill, CancellationToken cancellationToken)
    {
        var documentId = bill.DocumentId ?? await _dbContext.SupplierInvoiceSourceDocumentAttachments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.BillId == bill.Id && x.DocumentId != null)
            .OrderByDescending(x => x.AttachedUtc).Select(x => x.DocumentId).FirstOrDefaultAsync(cancellationToken);
        if (!documentId.HasValue) return (null, null, $"Supplier bill {bill.BillNumber}");
        var document = await _dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == bill.CompanyId && x.Id == documentId.Value, cancellationToken);
        var hash = document?.Metadata.TryGetValue("checksum_sha256", out var node) == true ? node?.ToString() : null;
        return (document?.Id, hash, document?.Title ?? $"Supplier bill {bill.BillNumber}");
    }

    private static FinanceAccount? FindRole(
        AccountingConfiguration? configuration, string roleKey, ICollection<SupplierBillAccountingIssueDto> issues)
    {
        var account = configuration?.AccountRoles.FirstOrDefault(x =>
            string.Equals(x.RoleKey, roleKey, StringComparison.OrdinalIgnoreCase))?.FinanceAccount;
        if (account is null)
            Add(issues, SupplierBillAccountingReasonCodes.AccountRoleMissing,
                $"Accounting setup is missing the {roleKey.Replace('_', ' ')} account.");
        return account;
    }

    private static decimal Round(decimal amount, AccountingConfiguration? configuration) =>
        decimal.Round(amount, configuration?.RoundingPrecision ?? 2,
            configuration?.RoundingMode == AccountingRoundingModeValues.AwayFromZero
                ? MidpointRounding.AwayFromZero
                : MidpointRounding.ToEven);

    private static bool IsApproved(string status) => status.Trim().ToLowerInvariant() is "approved" or "paid" or "booked";
    private static bool IsVatRegistered(CompanyStatutoryProfile? profile, DateOnly accountingDate) =>
        profile is { IsUserAttested: true, VatRegistrationStatus: StatutoryVatRegistrationStatusValues.Registered } &&
        profile.VatRegistrationEffectiveFrom <= accountingDate &&
        (!profile.VatRegistrationEffectiveTo.HasValue || accountingDate <= profile.VatRegistrationEffectiveTo.Value);
    private static void Add(ICollection<SupplierBillAccountingIssueDto> issues, string code, string message) => issues.Add(new(code, message));
    private static SupplierBillAccountingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private static string ComputeHash(
        FinanceBill bill, Guid periodId, string seriesCode, decimal exchangeRate,
        IReadOnlyList<SupplierBillAccountingLinePlan> lines, string? packKey, string? packVersion,
        string? documentHash, Guid? originalBillId)
    {
        var json = JsonSerializer.Serialize(new
        {
            bill.Id, bill.CounterpartyId, bill.BillNumber, bill.ReceivedUtc, bill.DueUtc, bill.Amount,
            bill.Currency, bill.Status, bill.DocumentKind, PeriodId = periodId, Series = seriesCode,
            ExchangeRate = exchangeRate, PackKey = packKey, PackVersion = packVersion,
            SourceDocumentHash = documentHash, OriginalBillId = originalBillId,
            Lines = lines.Select(x => new
            {
                x.Sequence, x.Description, x.CostAccountId, x.AccountClassification, x.TaxRuleKey,
                x.TaxRuleVersion, x.TaxMethod, x.TaxTreatment, x.TaxRate, x.InputAmount, x.NetAmount, x.TaxAmount,
                x.GrossAmount, x.VatBoxMappings, x.LineClassification, x.CounterpartyJurisdiction,
                x.CounterpartyVatStatus, x.SuppliedEvidence
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

internal sealed record SupplierBillAccountingLinePlan(
    int Sequence, string Description, Guid CostAccountId, string CostAccountCode, string CostAccountName,
    string AccountClassification, string TaxRuleKey, string TaxMethod, string TaxTreatment,
    decimal TaxRate, decimal NetAmount, decimal TaxAmount, decimal RecoverableTaxAmount,
    decimal NonRecoverableTaxAmount, decimal GrossAmount, decimal CostBaseAmount,
    decimal RecoverableTaxBaseAmount, Guid? RecoverableTaxAccountId,
    string TaxRuleVersion = "1", IReadOnlyList<string>? VatBoxMappings = null,
    string EvidenceClassification = "none", decimal InputAmount = 0m,
    string LineClassification = "unknown", string CounterpartyJurisdiction = "unknown",
    string CounterpartyVatStatus = "unknown", IReadOnlyList<AccountingTaxEvidenceInput>? SuppliedEvidence = null,
    string? LiabilityAccountRoleKey = null, string? RecoverableAccountRoleKey = null,
    string Recoverability = AccountingTaxRecoverabilityValues.Legacy);

internal sealed record SupplierBillAccountingPlan(
    FinanceBill Bill,
    AccountingConfiguration? Configuration,
    IAccountingPolicyPack? PolicyPack,
    SupplierBillAccountingProfile? ExistingProfile,
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    decimal ExchangeRate,
    decimal NetAmount,
    decimal RecoverableTaxAmount,
    decimal NonRecoverableTaxAmount,
    decimal GrossAmount,
    decimal CostBaseAmount,
    decimal RecoverableTaxBaseAmount,
    decimal GrossBaseAmount,
    decimal RoundingBaseAmount,
    Guid PayableAccountId,
    string TaxTreatment,
    long SourceVersion,
    string PayloadHash,
    string? SourceDocumentHash,
    IReadOnlyList<SupplierBillAccountingLinePlan> Lines,
    IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines,
    IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence,
    IReadOnlyList<ProposedAccountingEvidence> Evidence,
    IReadOnlyList<SupplierBillAccountingIssueDto> Issues)
{
    public bool IsReady => Issues.All(x => !x.IsBlocking);
}
