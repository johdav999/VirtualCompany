using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;

namespace VirtualCompany.Web.Localization.Finance;

public sealed record FinanceStatusPresentation(string ResourceKey, bool IsKnown, bool IsShared);

public static class FinanceStatusPresenter
{
    private static readonly IReadOnlyDictionary<string, string> FinanceResourceKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = "StatusCritical",
            ["high"] = "StatusHigh",
            ["medium"] = "StatusMedium",
            ["low"] = "StatusLow",
            ["auto_approve"] = "StatusAutoApprove",
            ["manual_review"] = "StatusManualReview",
            ["follow_up"] = "StatusFollowUp",
            ["send_for_follow_up"] = "StatusFollowUp",
            ["no_action"] = "StatusNoAction",
            ["review"] = "StatusReview"
        };

    public static string Present(
        string code,
        IStringLocalizer<FinanceResources> financeLocalizer,
        IStringLocalizer<CommonResources> commonLocalizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(financeLocalizer);
        ArgumentNullException.ThrowIfNull(commonLocalizer);

        var presentation = Resolve(code);
        if (presentation.IsShared)
        {
            return SharedStatusPresenter.Format(code, commonLocalizer);
        }

        if (presentation.IsKnown)
        {
            return financeLocalizer[presentation.ResourceKey];
        }

        // Legacy API values can still be plain English. Preserve a readable fallback
        // until Prompt 8 replaces those contracts with stable reason and status codes.
        var normalized = Normalize(code);
        return string.Join(
            " ",
            normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static FinanceStatusPresentation Resolve(string? code)
    {
        var normalized = Normalize(code);
        var shared = SharedStatusPresenter.Resolve(normalized);
        if (shared.IsKnown)
        {
            return new FinanceStatusPresentation(shared.ResourceKey, true, true);
        }

        return FinanceResourceKeys.TryGetValue(normalized, out var resourceKey)
            ? new FinanceStatusPresentation(resourceKey, true, false)
            : new FinanceStatusPresentation("StatusUnknown", false, false);
    }

    private static string Normalize(string? code) =>
        code?.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant() ?? string.Empty;
}
