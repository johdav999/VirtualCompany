using Microsoft.Extensions.Localization;

namespace VirtualCompany.Web.Localization.Sales;

public static class SalesStatusPresenter
{
    private static readonly IReadOnlyDictionary<string, string> Keys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["active"] = "StatusActive",
            ["accepted"] = "StatusAccepted",
            ["approved"] = "StatusApproved",
            ["bounced"] = "StatusBounced",
            ["cancelled"] = "StatusCancelled",
            ["candidate"] = "StatusCandidate",
            ["completed"] = "StatusCompleted",
            ["converted"] = "StatusConverted",
            ["delivered"] = "StatusDelivered",
            ["draft"] = "StatusDraft",
            ["failed"] = "StatusFailed",
            ["hot"] = "StatusHot",
            ["in_progress"] = "StatusInProgress",
            ["lost"] = "StatusLost",
            ["low"] = "StatusLow",
            ["medium"] = "StatusMedium",
            ["high"] = "StatusHigh",
            ["new"] = "StatusNew",
            ["open"] = "StatusOpen",
            ["paused"] = "StatusPaused",
            ["pending"] = "StatusPending",
            ["qualified"] = "StatusQualified",
            ["ready_for_review"] = "StatusReadyForReview",
            ["rejected"] = "StatusRejected",
            ["scheduled"] = "StatusScheduled",
            ["planned"] = "StatusPlanned",
            ["running"] = "StatusRunning",
            ["sent"] = "StatusSent",
            ["suppressed"] = "StatusSuppressed",
            ["warm"] = "StatusWarm",
            ["won"] = "StatusWon"
        };

    public static string Present(string? code, IStringLocalizer<SalesResources> text)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return text["StatusUnknown"];
        }

        var normalized = code.Trim().Replace('-', '_').Replace(' ', '_');
        return Keys.TryGetValue(normalized, out var key)
            ? text[key]
            : text["StatusFallback", code.Trim()];
    }
}
