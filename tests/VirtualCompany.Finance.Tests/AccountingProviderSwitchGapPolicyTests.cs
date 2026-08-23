using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchGapPolicyTests
{
    private readonly AccountingProviderSwitchGapPolicy _policy = new();

    [Theory]
    [InlineData("accounts", "account_mapping")]
    [InlineData("tax", "tax_mapping")]
    [InlineData("fiscal_periods", "locked_periods")]
    [InlineData("voucher_numbering", "numbering")]
    [InlineData("invoices", "open_items")]
    [InlineData("payments", "payment_allocation")]
    [InlineData("allocations", "payment_allocation")]
    [InlineData("currencies", "currency")]
    [InlineData("exchange_rates", "currency")]
    [InlineData("dimensions", "dimensions")]
    [InlineData("journals", "aggregate_mismatch")]
    [InlineData("bank_reconciliation", "reconciliation")]
    [InlineData("customers", "timing")]
    public void Aggregate_mismatches_map_to_deterministic_gap_categories(string dataset, string category)
    {
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.FullHistory, CompleteCapabilities(),
        [
            Dataset("source", dataset, 2, 100m),
            Dataset("target", dataset, 1, 90m)
        ]));

        Assert.Contains(gaps, x => x.Category == category && x.DatasetKey == dataset && x.IsBlocking);
    }

    [Fact]
    public void Missing_scope_is_actionable_and_never_treated_as_absent()
    {
        var capabilities = CompleteCapabilities().Where(x =>
            !(x.EndpointRole == "target" && x.CapabilityKey == AccountingProviderSwitchCapabilityKeys.Invoices)).ToList();
        capabilities.Add(new("target", AccountingProviderSwitchCapabilityKeys.Invoices, "unknown",
            "Required scope is missing.", "invoice", DateTime.UnixEpoch));
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, capabilities,
        [
            Dataset("source", "invoices", 3, 300m, "not_authorized")
        ]));

        Assert.Contains(gaps, x => x.Category == "missing_provider_scope" && x.ReasonCode == "source_not_authorized" && x.IsBlocking);
        Assert.DoesNotContain(gaps, x => x.ReasonCode.Contains("absent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, false)]
    [InlineData(AccountingProviderSwitchStrategies.CurrentFiscalYear, false)]
    [InlineData(AccountingProviderSwitchStrategies.FullHistory, true)]
    public void Historical_attachment_gap_uses_strategy_dependent_severity(string strategy, bool blocking)
    {
        var gaps = _policy.Evaluate(new(strategy, CompleteCapabilities(),
        [
            Dataset("source", "attachments", 5, 0m),
            Dataset("target", "attachments", 0, 0m)
        ]));

        Assert.Contains(gaps, x => x.Category == "documents" && x.IsBlocking == blocking);
    }

    [Theory]
    [InlineData("not_returned")]
    [InlineData("not_authorized")]
    [InlineData("unsupported")]
    [InlineData("unknown")]
    public void Incomplete_source_states_produce_unknown_or_scope_gaps(string availability)
    {
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.FullHistory, CompleteCapabilities(),
            [Dataset("source", "journals", 0, 0, availability)]));
        Assert.Contains(gaps, x => x.Category is "unknown_provider_outcome" or "missing_provider_scope");
    }

    [Fact]
    public void Confirmed_absence_is_not_reported_as_unknown()
    {
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.FullHistory, CompleteCapabilities(),
            [Dataset("source", "journals", 0, 0, "confirmed_absent")]));
        Assert.DoesNotContain(gaps, x => x.ReasonCode.StartsWith("source_", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_identifiers_are_always_blocking()
    {
        var source = Dataset("source", "stable_identifiers", 3, 0) with { EvidenceJson = "{\"duplicateCount\":2}" };
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
            CompleteCapabilities(), [source]));
        Assert.Contains(gaps, x => x.Category == "duplicates" && x.ReasonCode == "duplicate_stable_identifier" && x.IsBlocking);
    }

    [Fact]
    public void Unsupported_target_capability_and_missing_configuration_are_explicit()
    {
        var capabilities = CompleteCapabilities().Where(x =>
            !(x.EndpointRole == "target" && x.CapabilityKey is AccountingProviderSwitchCapabilityKeys.Tax or AccountingProviderSwitchCapabilityKeys.Accounts)).ToList();
        capabilities.Add(new("target", AccountingProviderSwitchCapabilityKeys.Accounts, "unsupported",
            "Accounts cannot be imported.", null, DateTime.UnixEpoch));
        var gaps = _policy.Evaluate(new(AccountingProviderSwitchStrategies.FullHistory, capabilities, []));

        Assert.Contains(gaps, x => x.Category == "unsupported_target_capability" && x.DatasetKey == "accounts");
        Assert.Contains(gaps, x => x.Category == "missing_configuration" && x.DatasetKey == "tax");
    }

    private static IReadOnlyList<AccountingProviderSwitchCapabilityDto> CompleteCapabilities() =>
        AccountingProviderSwitchCapabilityKeys.All.SelectMany(key => new[]
        {
            new AccountingProviderSwitchCapabilityDto("source", key, "supported", "Supported by source.", null, DateTime.UnixEpoch),
            new AccountingProviderSwitchCapabilityDto("target", key, "supported", "Supported by target.", null, DateTime.UnixEpoch)
        }).ToArray();

    private static AccountingProviderSwitchDatasetDto Dataset(string role, string key, long count, decimal total,
        string availability = "available") =>
        new(role, key, availability, "supported", count, total, null, null, "v1", new string('a', 64),
            "{}", null, null, DateTime.UnixEpoch);
}
