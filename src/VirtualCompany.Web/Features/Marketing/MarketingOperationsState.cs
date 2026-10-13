using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingOperationsState
{
    public IReadOnlyList<MarketingOperatingRunViewModel> OperatingRuns { get; set; } = [];
    public MarketingOperatingRunViewModel? SelectedOperatingRun { get; set; }
    public IReadOnlyList<MarketingOperatingActionViewModel> OperatingActions { get; set; } = [];
    public IReadOnlyList<MarketingEventTriggerViewModel> Events { get; set; } = [];


}
