using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingInvitationDomainTests
{
    [Fact]
    public void Invitation_cannot_execute_before_approval()
    {
        var invitation = CreateInvitation();

        Assert.Throws<InvalidOperationException>(() => invitation.BeginScheduling());
        Assert.Equal(SalesMeetingInvitationStatus.Draft, invitation.Status);
    }

    [Fact]
    public void Approved_invitation_moves_through_queue_to_scheduled()
    {
        var invitation = CreateInvitation();
        var approvalId = Guid.NewGuid();
        invitation.SubmitForApproval(approvalId);
        invitation.MarkApproved(Guid.NewGuid(), DateTime.UtcNow);
        invitation.BeginScheduling();
        invitation.MarkScheduled("provider-event", "ical", "https://calendar.example/event", "https://meet.example/join", DateTime.UtcNow);

        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, invitation.Status);
        Assert.Equal(1, invitation.ExecutionAttemptCount);
        Assert.Equal("provider-event", invitation.ExternalEventId);
        Assert.Equal("https://meet.example/join", invitation.OnlineMeetingUrl);
    }

    [Fact]
    public void Rejected_invitation_never_becomes_queued()
    {
        var invitation = CreateInvitation();
        invitation.SubmitForApproval(Guid.NewGuid());
        invitation.MarkRejected(DateTime.UtcNow);

        Assert.Equal(SalesMeetingInvitationStatus.Rejected, invitation.Status);
        Assert.Throws<InvalidOperationException>(() => invitation.BeginScheduling());
    }

    [Fact]
    public void Meeting_change_cannot_execute_before_approval()
    {
        var change = CreateReschedule();

        Assert.Throws<InvalidOperationException>(() => change.BeginExecution());
        Assert.Equal(SalesMeetingChangeRequestStatus.Draft, change.Status);
    }

    [Fact]
    public void Approved_reschedule_moves_through_queue_to_completed()
    {
        var change = CreateReschedule();
        change.SubmitForApproval(Guid.NewGuid());
        change.MarkApproved(Guid.NewGuid(), DateTime.UtcNow);
        change.BeginExecution();
        change.MarkCompleted(DateTime.UtcNow);

        Assert.Equal(SalesMeetingChangeRequestStatus.Completed, change.Status);
        Assert.Equal(1, change.ExecutionAttemptCount);
        Assert.NotNull(change.CompletedUtc);
    }

    [Fact]
    public void Reschedule_preserves_the_existing_provider_event_identity()
    {
        var invitation = CreateInvitation();
        invitation.SubmitForApproval(Guid.NewGuid());
        invitation.MarkApproved(Guid.NewGuid(), DateTime.UtcNow);
        invitation.BeginScheduling();
        invitation.MarkScheduled("provider-event", "ical", "https://calendar.example/event", "https://meet.example/join", DateTime.UtcNow);

        invitation.ApplyReschedule(
            "Updated demo", "Updated agenda.", DateTime.UtcNow.AddDays(2),
            DateTime.UtcNow.AddDays(2).AddMinutes(45), "Europe/Stockholm",
            null, true, "https://calendar.example/event", "https://meet.example/join", DateTime.UtcNow);

        Assert.Equal("provider-event", invitation.ExternalEventId);
        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, invitation.Status);
        Assert.Equal("Updated demo", invitation.Title);
    }

    [Fact]
    public void Scheduled_invitation_can_be_cancelled_without_losing_provider_identity()
    {
        var invitation = CreateInvitation();
        invitation.SubmitForApproval(Guid.NewGuid());
        invitation.MarkApproved(Guid.NewGuid(), DateTime.UtcNow);
        invitation.BeginScheduling();
        invitation.MarkScheduled("provider-event", "ical", null, null, DateTime.UtcNow);

        invitation.MarkCancelled(DateTime.UtcNow);

        Assert.Equal(SalesMeetingInvitationStatus.Cancelled, invitation.Status);
        Assert.Equal("provider-event", invitation.ExternalEventId);
    }

    private static SalesMeetingChangeRequest CreateReschedule() =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            SalesMeetingChangeOperation.Reschedule, Guid.NewGuid(),
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddMinutes(30),
            "Europe/Stockholm", "Updated demo", "Updated agenda.", null, true);
    private static SalesMeetingInvitation CreateInvitation() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExternalAccountProvider.Google,
            "sales@example.com",
            "customer@example.com",
            "Customer",
            "Virtual Company demo",
            "Product overview.",
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddMinutes(30),
            "Europe/Stockholm",
            null,
            true,
            Guid.NewGuid());
}
