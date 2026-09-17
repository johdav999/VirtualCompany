using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesMeetingPresetEligibility
{
    internal static bool Allows(SalesMeetingSessionStatus status, bool browserRoom, bool roomOpen,
        bool connectedParticipants, string? agentHealth) =>
        status is not (SalesMeetingSessionStatus.Completed or SalesMeetingSessionStatus.Cancelled or SalesMeetingSessionStatus.Failed) &&
        (browserRoom
            ? roomOpen && !connectedParticipants && agentHealth is "not_started" or "stopped" or "paused"
            : status == SalesMeetingSessionStatus.Ready);

    internal static async Task<bool> CanChangeAsync(VirtualCompanyDbContext db, SalesMeetingSession session, CancellationToken ct)
    {
        var room = await db.SalesBrowserRooms.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == session.CompanyId && x.MeetingSessionId == session.Id, ct);
        var connected = room is not null && await db.SalesRoomParticipants.AsNoTracking().AnyAsync(
            x => x.CompanyId == session.CompanyId && x.RoomId == room.Id && x.Connected, ct);
        return Allows(session.Status, room is not null,
            room?.State is SalesBrowserRoomStates.Provisioning or SalesBrowserRoomStates.Lobby or SalesBrowserRoomStates.Live,
            connected, room?.AgentHealth);
    }
}
