using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Sales;

// Consent, provider health, permission, quota and lease checks still run in StartAsync.
// This policy only identifies the first join, not an automatic recovery/restart.
public static class SalesRoomAgentJoinPolicy
{
    public static bool CanStartAutomatically(SalesBrowserRoom room, DateTime nowUtc) =>
        room.AllowsAccess(nowUtc) &&
        room.AgentHealth is SalesRoomAgentHealthStates.NotStarted or SalesRoomAgentHealthStates.Stopped &&
        room.AgentLastErrorCode is null or "room_ending" or "room_ended";
}
