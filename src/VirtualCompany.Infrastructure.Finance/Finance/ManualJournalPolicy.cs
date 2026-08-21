using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ManualJournalPolicy : IManualJournalPolicy
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;

    public ManualJournalPolicy(VirtualCompanyDbContext dbContext, IAccountingPolicyPackResolver packResolver)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
    }

    public async Task<ManualJournalPolicyDecisionDto> EvaluateAsync(Guid companyId, ManualJournalDraftInput draft, CancellationToken cancellationToken)
    {
        var issues = new List<AccountingPostingIssue>();
        var warnings = new List<AccountingPostingIssue>();
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (configuration is null)
        {
            issues.Add(new(AccountingPostingReasonCodes.ConfigurationMissing, "Accounting has not been configured for this company."));
            return new(false, true, 0m, draft.Currency, issues, warnings);
        }

        var pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion).Definition;
        if (string.IsNullOrWhiteSpace(draft.Explanation))
            issues.Add(new(ManualJournalReasonCodes.ExplanationRequired, "Explain why this manual journal is needed."));
        if (pack.RetentionAndLockPolicy.RequiresEvidenceForPosting && (draft.EvidenceDocumentIds?.Count ?? 0) == 0)
            issues.Add(new(ManualJournalReasonCodes.EvidenceRequired, "Add supporting evidence before this manual journal can be submitted."));

        var accountIds = (draft.Lines ?? []).Select(x => x.FinanceAccountId).Distinct().ToArray();
        var accounts = await _dbContext.FinanceAccounts.AsNoTracking()
            .Where(x => x.CompanyId == companyId && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var line in draft.Lines ?? [])
        {
            if (accounts.TryGetValue(line.FinanceAccountId, out var account) && account.RestrictManualPosting)
                issues.Add(new(AccountingPostingReasonCodes.ManualPostingRestricted,
                    $"Manual posting is restricted for control account {account.Code} {account.Name}.", account.Id));

            var taxCode = ReadFact(line.TaxFacts, "taxCode") ?? ReadFact(line.TaxFacts, "tax_code");
            if (taxCode is not null && !pack.TaxRules.Any(rule => string.Equals(rule.Key, taxCode, StringComparison.OrdinalIgnoreCase)))
                issues.Add(new(AccountingPostingReasonCodes.InvalidFacts, $"Tax code '{taxCode}' is not available in the active accounting policy.", line.FinanceAccountId));
        }

        var policy = await _dbContext.FinancePolicyConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var threshold = policy?.BillApprovalThreshold ?? 0m;
        var approvalCurrency = policy?.ApprovalCurrency ?? configuration.BaseCurrency;
        var debitTotal = (draft.Lines ?? []).Sum(x => x.DebitAmount);
        if (!string.Equals(draft.Currency, approvalCurrency, StringComparison.OrdinalIgnoreCase))
            warnings.Add(new(AccountingPostingReasonCodes.CurrencyMismatch,
                $"The configured approval threshold is denominated in {approvalCurrency}; this journal uses {draft.Currency}. Approval remains required."));
        else if (debitTotal >= threshold)
            warnings.Add(new(ManualJournalReasonCodes.ApprovalRequired,
                $"The journal total meets or exceeds the {threshold:0.##} {approvalCurrency} approval threshold."));
        else
            warnings.Add(new(ManualJournalReasonCodes.ApprovalRequired,
                "Manual journals require human approval before posting."));

        return new(issues.Count == 0, true, threshold, approvalCurrency, issues, warnings);
    }

    private static string? ReadFact(IReadOnlyDictionary<string, string>? facts, string key) =>
        facts?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}
