using VirtualCompany.Application.Sales;
namespace VirtualCompany.Shared.Contracts.SalesCampaigns;
public sealed record StopCampaignRequest(string? Reason);
public sealed record DealCreatedStopRequest(Guid DealId);
public sealed record StopConditionResponse(int CancelledPendingSteps);
public sealed record CampaignVersionRequest(long ExpectedVersion);
public sealed record CaptureCampaignAudienceRequest(Guid SegmentId);
