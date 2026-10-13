using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingPerformanceState
{
    public IReadOnlyList<MarketingAttributionViewModel> Attribution { get; set; } = [];


}
