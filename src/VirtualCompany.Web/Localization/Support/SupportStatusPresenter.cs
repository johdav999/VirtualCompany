using Microsoft.Extensions.Localization;

namespace VirtualCompany.Web.Localization.Support;

public static class SupportStatusPresenter
{
    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["new"] = "StatusNew", ["triaged"] = "StatusTriaged", ["waiting_for_customer"] = "StatusWaitingCustomer",
        ["waiting_internal"] = "StatusWaitingInternal", ["escalated"] = "StatusEscalated", ["awaiting_approval"] = "StatusAwaitingApproval",
        ["resolved"] = "StatusResolved", ["urgent"] = "PriorityUrgent", ["high"] = "PriorityHigh", ["normal"] = "PriorityNormal", ["low"] = "PriorityLow",
        ["billing"] = "CategoryBilling", ["refund"] = "CategoryRefund", ["technical_issue"] = "CategoryTechnicalIssue", ["account_access"] = "CategoryAccountAccess",
        ["complaint"] = "CategoryComplaint", ["bug_report"] = "CategoryBugReport", ["churn_risk"] = "CategoryChurnRisk",
        ["general_question"] = "CategoryGeneralQuestion", ["review"] = "NeedsReview", ["approved"] = "StatusApproved",
        ["rejected"] = "StatusRejected", ["expired"] = "StatusExpired", ["deleted"] = "StatusDeleted",
        ["open"] = "Open", ["linked_to_task"] = "StatusDocumentationPlanned", ["indexed"] = "StatusIndexed",
        ["failed"] = "StatusFailed", ["indexing"] = "StatusIndexing", ["queued"] = "StatusQueued", ["processing"] = "StatusProcessing"
    };

    public static string Present(string? code, IStringLocalizer<SupportResources> text)
    {
        if (string.IsNullOrWhiteSpace(code)) return text["Unknown"];
        var normalized = code.Trim().Replace('-', '_').Replace(' ', '_');
        return Keys.TryGetValue(normalized, out var key) ? text[key] : text["Fallback", code.Trim()];
    }
}
