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
    Guid? TaxPayableAccountId);

internal sealed record CustomerInvoiceAccountingPlan(
    FinanceInvoice Invoice,
    AccountingConfiguration? Configuration,
    IAccountingPolicyPack? PolicyPack,
    CustomerInvoiceAccountingProfile? ExistingProfile,
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    decimal ExchangeRate,
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

    public CustomerInvoiceAccountingPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
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
        if (period is null || period.IsClosed || period.IsReportingLocked || issueDate < DateOnly.FromDateTime(period.StartUtc) || issueDate >= DateOnly.FromDateTime(period.EndUtc))
            Add(issues, CustomerInvoiceAccountingReasonCodes.PeriodUnavailable, "The invoice date must fall in an open accounting period.");
        var seriesCode = string.IsNullOrWhiteSpace(input.VoucherSeriesCode) ? "G" : input.VoucherSeriesCode.Trim().ToUpperInvariant();
        var seriesAvailable = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == query.CompanyId && x.Code == seriesCode && x.IsActive, cancellationToken);
        if (!seriesAvailable) Add(issues, CustomerInvoiceAccountingReasonCodes.VoucherSeriesUnavailable, "Select an active voucher series.");

        var baseCurrency = configuration?.BaseCurrency ?? invoice.Currency;
        var exchangeRate = string.Equals(invoice.Currency, baseCurrency, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : input.ExchangeRate.GetValueOrDefault();
        if (exchangeRate <= 0m)
        {
            Add(issues, CustomerInvoiceAccountingReasonCodes.CurrencyConversionMissing,
                $"Enter the {invoice.Currency}-to-{baseCurrency} exchange rate used for this invoice.");
            exchangeRate = 1m;
        }

        var receivable = FindRole(configuration, "accounts_receivable", issues);
        var revenue = FindRole(configuration, "revenue", issues);
        var documentKind = invoice.DocumentKind;
        var isCredit = string.Equals(documentKind, FinanceDocumentKinds.CreditNote, StringComparison.OrdinalIgnoreCase);
        if (pack is not null && !pack.Definition.InvoicePolicy.SupportedDocumentTypes.Contains(
                isCredit ? "credit_note" : "invoice", StringComparer.OrdinalIgnoreCase))
            Add(issues, CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "The selected accounting policy does not support this document type.");

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

                var rule = pack?.Definition.TaxRules.FirstOrDefault(x =>
                    string.Equals(x.Key, line.TaxRuleKey?.Trim(), StringComparison.OrdinalIgnoreCase) && x.EffectiveFrom <= issueDate);
                if (rule is null)
                {
                    Add(issues, CustomerInvoiceAccountingReasonCodes.TaxRuleUnsupported, $"Invoice line {sequence} uses an unavailable tax rule.");
                    continue;
                }

                string method;
                try { method = CustomerInvoiceTaxMethodValues.Normalize(rule.AmountMethod); }
                catch (ArgumentException)
                {
                    Add(issues, CustomerInvoiceAccountingReasonCodes.TaxTreatmentUnsupported, $"Tax rule {rule.DisplayName} has an unsupported amount method.");
                    continue;
                }
                var rate = method == CustomerInvoiceTaxMethodValues.Exempt ? 0m : rule.Rate.GetValueOrDefault();
                if (rate < 0m || rate > 1m || method != CustomerInvoiceTaxMethodValues.Exempt && rule.Rate is null)
                {
                    Add(issues, CustomerInvoiceAccountingReasonCodes.TaxTreatmentUnsupported, $"Tax rule {rule.DisplayName} does not define a supported rate.");
                    continue;
                }
                if (rate > 0m && string.IsNullOrWhiteSpace(rule.LiabilityAccountRoleKey))
                    Add(issues, CustomerInvoiceAccountingReasonCodes.AccountRoleMissing, $"Tax rule {rule.DisplayName} does not identify a payable-tax account role.");
                var taxAccount = rate > 0m ? FindRole(configuration, rule.LiabilityAccountRoleKey!, issues) : null;

                var amount = Round(line.Amount, configuration);
                var net = method == CustomerInvoiceTaxMethodValues.Inclusive ? Round(amount / (1m + rate), configuration) : amount;
                var tax = method switch
                {
                    CustomerInvoiceTaxMethodValues.Exclusive => Round(net * rate, configuration),
                    CustomerInvoiceTaxMethodValues.Inclusive => Round(amount - net, configuration),
                    _ => 0m
                };
                var gross = method == CustomerInvoiceTaxMethodValues.Exclusive ? Round(net + tax, configuration) : amount;
                calculated.Add(new(sequence, line.Description.Trim(), rule.Key, method, rate, net, tax, gross,
                    Round(net * exchangeRate, configuration), Round(tax * exchangeRate, configuration), taxAccount?.Id));
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
                isCredit ? 0m : grossBase, isCredit ? grossBase : 0m, baseCurrency, $"{invoice.InvoiceNumber} · {invoice.Counterparty.Name}"));
            foreach (var line in calculated)
            {
                var baseNet = line.NetBaseAmount + (line.Sequence == calculated.Last().Sequence ? rounding : 0m);
                journalLines.Add(new(revenue.Id, "revenue", revenue.Code, revenue.Name,
                    isCredit ? baseNet : 0m, isCredit ? 0m : baseNet, baseCurrency, line.Description, line.TaxRuleKey));
                if (line.TaxBaseAmount > 0m && pack is not null)
                {
                    var rule = pack.Definition.TaxRules.Single(x => string.Equals(x.Key, line.TaxRuleKey, StringComparison.OrdinalIgnoreCase));
                    var taxAccount = configuration?.AccountRoles.FirstOrDefault(x => x.FinanceAccountId == line.TaxPayableAccountId)?.FinanceAccount;
                    if (taxAccount is not null)
                        journalLines.Add(new(taxAccount.Id, rule.LiabilityAccountRoleKey!, taxAccount.Code, taxAccount.Name,
                            isCredit ? line.TaxBaseAmount : 0m, isCredit ? 0m : line.TaxBaseAmount, baseCurrency, rule.DisplayName, line.TaxRuleKey));
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
        var payloadHash = ComputeHash(invoice, input.FiscalPeriodId, seriesCode, exchangeRate, calculated,
            configuration?.PolicyPackKey, configuration?.PolicyPackVersion, existing?.OriginalInvoiceId);
        var sourceVersion = existing is null ? 1 : string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase) ? existing.Version : existing.Version + 1;

        return new(invoice, configuration, pack, existing, input.FiscalPeriodId, seriesCode, exchangeRate,
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
        plan.SourceVersion, plan.PayloadHash, plan.JournalLines, plan.Issues);

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
    private static void Add(ICollection<CustomerInvoiceAccountingIssueDto> issues, string code, string message) => issues.Add(new(code, message));
    private static CustomerInvoiceAccountingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private static string ComputeHash(FinanceInvoice invoice, Guid periodId, string seriesCode, decimal exchangeRate,
        IReadOnlyList<CustomerInvoiceAccountingLinePlan> lines, string? packKey, string? packVersion, Guid? originalInvoiceId)
    {
        var json = JsonSerializer.Serialize(new
        {
            invoice.Id, invoice.InvoiceNumber, invoice.IssuedUtc, invoice.Amount, invoice.Currency, invoice.DocumentKind,
            PeriodId = periodId, Series = seriesCode, ExchangeRate = exchangeRate,
            PackKey = packKey, PackVersion = packVersion, OriginalInvoiceId = originalInvoiceId,
            Lines = lines.Select(x => new { x.Sequence, x.Description, x.TaxRuleKey, x.TaxMethod, x.TaxRate, x.NetAmount, x.TaxAmount, x.GrossAmount })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
