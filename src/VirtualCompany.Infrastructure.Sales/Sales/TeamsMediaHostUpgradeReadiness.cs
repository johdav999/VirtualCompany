using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsMediaHostUpgradeReadiness(
    VirtualCompanyDbContext db, ITeamsMediaHostRuntime runtime) : ITeamsMediaHostUpgradeReadiness
{
    public async Task<bool> IsDrainedAsync(CancellationToken ct)
    {
        var status = runtime.GetStatus();
        if (status.State != TeamsMediaHostStates.Draining || status.ActiveCalls != 0) return false;
        return !await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking().AnyAsync(call =>
            call.MediaHostInstanceId == status.HostInstanceId &&
            call.State != TeamsMeetingCallStates.Ended &&
            call.State != TeamsMeetingCallStates.Failed &&
            call.State != TeamsMeetingCallStates.Rejected, ct);
    }
}
