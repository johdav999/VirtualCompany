namespace VirtualCompany.Infrastructure.Sales;
public sealed class SalesRoomLifecycleOptions
{
    public const string SectionName = "SalesBrowserRoom:Lifecycle";
    public string PublicOrigin {get;set;}="";
    public bool Enabled { get; set; }
    public int MaximumRoomsPerCompany { get; set; } = 20;
    public int MaximumLiveRoomsPerCompany { get; set; } = 2;
    public int MaximumParticipants { get; set; } = 6;
    public int MaximumInvitationsPerRoom { get; set; } = 30;
    public string NoticeVersion { get; set; } = "browser-room-v1";
}
