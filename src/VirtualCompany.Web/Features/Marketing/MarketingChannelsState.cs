using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingChannelsState
{
    public IReadOnlyList<MarketingChannelConnectionViewModel> ChannelConnections { get; set; } = [];
    public IReadOnlyList<MarketingChannelDestinationViewModel> ChannelDestinations { get; set; } = [];
    public IReadOnlyList<MarketingChannelActionViewModel> ChannelActions { get; set; } = [];


}
