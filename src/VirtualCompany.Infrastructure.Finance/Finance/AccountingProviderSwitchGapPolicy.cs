using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchGapPolicy : IAccountingProviderSwitchGapPolicy
{
    private static readonly IReadOnlyDictionary<string, string> DatasetCategories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AccountingProviderSwitchDatasetKeys.Accounts] = "account_mapping",
            [AccountingProviderSwitchDatasetKeys.Tax] = "tax_mapping",
            [AccountingProviderSwitchDatasetKeys.FiscalPeriods] = "locked_periods",
            [AccountingProviderSwitchDatasetKeys.VoucherNumbering] = "numbering",
            [AccountingProviderSwitchDatasetKeys.Invoices] = "open_items",
            [AccountingProviderSwitchDatasetKeys.Credits] = "open_items",
            [AccountingProviderSwitchDatasetKeys.Payments] = "payment_allocation",
            [AccountingProviderSwitchDatasetKeys.Allocations] = "payment_allocation",
            [AccountingProviderSwitchDatasetKeys.Currencies] = "currency",
            [AccountingProviderSwitchDatasetKeys.ExchangeRates] = "currency",
            [AccountingProviderSwitchDatasetKeys.Dimensions] = "dimensions",
            [AccountingProviderSwitchDatasetKeys.Attachments] = "documents",
            [AccountingProviderSwitchDatasetKeys.StableIdentifiers] = "duplicates",
            [AccountingProviderSwitchDatasetKeys.BankReconciliation] = "reconciliation",
            [AccountingProviderSwitchDatasetKeys.Journals] = "aggregate_mismatch"
        };

    public IReadOnlyList<AccountingProviderSwitchGapDecision> Evaluate(AccountingProviderSwitchGapInput input)
    {
        var strategy = AccountingProviderSwitchStrategies.Normalize(input.Strategy);
        var gaps = new List<AccountingProviderSwitchGapDecision>();
        var capabilities = input.Capabilities.ToLookup(x => (x.EndpointRole, x.CapabilityKey));

        foreach (var required in new[]
        {
            AccountingProviderSwitchCapabilityKeys.Accounts,
            AccountingProviderSwitchCapabilityKeys.Tax,
            AccountingProviderSwitchCapabilityKeys.FiscalPeriods,
            AccountingProviderSwitchCapabilityKeys.VoucherNumbering,
            AccountingProviderSwitchCapabilityKeys.Invoices,
            AccountingProviderSwitchCapabilityKeys.Payments,
            AccountingProviderSwitchCapabilityKeys.Journals,
            AccountingProviderSwitchCapabilityKeys.StableIdentifiers
        })
        {
            if (capabilities[(AccountingProviderSwitchEndpointRoles.Target, required)].Any()) continue;
            gaps.Add(Decision("missing_configuration", CapabilityDataset(required), true,
                "target_capability_not_reported",
                $"The target did not report the required {required.Replace('_', ' ')} capability.",
                new { capability = required },
                "Verify the target adapter and connection configuration, then replay the assessment."));
        }

        foreach (var target in input.Capabilities.Where(x => x.EndpointRole == AccountingProviderSwitchEndpointRoles.Target))
        {
            if (target.Level == AccountingProviderSwitchCapabilityLevels.Supported) continue;
            var source = capabilities[(AccountingProviderSwitchEndpointRoles.Source, target.CapabilityKey)].FirstOrDefault();
            if (source?.Level is not (AccountingProviderSwitchCapabilityLevels.Supported or AccountingProviderSwitchCapabilityLevels.Partial)) continue;

            var missingScope = target.Level == AccountingProviderSwitchCapabilityLevels.Unknown && target.RequiredScope is not null;
            var category = missingScope ? "missing_provider_scope" : "unsupported_target_capability";
            var blocking = IsCapabilityBlocking(target.CapabilityKey, strategy, target.Level);
            gaps.Add(Decision(category, CapabilityDataset(target.CapabilityKey), blocking,
                missingScope ? "provider_scope_missing" : "target_capability_unavailable",
                missingScope
                    ? $"The target connection did not authorize the '{target.RequiredScope}' scope required for {target.CapabilityKey.Replace('_', ' ')}."
                    : $"The source uses {target.CapabilityKey.Replace('_', ' ')}, but the target reports this capability as {target.Level}.",
                new { capability = target.CapabilityKey, source = source.Level, target = target.Level, requiredScope = target.RequiredScope },
                missingScope ? "Reconnect the target and grant the required scope, then replay the assessment."
                    : "Choose a documented migration treatment or a target that supports this feature."));
        }

        foreach (var source in input.Datasets.Where(x => x.EndpointRole == AccountingProviderSwitchEndpointRoles.Source))
        {
            var target = input.Datasets.FirstOrDefault(x =>
                x.EndpointRole == AccountingProviderSwitchEndpointRoles.Target && x.DatasetKey == source.DatasetKey);
            var category = DatasetCategories.GetValueOrDefault(source.DatasetKey, "timing");

            if (source.Availability != AccountingProviderSwitchDatasetAvailability.Available &&
                source.Availability != AccountingProviderSwitchDatasetAvailability.ConfirmedAbsent)
            {
                var blocking = source.Availability != AccountingProviderSwitchDatasetAvailability.Unsupported ||
                               strategy == AccountingProviderSwitchStrategies.FullHistory;
                gaps.Add(Decision(source.Availability == AccountingProviderSwitchDatasetAvailability.NotAuthorized
                        ? "missing_provider_scope" : "unknown_provider_outcome", source.DatasetKey, blocking,
                    $"source_{source.Availability}",
                    $"The source {source.DatasetKey.Replace('_', ' ')} dataset is {source.Availability}; it is not evidence that the data is absent.",
                    new { source.DatasetKey, source.Availability, source.FailureCode },
                    source.Availability == AccountingProviderSwitchDatasetAvailability.NotAuthorized
                        ? "Grant the required source scope and replay the assessment."
                        : "Verify provider availability and replay the read-only extraction."));
                continue;
            }

            if (source.DatasetKey == AccountingProviderSwitchDatasetKeys.StableIdentifiers && DuplicateCount(source.EvidenceJson) > 0)
                gaps.Add(Decision("duplicates", source.DatasetKey, true, "duplicate_stable_identifier",
                    "The source contains duplicate stable identifiers that cannot be migrated deterministically.",
                    new { duplicateCount = DuplicateCount(source.EvidenceJson) },
                    "Resolve duplicate identifiers at the source and replay the assessment."));

            if (target is null || target.Availability is AccountingProviderSwitchDatasetAvailability.Unknown or
                AccountingProviderSwitchDatasetAvailability.NotReturned or AccountingProviderSwitchDatasetAvailability.NotAuthorized)
                continue;

            if (source.Availability == AccountingProviderSwitchDatasetAvailability.Available &&
                target.Availability == AccountingProviderSwitchDatasetAvailability.Available &&
                (source.RecordCount != target.RecordCount || source.FinancialTotal != target.FinancialTotal))
            {
                gaps.Add(Decision(category, source.DatasetKey, IsDatasetMismatchBlocking(source.DatasetKey, strategy),
                    "dataset_aggregate_mismatch",
                    $"Source and target {source.DatasetKey.Replace('_', ' ')} aggregates do not match.",
                    new { sourceCount = source.RecordCount, targetCount = target.RecordCount, sourceTotal = source.FinancialTotal, targetTotal = target.FinancialTotal },
                    "Review mappings and migration scope before planning the transfer."));
            }
        }

        return gaps
            .GroupBy(x => (x.Category, x.DatasetKey, x.ReasonCode))
            .Select(x => x.First())
            .OrderByDescending(x => x.IsBlocking)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.DatasetKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static AccountingProviderSwitchGapDecision Decision(string category, string? dataset, bool blocking,
        string reason, string explanation, object evidence, string action) =>
        new(category, dataset,
            blocking ? AccountingProviderSwitchGapSeverities.Blocking : AccountingProviderSwitchGapSeverities.Warning,
            blocking, reason, explanation, JsonSerializer.Serialize(evidence), action);

    private static bool IsCapabilityBlocking(string capability, string strategy, string level)
    {
        if (level == AccountingProviderSwitchCapabilityLevels.Partial && strategy == AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems)
            return capability is AccountingProviderSwitchCapabilityKeys.Accounts or AccountingProviderSwitchCapabilityKeys.Tax or
                AccountingProviderSwitchCapabilityKeys.Invoices or AccountingProviderSwitchCapabilityKeys.Payments or
                AccountingProviderSwitchCapabilityKeys.Allocations;
        if (capability == AccountingProviderSwitchCapabilityKeys.Attachments)
            return strategy == AccountingProviderSwitchStrategies.FullHistory;
        return capability is not (AccountingProviderSwitchCapabilityKeys.SandboxPreview or AccountingProviderSwitchCapabilityKeys.RateLimits);
    }

    private static bool IsDatasetMismatchBlocking(string dataset, string strategy) =>
        dataset != AccountingProviderSwitchDatasetKeys.Attachments || strategy == AccountingProviderSwitchStrategies.FullHistory;

    private static string? CapabilityDataset(string capability) =>
        AccountingProviderSwitchDatasetKeys.All.Contains(capability, StringComparer.Ordinal) ? capability : null;

    private static int DuplicateCount(string evidence)
    {
        try
        {
            using var document = JsonDocument.Parse(evidence);
            return document.RootElement.TryGetProperty("duplicateCount", out var value) && value.TryGetInt32(out var count) ? count : 0;
        }
        catch (JsonException) { return 0; }
    }
}
