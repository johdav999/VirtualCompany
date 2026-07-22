using Microsoft.Extensions.Localization;

namespace VirtualCompany.Web.Localization.Shared;

public sealed record SharedStatusPresentation(string ResourceKey, bool IsKnown);

public static class SharedStatusPresenter
{
    private static readonly IReadOnlyDictionary<string, string> ResourceKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["active"] = "StatusActive",
            ["approved"] = "StatusApproved",
            ["archived"] = "StatusArchived",
            ["cancelled"] = "StatusCancelled",
            ["completed"] = "StatusCompleted",
            ["configuration_required"] = "StatusConfigurationRequired",
            ["draft"] = "StatusDraft",
            ["failed"] = "StatusFailed",
            ["in_progress"] = "StatusInProgress",
            ["needs_review"] = "StatusNeedsReview",
            ["paused"] = "StatusPaused",
            ["pending"] = "StatusPending",
            ["pending_review"] = "StatusPendingReview",
            ["rejected"] = "StatusRejected",
            ["restricted"] = "StatusRestricted",
            ["succeeded"] = "StatusSucceeded"
        };

    public static SharedStatusPresentation Resolve(string? code)
    {
        var normalized = code?.Trim().Replace('-', '_').ToLowerInvariant();
        return normalized is not null && ResourceKeys.TryGetValue(normalized, out var resourceKey)
            ? new SharedStatusPresentation(resourceKey, true)
            : new SharedStatusPresentation("StatusUnknown", false);
    }

    public static string Format(string? code, IStringLocalizer<CommonResources> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var presentation = Resolve(code);
        var localized = localizer[presentation.ResourceKey];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? presentation.IsKnown ? presentation.ResourceKey : "Unknown status"
            : localized.Value;
    }
}
