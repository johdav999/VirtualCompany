using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingJourneysState
{
    public IReadOnlyList<MarketingJourneyViewModel> Journeys { get; set; } = [];
    public IReadOnlyList<MarketingJourneyEnrollmentViewModel> JourneyEnrollments { get; set; } = [];
    public MarketingJourneyViewModel? SelectedJourney { get; set; }
    public MarketingJourneyValidationViewModel? JourneyValidation { get; set; }
    public MarketingJourneyAudiencePreviewViewModel? JourneyAudiencePreview { get; set; }


}
