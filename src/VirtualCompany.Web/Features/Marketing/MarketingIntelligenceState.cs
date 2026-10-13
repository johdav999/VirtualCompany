using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingIntelligenceState
{
    public IReadOnlyList<MarketingIntelligenceViewModel> Intelligence { get; set; } = [];
    public IReadOnlyList<MarketingIntelligenceReviewViewModel> IntelligenceReviews { get; set; } = [];
    public MarketingIntelligenceViewModel? SelectedIntelligence { get; set; }
    public string IntelligenceSearch { get; set; } = string.Empty;
    public string IntelligenceFreshness { get; set; } = "all";
    public string IntelligenceReviewRationale { get; set; } = "Reviewed against the currently accessible source evidence.";

    public IReadOnlyList<MarketingIntelligenceViewModel> FilteredIntelligence => Intelligence
        .Where(x => string.IsNullOrWhiteSpace(IntelligenceSearch) || x.Subject.Contains(IntelligenceSearch, StringComparison.OrdinalIgnoreCase) || x.Summary.Contains(IntelligenceSearch, StringComparison.OrdinalIgnoreCase))
        .Where(x => IntelligenceFreshness == "all" || IntelligenceFreshness == "due" && x.ReviewDueUtc <= DateTime.UtcNow || IntelligenceFreshness == "current" && x.ReviewDueUtc > DateTime.UtcNow)
        .ToArray();

}
