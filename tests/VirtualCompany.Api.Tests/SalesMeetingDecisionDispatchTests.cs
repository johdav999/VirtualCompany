using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesMeetingSchedulingServiceTests
{
    [Fact]
    public async Task Dispatcher_revalidates_revoked_approval_before_calling_calendar_provider()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invitation = await fixture.CreateApprovedInvitationAsync();
        var approval = await fixture.Db.ApprovalRequests.SingleAsync(x => x.Id == invitation.ApprovalRequestId);
        approval.MarkRevoked("Delivery authority withdrawn.");
        await fixture.Db.SaveChangesAsync();
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(fixture.Db, new StaticCalendarTokenLeaseService(),
            new CalendarProviderRegistry([fixture.Provider]), new CapturingOutbox());
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(new SalesMeetingInvitationDeliveryRequestedMessage(
            fixture.CompanyId, invitation.Id, invitation.IdempotencyKey, approval.Id.ToString("N")), CancellationToken.None));
        Assert.Equal(0, fixture.Provider.CreateCalls);
        Assert.Equal(SalesMeetingInvitationStatus.Queued, invitation.Status);
    }
}
