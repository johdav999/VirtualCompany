using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingCreativeState
{
    public IReadOnlyList<MarketingCreativeAssetViewModel> CreativeAssets { get; set; } = [];
    public IReadOnlyDictionary<Guid, MarketingCreativeAssetScanViewModel> CreativeScans { get; set; } = new Dictionary<Guid, MarketingCreativeAssetScanViewModel>();
    public CreativeFormModel CreativeForm { get; set; } = new();

    public MarketingCreativeAssetScanViewModel? LatestScan(Guid assetId) => CreativeScans.GetValueOrDefault(assetId);

    public sealed class CreativeFormModel
    {
        public Guid BriefId { get; set; }
        public string Name { get; set; } = "Campaign creative";
        public string Prompt { get; set; } = "A clear, confident editorial image with generous negative space and no embedded text.";
        public string AltText { get; set; } = "Marketing campaign illustration";
        public string Dimensions { get; set; } = "1024x1024";
        public string Language { get; set; } = "English";
        public string BrandProfileVersion { get; set; } = "current";
        public string Quality { get; set; } = "medium";
    }
}
