using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingContentState
{
    public MarketingContentPreflightViewModel? ContentPreflight { get; set; }
    public GenerateMarketingContentVariantsResultViewModel? ContentGenerationResult { get; set; }
    public ContentFormModel ContentForm { get; set; } = new();
    public ContentGenerationFormModel ContentGenerationForm { get; set; } = new();


    public sealed class ContentFormModel
    {
        public string Title { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string Audience { get; set; } = "";
        public string Channel { get; set; } = "email";
        public string Language { get; set; } = "English";
        public string MeasurableObjective { get; set; } = "Increase qualified engagement";
        public string FunnelStage { get; set; } = "awareness";
        public string KeyMessage { get; set; } = "";
        public string Offer { get; set; } = "Not specified";
        public string CallToAction { get; set; } = "";
    }
    public sealed class ContentGenerationFormModel
    {
        public Guid BriefId { get; set; }
        public string ContentFormat { get; set; } = "social_post";
        public int VariantCount { get; set; } = 3;
        public string Instructions { get; set; } = "Create distinct angles while preserving the brief's language, tone, evidence, and call to action.";
    }
}
