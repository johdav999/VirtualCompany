using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingTaxDecisionPolicy : IAccountingTaxDecisionPolicy
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public AccountingTaxDecision Decide(IAccountingPolicyPack pack, AccountingTaxDecisionInput input)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(input);

        var candidates = pack.Definition.TaxRules.Where(rule =>
            Comparer.Equals(rule.Key, input.RequestedRuleKey?.Trim()) &&
            rule.EffectiveFrom <= input.AccountingDate &&
            (!rule.EffectiveTo.HasValue || input.AccountingDate <= rule.EffectiveTo.Value) &&
            Applies(rule.Direction, input.Direction) &&
            ContainsOrUnrestricted(rule.DocumentTypes, input.DocumentType) &&
            ContainsOrUnrestricted(rule.LineClassifications, input.LineClassification) &&
            ContainsOrUnrestricted(rule.CounterpartyJurisdictions, input.CounterpartyJurisdiction) &&
            ContainsOrUnrestricted(rule.CounterpartyVatStatuses, input.CounterpartyVatStatus))
            .OrderByDescending(rule => rule.EffectiveFrom)
            .ToArray();

        if (candidates.Length == 0)
            return Block(AccountingTaxDecisionReasonCodes.RuleUnavailable,
                pack.Definition.CountryOrRegion == "SE"
                    ? "No approved Swedish VAT rule covers this transaction. Select a supported, evidence-backed case after an approved VAT specification has been installed."
                    : "The selected tax rule is not available for the accounting date and transaction facts.");
        if (candidates.Length > 1 && candidates[0].EffectiveFrom == candidates[1].EffectiveFrom)
            return Block(AccountingTaxDecisionReasonCodes.AmbiguousRule,
                "More than one tax rule matches the same transaction facts and effective date. Posting is blocked until the policy pack is corrected.");

        var rule = candidates[0];
        var validationIssue = ValidateRule(rule);
        if (validationIssue is not null)
            return Block(AccountingTaxDecisionReasonCodes.InvalidRule, validationIssue.Explanation);

        var suppliedEvidence = NormalizeEvidence(input);
        if (pack.Definition.CountryOrRegion == "SE")
        {
            var scopeBlock = ValidateSwedishScope(input, rule, suppliedEvidence);
            if (scopeBlock is not null) return scopeBlock;
        }

        var requiredEvidence = rule.RequiredEvidence ?? [];
        var supplied = suppliedEvidence.Select(item => item.Classification).ToHashSet(Comparer);
        var missing = requiredEvidence.Where(item => !supplied.Contains(item)).ToArray();
        if (missing.Length > 0)
            return new(false, AccountingTaxDecisionReasonCodes.EvidenceMissing,
                "Required tax evidence is missing: " + string.Join(", ", missing.Select(Plain)) + ".",
                missing, rule.Key, rule.RuleVersion, 0m, 0m, 0m, rule.Rate, rule.AmountMethod,
                rule.Treatment, rule.LiabilityAccountRoleKey, rule.RecoverableAccountRoleKey,
                rule.Recoverability, rule.VatBoxMappings ?? [], Evidence(supplied), suppliedEvidence);

        var amount = Round(input.LineAmount, input.RoundingPrecision, input.RoundingMode);
        var rate = rule.AmountMethod == CustomerInvoiceTaxMethodValues.Exempt ? 0m : rule.Rate!.Value;
        var basis = rule.AmountMethod == CustomerInvoiceTaxMethodValues.Inclusive
            ? Round(amount / (1m + rate), input.RoundingPrecision, input.RoundingMode)
            : amount;
        var tax = rule.AmountMethod switch
        {
            CustomerInvoiceTaxMethodValues.Exclusive => Round(basis * rate, input.RoundingPrecision, input.RoundingMode),
            CustomerInvoiceTaxMethodValues.Inclusive => Round(amount - basis, input.RoundingPrecision, input.RoundingMode),
            _ => 0m
        };
        var gross = rule.AmountMethod == CustomerInvoiceTaxMethodValues.Exclusive
            ? Round(basis + tax, input.RoundingPrecision, input.RoundingMode)
            : amount;

        return new(true, AccountingTaxDecisionReasonCodes.Allowed, $"Applied {rule.DisplayName}.",
            requiredEvidence, rule.Key, rule.RuleVersion, basis, tax, gross, rate, rule.AmountMethod,
            rule.Treatment, rule.LiabilityAccountRoleKey, rule.RecoverableAccountRoleKey,
            rule.Recoverability, rule.VatBoxMappings ?? [], Evidence(supplied), suppliedEvidence);
    }

    public IReadOnlyList<AccountingConfigurationIssueDto> Validate(IAccountingPolicyPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var issues = new List<AccountingConfigurationIssueDto>();
        var roles = pack.Definition.AccountRoles.Select(x => x.Key).ToHashSet(Comparer);
        var chartRoles = pack.Definition.ChartTemplates.SelectMany(chart => chart.Accounts)
            .Where(account => !string.IsNullOrWhiteSpace(account.DefaultRoleKey))
            .Select(account => account.DefaultRoleKey!).ToHashSet(Comparer);
        foreach (var rule in pack.Definition.TaxRules)
        {
            var issue = ValidateRule(rule);
            if (issue is not null) issues.Add(issue);
            foreach (var role in new[] { rule.LiabilityAccountRoleKey, rule.RecoverableAccountRoleKey }.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!roles.Contains(role!))
                    issues.Add(new(AccountingTaxDecisionReasonCodes.InvalidRule,
                        $"Tax rule {rule.Key} references undefined account role {role}.", rule.Key));
                else if (pack.Definition.ChartTemplates.Count > 0 && !chartRoles.Contains(role!))
                    issues.Add(new(AccountingTaxDecisionReasonCodes.InvalidRule,
                        $"Tax rule {rule.Key} references account role {role}, but no chart account supplies that role.", rule.Key));
            }

            var postingIssue = ValidatePostingShape(rule);
            if (postingIssue is not null) issues.Add(postingIssue);
        }

        foreach (var group in pack.Definition.TaxRules.GroupBy(x => x.Key, Comparer))
        {
            foreach (var duplicate in group.GroupBy(x => x.RuleVersion, Comparer).Where(version => version.Count() > 1))
                issues.Add(new(AccountingTaxDecisionReasonCodes.InvalidRule,
                    $"Tax rule {group.Key} contains duplicate rule version {duplicate.Key}.", group.Key));

            var ordered = group.OrderBy(x => x.EffectiveFrom).ToArray();
            for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
                {
                    var left = ordered[leftIndex];
                    var right = ordered[rightIndex];
                    if (EffectivePeriodsOverlap(left, right) && ApplicabilityOverlaps(left, right))
                        issues.Add(new(AccountingTaxDecisionReasonCodes.InvalidRule,
                            $"Tax rule {right.Key} has overlapping transaction applicability.", right.Key));
                }
            }

            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                if (!SameApplicability(previous, current) || EffectivePeriodsOverlap(previous, current)) continue;
                if (previous.EffectiveTo!.Value.AddDays(1) < current.EffectiveFrom)
                    issues.Add(new(AccountingTaxDecisionReasonCodes.InvalidRule,
                        $"Tax rule {current.Key} has an effective-date gap.", current.Key));
            }
        }
        return issues;
    }

    private static AccountingConfigurationIssueDto? ValidateRule(AccountingTaxRuleDefinition rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Key) || string.IsNullOrWhiteSpace(rule.RuleVersion))
            return Invalid(rule, "Tax rule key and version are required.");
        if (rule.EffectiveTo < rule.EffectiveFrom)
            return Invalid(rule, $"Tax rule {rule.Key} ends before it starts.");
        if (rule.Rate is < 0m or > 1m || rule.AmountMethod != CustomerInvoiceTaxMethodValues.Exempt && rule.Rate is null)
            return Invalid(rule, $"Tax rule {rule.Key} has an invalid rate.");
        try { CustomerInvoiceTaxMethodValues.Normalize(rule.AmountMethod); }
        catch (ArgumentException) { return Invalid(rule, $"Tax rule {rule.Key} has an unsupported basis amount method."); }
        if (rule.TaxableBasisMethod != AccountingTaxableBasisMethodValues.LineAmount)
            return Invalid(rule, $"Tax rule {rule.Key} has an unsupported taxable-basis method.");
        if (rule.Recoverability == AccountingTaxRecoverabilityValues.Partial)
            return Invalid(rule, $"Tax rule {rule.Key} requests partial recovery, which is not supported by this policy version.");
        return null;
    }

    private static AccountingConfigurationIssueDto? ValidatePostingShape(AccountingTaxRuleDefinition rule)
    {
        if (rule.Rate.GetValueOrDefault() <= 0m) return null;
        if (Comparer.Equals(rule.Direction, AccountingTaxDirectionValues.Sales) &&
            string.IsNullOrWhiteSpace(rule.LiabilityAccountRoleKey))
            return Invalid(rule, $"Tax rule {rule.Key} cannot produce a balanced sales VAT posting without an output-tax account role.");
        if (Comparer.Equals(rule.Direction, AccountingTaxDirectionValues.Purchase) &&
            Comparer.Equals(rule.Recoverability, AccountingTaxRecoverabilityValues.Full) &&
            string.IsNullOrWhiteSpace(rule.RecoverableAccountRoleKey))
            return Invalid(rule, $"Tax rule {rule.Key} cannot produce a balanced recoverable purchase VAT posting without an input-tax account role.");
        if (Comparer.Equals(rule.Direction, AccountingTaxDirectionValues.Sales) &&
            !string.IsNullOrWhiteSpace(rule.RecoverableAccountRoleKey))
            return Invalid(rule, $"Tax rule {rule.Key} assigns an input-tax account to a sales posting.");
        if (Comparer.Equals(rule.Direction, AccountingTaxDirectionValues.Purchase) &&
            !string.IsNullOrWhiteSpace(rule.LiabilityAccountRoleKey))
            return Invalid(rule, $"Tax rule {rule.Key} assigns an output-tax account to an ordinary purchase posting.");
        return null;
    }

    private static AccountingTaxDecision? ValidateSwedishScope(
        AccountingTaxDecisionInput input,
        AccountingTaxRuleDefinition rule,
        IReadOnlyList<AccountingTaxEvidenceInput> suppliedEvidence)
    {
        AccountingTaxDecision BlockRule(string code, string explanation) => new(false, code, explanation,
            rule.RequiredEvidence ?? [], rule.Key, rule.RuleVersion, 0m, 0m, 0m, rule.Rate, rule.AmountMethod,
            rule.Treatment, rule.LiabilityAccountRoleKey, rule.RecoverableAccountRoleKey, rule.Recoverability,
            rule.VatBoxMappings ?? [], Evidence(suppliedEvidence.Select(item => item.Classification)), suppliedEvidence);

        if (!Comparer.Equals(input.CompanyCountryCode, "SE"))
            return BlockRule(AccountingTaxDecisionReasonCodes.CompanyJurisdictionUnsupported,
                "The Swedish VAT launch policy requires a company established in Sweden.");
        if (!Comparer.Equals(input.AccountingCurrency, "SEK"))
            return BlockRule(AccountingTaxDecisionReasonCodes.AccountingCurrencyUnsupported,
                "The Swedish VAT launch policy requires SEK accounting.");
        if (!Comparer.Equals(input.BookkeepingMethod, StatutoryBookkeepingMethodValues.Accrual))
            return BlockRule(AccountingTaxDecisionReasonCodes.BookkeepingMethodUnsupported,
                "The Swedish VAT launch policy supports only the invoice/accrual bookkeeping method.");
        if (!Comparer.Equals(input.DocumentCurrency, "SEK"))
            return BlockRule(AccountingTaxDecisionReasonCodes.DocumentCurrencyUnsupported,
                "Foreign-currency VAT calculation is not supported by this Swedish policy version.");
        if (!Comparer.Equals(input.CompanyVatRegistrationStatus, StatutoryVatRegistrationStatusValues.Registered) && rule.Rate > 0m)
            return BlockRule(AccountingTaxDecisionReasonCodes.RegistrationRequired,
                "A current, user-attested VAT registration state is required for this Swedish VAT treatment.");
        return null;
    }

    private static IReadOnlyList<AccountingTaxEvidenceInput> NormalizeEvidence(AccountingTaxDecisionInput input)
    {
        var items = new List<AccountingTaxEvidenceInput>();
        if (input.Evidence is not null)
            items.AddRange(input.Evidence.Where(item => !string.IsNullOrWhiteSpace(item.Classification))
                .Select(item => new AccountingTaxEvidenceInput(item.Classification.Trim().ToLowerInvariant(),
                    string.IsNullOrWhiteSpace(item.SourceReference) ? null : item.SourceReference.Trim())));
        if (input.EvidenceClassifications is not null)
            items.AddRange(input.EvidenceClassifications.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new AccountingTaxEvidenceInput(value.Trim().ToLowerInvariant())));
        return items.GroupBy(item => item.Classification, Comparer)
            .Select(group => group.First()).OrderBy(item => item.Classification, Comparer).ToArray();
    }

    private static AccountingConfigurationIssueDto Invalid(AccountingTaxRuleDefinition rule, string explanation) =>
        new(AccountingTaxDecisionReasonCodes.InvalidRule, explanation, rule.Key);
    private static AccountingTaxDecision Block(string code, string explanation) =>
        new(false, code, explanation, [], null, null, 0m, 0m, 0m, null, null, null, null, null,
            AccountingTaxRecoverabilityValues.Legacy, [], "none", []);
    private static bool Applies(string configured, string actual) => Comparer.Equals(configured, AccountingTaxDirectionValues.Both) || Comparer.Equals(configured, actual);
    private static bool ContainsOrUnrestricted(IReadOnlyList<string>? configured, string actual) => configured is null || configured.Count == 0 || configured.Contains(actual, Comparer);
    private static string Plain(string value) => value.Replace('_', ' ');
    private static string Evidence(IEnumerable<string> values)
    {
        var result = string.Join(",", values.OrderBy(x => x, Comparer));
        return string.IsNullOrEmpty(result) ? "none" : result;
    }
    private static decimal Round(decimal value, int precision, string mode) => decimal.Round(value, precision,
        mode == AccountingRoundingModeValues.AwayFromZero ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven);
    private static bool SameApplicability(AccountingTaxRuleDefinition left, AccountingTaxRuleDefinition right) =>
        Comparer.Equals(left.Direction, right.Direction) && SequenceEqual(left.DocumentTypes, right.DocumentTypes) &&
        SequenceEqual(left.LineClassifications, right.LineClassifications) && SequenceEqual(left.CounterpartyJurisdictions, right.CounterpartyJurisdictions) &&
        SequenceEqual(left.CounterpartyVatStatuses, right.CounterpartyVatStatuses);
    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        (left ?? []).OrderBy(x => x, Comparer).SequenceEqual((right ?? []).OrderBy(x => x, Comparer), Comparer);
    private static bool EffectivePeriodsOverlap(AccountingTaxRuleDefinition left, AccountingTaxRuleDefinition right) =>
        left.EffectiveFrom <= (right.EffectiveTo ?? DateOnly.MaxValue) &&
        right.EffectiveFrom <= (left.EffectiveTo ?? DateOnly.MaxValue);
    private static bool ApplicabilityOverlaps(AccountingTaxRuleDefinition left, AccountingTaxRuleDefinition right) =>
        DirectionsOverlap(left.Direction, right.Direction) && ListsOverlap(left.DocumentTypes, right.DocumentTypes) &&
        ListsOverlap(left.LineClassifications, right.LineClassifications) &&
        ListsOverlap(left.CounterpartyJurisdictions, right.CounterpartyJurisdictions) &&
        ListsOverlap(left.CounterpartyVatStatuses, right.CounterpartyVatStatuses);
    private static bool DirectionsOverlap(string left, string right) =>
        Comparer.Equals(left, AccountingTaxDirectionValues.Both) || Comparer.Equals(right, AccountingTaxDirectionValues.Both) ||
        Comparer.Equals(left, right);
    private static bool ListsOverlap(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is null || left.Count == 0 || right is null || right.Count == 0 || left.Intersect(right, Comparer).Any();
}
