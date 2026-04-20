using System.Text.RegularExpressions;
using VirtualCompany.Application.Focus;
using VirtualCompany.Application.Insights;

namespace VirtualCompany.Application.Common;

public static partial class DisplayTextMapper
{
    public static IReadOnlyList<ActionQueueItemDto> DistinctActionItemsForDisplay(IEnumerable<ActionQueueItemDto> items) =>
        items
            .GroupBy(item => BuildActionDisplayKey(MapActionItem(item)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    public static ActionQueueItemDto MapActionItem(ActionQueueItemDto item) =>
        item with
        {
            Title = MapActionTitle(item.Title, item.Reason),
            Reason = MapActionDescription(item.Title, item.Reason),
            Owner = MapRole(item.Owner)
        };

    public static ActionQueuePageDto MapActionPage(ActionQueuePageDto page) =>
        page with
        {
            Items = page.Items.Select(MapActionItem).ToList()
        };

    public static FocusItemDto MapFocusItem(FocusItemDto item) =>
        item with
        {
            Title = MapFocusTitle(item.Title, item.Description),
            Description = MapFocusDescription(item.Title, item.Description)
        };

    private static string MapActionTitle(string title, string reason)
    {
        if (Contains(reason, "invoice_review"))
        {
            return "Approve pending invoices";
        }

        if (Contains(reason, "bill_review"))
        {
            return "Approve pending bills";
        }

        if (Contains(title, "approval required"))
        {
            return "Review and approve request";
        }

        return FinalizeTitle(title);
    }

    private static string MapActionDescription(string title, string reason)
    {
        if (Contains(reason, "pending invoice_review approval"))
        {
            return "Several invoices are waiting for approval from the finance team.";
        }

        if (Contains(reason, "pending bill_review approval"))
        {
            return "Several bills are waiting for approval from the finance team.";
        }

        if (Contains(reason, "threshold approval"))
        {
            return "A finance approval is waiting and may delay follow-up work.";
        }

        if (Contains(reason, "task is blocked"))
        {
            return "A work item is blocked and needs attention to keep operations moving.";
        }

        if (Contains(reason, "waiting for an approval decision"))
        {
            return "A work item is waiting for approval before it can continue.";
        }

        if (Contains(reason, "task is past due"))
        {
            return "A work item is overdue and should be addressed as soon as possible.";
        }

        if (Contains(reason, "task is due soon"))
        {
            return "A work item is due soon and should be handled before it slips.";
        }

        if (Contains(reason, "workflow is blocked at step"))
        {
            var step = ExtractWorkflowStep(reason);
            return string.IsNullOrWhiteSpace(step)
                ? "A workflow is blocked and needs attention to move forward."
                : $"A workflow is blocked at the {step} step and needs attention to move forward.";
        }

        if (Contains(reason, "workflow is blocked"))
        {
            return "A workflow is blocked and needs attention to move forward.";
        }

        return FinalizeSentence(string.IsNullOrWhiteSpace(reason) ? title : reason);
    }

    private static string MapFocusTitle(string title, string description)
    {
        if (Contains(title, "approval required"))
        {
            return "Review and approve request";
        }

        if (Contains(description, "invoice_review"))
        {
            return "Review pending invoices";
        }

        if (Contains(description, "bill_review"))
        {
            return "Review pending bills";
        }

        return FinalizeTitle(title);
    }

    private static string MapFocusDescription(string title, string description)
    {
        if (Contains(description, "pending invoice_review approval"))
        {
            return "Several invoices are waiting for approval from the finance team.";
        }

        if (Contains(description, "pending bill_review approval"))
        {
            return "Several bills are waiting for approval from the finance team.";
        }

        if (Contains(description, "threshold approval"))
        {
            return "A finance approval must be reviewed before the related work can continue.";
        }

        if (Contains(description, "finance anomaly"))
        {
            return FinalizeSentence(description.Replace("Finance anomaly", "A finance issue", StringComparison.OrdinalIgnoreCase));
        }

        return FinalizeSentence(string.IsNullOrWhiteSpace(description) ? title : description);
    }

    public static string MapRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Operations team";
        }

        return value.Trim() switch
        {
            var role when role.Equals("finance_approver", StringComparison.OrdinalIgnoreCase) => "Finance team",
            var role when role.Equals("support_agent", StringComparison.OrdinalIgnoreCase) => "Support team",
            var role when role.Equals("sales_lead", StringComparison.OrdinalIgnoreCase) => "Sales team",
            var role when role.Equals("owner", StringComparison.OrdinalIgnoreCase) => "Operations team",
            _ => FinalizeTitle(value)
        };
    }

    private static string FinalizeTitle(string value)
    {
        var normalized = ReplaceKnownTerms(value);
        normalized = RemoveTechnicalNoise(normalized);
        normalized = normalized.Replace("Finance anomaly:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        normalized = normalized.Replace("Approval required for task", "Review and approve request", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("Approval required", "Review and approve request", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("Resolve blocked fulfillment task", "Resolve blocked fulfillment work", StringComparison.OrdinalIgnoreCase);
        normalized = CollapseWhitespace(normalized);

        return string.IsNullOrWhiteSpace(normalized)
            ? "Review item"
            : EnsureTitleCase(normalized);
    }

    private static string FinalizeSentence(string value)
    {
        var normalized = ReplaceKnownTerms(value);
        normalized = RemoveTechnicalNoise(normalized);
        normalized = normalized.Replace("for task.", ".", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("for task", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        normalized = CollapseWhitespace(normalized);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "This item needs attention.";
        }

        normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        return normalized.EndsWith(".", StringComparison.Ordinal) ? normalized : $"{normalized}.";
    }

    private static string ReplaceKnownTerms(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("multiple_payments", "duplicate payments detected", StringComparison.OrdinalIgnoreCase)
            .Replace("suspicious_payment_timing", "unusual payment timing detected", StringComparison.OrdinalIgnoreCase)
            .Replace("missing_document", "missing required document", StringComparison.OrdinalIgnoreCase)
            .Replace("category_mismatch", "transaction category mismatch", StringComparison.OrdinalIgnoreCase)
            .Replace("invoice_review", "pending invoices", StringComparison.OrdinalIgnoreCase)
            .Replace("bill_review", "pending bills", StringComparison.OrdinalIgnoreCase)
            .Replace("finance_approver", "Finance team", StringComparison.OrdinalIgnoreCase)
            .Replace("support_agent", "Support team", StringComparison.OrdinalIgnoreCase)
            .Replace("sales_lead", "Sales team", StringComparison.OrdinalIgnoreCase)
            .Replace("blocked_workflow", "blocked workflow", StringComparison.OrdinalIgnoreCase)
            .Replace("_", " ", StringComparison.Ordinal);
    }

    private static string RemoveTechnicalNoise(string value)
    {
        var normalized = GuidPattern().Replace(value, string.Empty);
        normalized = UserIdPattern().Replace(normalized, string.Empty);
        normalized = LongTokenPattern().Replace(normalized, string.Empty);
        normalized = normalized.Replace("Owner:", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("Impact:", string.Empty, StringComparison.OrdinalIgnoreCase);
        return CollapseWhitespace(normalized);
    }

    private static string ExtractWorkflowStep(string value)
    {
        var marker = "step ";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var step = value[(index + marker.Length)..].Trim().TrimEnd('.');
        return ReplaceKnownTerms(step);
    }

    private static string EnsureTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string CollapseWhitespace(string value) =>
        SpacePattern().Replace(value, " ").Trim(' ', '.', ':', '|', '-');

    private static string BuildActionDisplayKey(ActionQueueItemDto item) =>
        $"{CollapseWhitespace(item.Title)}|{CollapseWhitespace(item.Reason)}|{item.Priority}";

    private static bool Contains(string value, string comparison) =>
        value.Contains(comparison, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\bUser\s+[0-9a-f]{32}\b", RegexOptions.IgnoreCase)]
    private static partial Regex UserIdPattern();

    [GeneratedRegex(@"\b[a-z]+[:/][a-z0-9\-:]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex LongTokenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
