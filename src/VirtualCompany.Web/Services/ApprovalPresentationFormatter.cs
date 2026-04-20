using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VirtualCompany.Web.Services;

public static partial class ApprovalPresentationFormatter
{
    public static ApprovalRequestViewModel Format(ApprovalRequestViewModel approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        var displayType = ResolveDisplayType(approval);
        var reference = ResolveReference(approval, displayType);
        var amount = ResolveAmount(approval);
        var reason = ResolveReason(approval);
        var scenario = ResolveScenario(approval);
        var counterparty = ResolveCounterparty(approval);
        var paymentActivity = ResolvePaymentActivity(approval);
        var statusNote = ResolveStatusNote(approval);

        approval.DisplayType = displayType;
        approval.DisplayTitle = $"{displayType} requires approval";
        approval.DisplayStatus = HumanizeStatus(approval.Status);
        approval.DisplayReference = reference;
        approval.DisplayAmount = amount;
        approval.DisplayReason = reason;
        approval.DisplayDecisionSummary = BuildDecisionSummary(reference, amount, reason);
        approval.DisplayTrigger = ResolveTrigger(approval);
        approval.DisplayScenario = scenario;
        approval.DisplayCounterparty = counterparty;
        approval.DisplayPaymentActivity = paymentActivity;
        approval.DisplayStatusNote = statusNote;

        return approval;
    }

    public static List<ApprovalRequestViewModel> Format(IEnumerable<ApprovalRequestViewModel> approvals) =>
        approvals.Select(Format).ToList();

    private static string ResolveDisplayType(ApprovalRequestViewModel approval)
    {
        if (TryReadString(approval.ThresholdContext, "billNumber") is not null ||
            approval.AffectedEntities.Any(x => string.Equals(x.EntityType, "bill", StringComparison.OrdinalIgnoreCase)) ||
            approval.AffectedDataSummary.Contains("SIM-BILL-", StringComparison.OrdinalIgnoreCase))
        {
            return "Bill";
        }

        if (TryReadString(approval.ThresholdContext, "invoiceNumber") is not null ||
            approval.AffectedEntities.Any(x => string.Equals(x.EntityType, "invoice", StringComparison.OrdinalIgnoreCase)) ||
            approval.AffectedDataSummary.Contains("SIM-INV-", StringComparison.OrdinalIgnoreCase))
        {
            return "Invoice";
        }

        return "Payment";
    }

    private static string ResolveReference(ApprovalRequestViewModel approval, string displayType)
    {
        var explicitReference = FirstNonEmpty(
            TryReadString(approval.ThresholdContext, "billNumber", "invoiceNumber"),
            ExtractReference(approval.AffectedDataSummary),
            approval.AffectedEntities
                .Select(entity => ExtractReference(entity.Label))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(explicitReference))
        {
            return explicitReference!;
        }

        return displayType switch
        {
            "Bill" => "Bill reference unavailable",
            "Invoice" => "Invoice reference unavailable",
            _ => "Reference unavailable"
        };
    }

    private static string? ResolveAmount(ApprovalRequestViewModel approval)
    {
        var amount = TryGetDecimal(approval.ThresholdContext, "amount") ??
            TryGetDecimal(approval.ThresholdContext, "invoiceAmount");
        var currency = TryReadString(approval.ThresholdContext, "currency", "invoiceCurrency");

        if (amount.HasValue && !string.IsNullOrWhiteSpace(currency))
        {
            return $"{amount.Value:0.##} {currency}";
        }

        var match = AmountRegex().Match(approval.AffectedDataSummary);
        return match.Success
            ? $"{match.Groups["amount"].Value} {match.Groups["currency"].Value}"
            : null;
    }

    private static string ResolveReason(ApprovalRequestViewModel approval)
    {
        var combinedText = string.Join(
            " ",
            new[]
            {
                approval.RationaleSummary,
                approval.ThresholdSummary,
                approval.DecisionSummary
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (combinedText.Contains("threshold", StringComparison.OrdinalIgnoreCase) ||
            combinedText.Contains("exceed", StringComparison.OrdinalIgnoreCase) ||
            approval.ThresholdContext.Count > 0)
        {
            return "Exceeds approval limit";
        }

        return "Requires approval";
    }

    private static string BuildDecisionSummary(string reference, string? amount, string reason)
    {
        if (!string.IsNullOrWhiteSpace(amount))
        {
            return $"Approve payment of {amount} for {reference} ({reason.ToLowerInvariant()}).";
        }

        return $"Approve payment for {reference} ({reason.ToLowerInvariant()}).";
    }

    private static string ResolveTrigger(ApprovalRequestViewModel approval)
    {
        if (string.Equals(approval.RequestedByActorType, "system", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(approval.RequestedByActorType, "agent", StringComparison.OrdinalIgnoreCase))
        {
            return "Automatically generated";
        }

        return "Created from a manual workflow";
    }

    private static string? ResolveScenario(ApprovalRequestViewModel approval)
    {
        var scenario = FirstNonEmpty(
            TryReadString(approval.ThresholdContext, "scenario", "invoiceScenario"),
            TryReadString(approval.ThresholdContext, "thresholdCase"));

        return string.IsNullOrWhiteSpace(scenario)
            ? null
            : scenario!.Trim().ToUpperInvariant();
    }

    private static string? ResolveCounterparty(ApprovalRequestViewModel approval)
    {
        var explicitCounterparty = FirstNonEmpty(
            TryReadString(approval.ThresholdContext, "vendorName", "counterpartyName", "supplierName", "customerName"),
            ExtractLabeledValue(approval.AffectedDataSummary, "Counterparty"));

        return string.IsNullOrWhiteSpace(explicitCounterparty) ? null : explicitCounterparty;
    }

    private static string? ResolvePaymentActivity(ApprovalRequestViewModel approval)
    {
        var explicitSummary = ExtractLabeledValue(approval.AffectedDataSummary, "Payment activity");
        if (!string.IsNullOrWhiteSpace(explicitSummary))
        {
            return explicitSummary;
        }

        var transactionCount = TryGetNestedInt(approval.ThresholdContext, "relatedPaymentContext", "transactionCount");
        if (!transactionCount.HasValue || transactionCount.Value <= 0)
        {
            return null;
        }

        var totalPaidAmount = TryGetNestedDecimal(approval.ThresholdContext, "relatedPaymentContext", "totalPaidAmount");
        var currency = FirstNonEmpty(
            TryGetNestedString(approval.ThresholdContext, "relatedPaymentContext", "currency"),
            TryReadString(approval.ThresholdContext, "currency", "invoiceCurrency"));

        return totalPaidAmount.HasValue && !string.IsNullOrWhiteSpace(currency)
            ? $"{transactionCount.Value} transaction(s) totaling {totalPaidAmount.Value:0.##} {currency}"
            : $"{transactionCount.Value} related transaction(s)";
    }

    private static string? ResolveStatusNote(ApprovalRequestViewModel approval)
    {
        var status = FirstNonEmpty(
            TryReadString(approval.ThresholdContext, "status", "invoiceStatus"),
            ExtractLabeledValue(approval.AffectedDataSummary, "Status"));

        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return HumanizeToken(status!);
    }

    private static string HumanizeStatus(string status) => HumanizeToken(status);

    private static string HumanizeToken(string value)
    {
        var normalized = value.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string? TryReadString(IReadOnlyDictionary<string, JsonNode?>? nodes, params string[] keys)
    {
        if (nodes is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!nodes.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue.Trim();
            }

            var text = node.ToJsonString().Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static decimal? TryGetDecimal(IReadOnlyDictionary<string, JsonNode?>? nodes, string key)
    {
        if (nodes is null || !nodes.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static int? TryGetNestedInt(IReadOnlyDictionary<string, JsonNode?>? nodes, string parentKey, string childKey)
    {
        if (!TryGetNestedNode(nodes, parentKey, childKey, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static decimal? TryGetNestedDecimal(IReadOnlyDictionary<string, JsonNode?>? nodes, string parentKey, string childKey)
    {
        if (!TryGetNestedNode(nodes, parentKey, childKey, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static string? TryGetNestedString(IReadOnlyDictionary<string, JsonNode?>? nodes, string parentKey, string childKey)
    {
        if (!TryGetNestedNode(nodes, parentKey, childKey, out var node))
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue.Trim();
        }

        var text = node?.ToJsonString().Trim().Trim('"');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryGetNestedNode(IReadOnlyDictionary<string, JsonNode?>? nodes, string parentKey, string childKey, out JsonNode? result)
    {
        result = null;
        if (nodes is null || !nodes.TryGetValue(parentKey, out var parent) || parent is not JsonObject parentObject)
        {
            return false;
        }

        return parentObject.TryGetPropertyValue(childKey, out result) && result is not null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? ExtractReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = ReferenceRegex().Match(value);
        return match.Success ? match.Value : null;
    }

    private static string? ExtractLabeledValue(string? source, string label)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var parts = source.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
            {
                return part[(label.Length + 1)..].Trim();
            }
        }

        return null;
    }

    [GeneratedRegex(@"SIM-(INV|BILL)-[A-Z0-9-]+", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"Amount:\s*(?<amount>[0-9]+(?:[.,][0-9]+)?)\s+(?<currency>[A-Z]{3})", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();
}
