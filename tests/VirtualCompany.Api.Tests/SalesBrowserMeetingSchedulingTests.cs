using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;
namespace VirtualCompany.Api.Tests;
public sealed partial class SalesMeetingSchedulingServiceTests
{
    private static (SalesBrowserMeetingScheduling Service, SalesRoomWorkDispatcher Worker, RoomMedia Media) Browser(Fixture f)
    {
        var media = new RoomMedia(); var limits = Options.Create(new SalesRoomLifecycleOptions { Enabled = true, PublicOrigin = "https://rooms.example.test" });
        var options = Options.Create(new SalesRoomMediaOptions { Enabled = true, Url = "wss://test.livekit.cloud", ApiKey = "test", ApiSecret = new string('x', 32) });
        var outbox = new RoomOutbox(f.Db);
        return (new(f.Db, outbox, new EphemeralDataProtectionProvider(), limits, options, TimeProvider.System), new(f.Db, outbox, media, media, limits, TimeProvider.System), media);
    }
    private static async Task ReadyBrowser(Fixture f, SalesMeetingInvitation invitation, (SalesBrowserMeetingScheduling Service, SalesRoomWorkDispatcher Worker, RoomMedia Media) browser)
    {
        var error = await Assert.ThrowsAsync<CalendarProviderException>(() => browser.Service.PrepareAsync(f.CompanyId, invitation.Id, default)); Assert.Equal("browser_room_preparing", error.Code);
        var op = await f.Db.SalesRoomOperations.SingleAsync(x => x.Action == "provision"); await browser.Worker.DispatchAsync(new(f.CompanyId, op.RoomId, op.Id), default);
    }
    [Fact]
    public async Task Browser_selection_is_explicit_in_review_and_never_creates_an_online_calendar_meeting()
    {
        await using var f = await Fixture.CreateAsync(); var browser = Browser(f);
        var service = new SalesMeetingSchedulingService(f.Db, f.Approvals, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), f.Outbox, browser.Service);
        var result = await service.CreateForLeadAsync(f.CompanyId, f.UserId, f.LeadId, f.Request() with { Conferencing = "browser" }, default);
        Assert.Equal("browser", result.Conferencing); Assert.False(result.CreateOnlineMeeting); Assert.Contains("AI meeting assistant notice", result.Description);
        Assert.Equal("browser", f.Approvals.LastCommand!.ThresholdContext!["conferencing"]!.GetValue<string>());
        Assert.Equal(0, f.Provider.CreateCalls); Assert.Empty(await f.Db.SalesBrowserRooms.ToListAsync());
    }
    [Fact]
    public async Task Scheduling_command_replay_returns_one_approval_and_rejects_changed_payload()
    {
        await using var f = await Fixture.CreateAsync(); var browser = Browser(f);
        var service = new SalesMeetingSchedulingService(f.Db, f.Approvals, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), f.Outbox, browser.Service);
        var request = f.Request() with { Conferencing = "browser", CommandId = Guid.NewGuid() };
        var first = await service.CreateForLeadAsync(f.CompanyId, f.UserId, f.LeadId, request, default);
        var replay = await service.CreateForLeadAsync(f.CompanyId, f.UserId, f.LeadId, request, default);
        Assert.Equal(first.Id, replay.Id); Assert.Single(await f.Db.SalesMeetingInvitations.ToListAsync());
        await Assert.ThrowsAsync<SalesValidationException>(() => service.CreateForLeadAsync(f.CompanyId, f.UserId, f.LeadId, request with { Title = "Another request" }, default));
    }
    [Theory]
    [InlineData(ExternalAccountProvider.Microsoft365, true, "teams")]
    [InlineData(ExternalAccountProvider.Google, true, "google_meet")]
    [InlineData(ExternalAccountProvider.Microsoft365, false, "none")]
    public void Legacy_conferencing_is_derived_from_explicit_calendar_fields(ExternalAccountProvider provider, bool online, string expected)
    { Assert.Equal(expected, SalesMeetingConferencing.Resolve(null, online, provider)); Assert.Equal("browser", SalesMeetingConferencing.Resolve("browser", online, provider)); }
    [Fact]
    public async Task Browser_room_and_secret_are_durable_once_and_calendar_delivery_reuses_them()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); var browser = Browser(f);
        await ReadyBrowser(f, invitation, browser);
        var link = await browser.Service.PrepareAsync(f.CompanyId, invitation.Id, default);
        Assert.Equal(link, await browser.Service.PrepareAsync(f.CompanyId, invitation.Id, default));
        Assert.Single(await f.Db.SalesBrowserRooms.ToListAsync()); Assert.Null((await f.Db.SalesBrowserRooms.SingleAsync()).MeetingSessionId);
        Assert.Single(await f.Db.SalesRoomInvitationGrants.ToListAsync());
        var secret = link.Split("#invite=")[1]; Assert.DoesNotContain(secret, invitation.ProtectedBrowserInvitationLink!);
        Assert.All(await f.Db.CompanyOutboxMessages.ToListAsync(), message => Assert.DoesNotContain(secret, message.PayloadJson));
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(f.Db, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), new RoomOutbox(f.Db), browser.Service);
        var command = new SalesMeetingInvitationDeliveryRequestedMessage(f.CompanyId, invitation.Id, invitation.IdempotencyKey, null);
        await dispatcher.DispatchAsync(command, default); await dispatcher.DispatchAsync(command, default);
        Assert.Equal(1, f.Provider.CreateCalls); Assert.False(f.Provider.LastCreate!.CreateOnlineMeeting); Assert.Contains(link, f.Provider.LastCreate.Description);
        Assert.DoesNotContain("#", invitation.OnlineMeetingUrl!); Assert.Equal(link, (await browser.Service.GetLinkAsync(f.CompanyId, f.UserId, invitation.Id, default)).Url);
    }
    [Fact]
    public async Task Copy_link_is_organizer_and_company_scoped_and_room_creation_requires_approval()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); var browser = Browser(f);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => browser.Service.GetLinkAsync(f.CompanyId, Guid.NewGuid(), invitation.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => browser.Service.PrepareAsync(Guid.NewGuid(), invitation.Id, default));
        f.Db.ApprovalRequests.Remove(await f.Db.ApprovalRequests.SingleAsync()); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CalendarProviderException>(() => browser.Service.PrepareAsync(f.CompanyId, invitation.Id, default)); Assert.Empty(await f.Db.SalesBrowserRooms.ToListAsync());
    }
    [Fact]
    public async Task Ambiguous_calendar_create_is_inspected_without_sending_a_second_invitation()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); var browser = Browser(f); await ReadyBrowser(f, invitation, browser);
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(f.Db, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), new RoomOutbox(f.Db), browser.Service);
        var command = new SalesMeetingInvitationDeliveryRequestedMessage(f.CompanyId, invitation.Id, invitation.IdempotencyKey, null);
        f.Provider.FailAfterCreate = true; await dispatcher.DispatchAsync(command, default); Assert.Equal(SalesMeetingInvitationStatus.ReconciliationRequired, invitation.Status);
        await dispatcher.DispatchAsync(command, default); Assert.Equal(1, f.Provider.CreateCalls);
        await browser.Service.ReconcileAsync(f.CompanyId, f.UserId, invitation.Id, null, default);
        await dispatcher.DispatchAsync(command with { ReconcileOnly = true }, default);
        Assert.Equal(SalesMeetingInvitationStatus.Scheduled, invitation.Status); Assert.Equal(1, f.Provider.CreateCalls);
    }
    [Fact]
    public async Task Retryable_calendar_error_is_inspected_before_another_create()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); var browser = Browser(f); await ReadyBrowser(f, invitation, browser);
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(f.Db, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), new RoomOutbox(f.Db), browser.Service);
        var command = new SalesMeetingInvitationDeliveryRequestedMessage(f.CompanyId, invitation.Id, invitation.IdempotencyKey, null);
        f.Provider.FailureKind = CalendarProviderFailureKind.Retryable; f.Provider.FailAfterCreate = true;
        await Assert.ThrowsAsync<CalendarProviderException>(() => dispatcher.DispatchAsync(command, default));
        await dispatcher.DispatchAsync(command, default); Assert.Equal(1, f.Provider.CreateCalls); Assert.Equal(SalesMeetingInvitationStatus.Scheduled, invitation.Status);
    }
    [Fact]
    public async Task Reschedule_preserves_link_and_cancellation_revokes_access_before_provider_cleanup()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); var browser = Browser(f); await ReadyBrowser(f, invitation, browser);
        var link = await browser.Service.DeliveryLinkAsync(f.CompanyId, invitation.Id, default); invitation.MarkScheduled("event", null, null, "https://rooms.example.test/room", DateTime.UtcNow); await f.Db.SaveChangesAsync();
        var reschedule = await ApprovedChange(f, invitation, SalesMeetingChangeOperation.Reschedule);
        var dispatcher = new SalesMeetingChangeDeliveryDispatcher(f.Db, new StaticCalendarTokenLeaseService(), new CalendarProviderRegistry([f.Provider]), browser.Service);
        await dispatcher.DispatchAsync(new(f.CompanyId, reschedule.Id, reschedule.IdempotencyKey, null), default);
        Assert.Equal(SalesMeetingChangeRequestStatus.Completed, reschedule.Status); Assert.False(f.Provider.LastUpdate!.CreateOnlineMeeting); Assert.Contains(link, f.Provider.LastUpdate.Description);
        Assert.Equal(link, await browser.Service.DeliveryLinkAsync(f.CompanyId, invitation.Id, default));
        Assert.Equal(invitation.EndsUtc.AddHours(1), (await f.Db.SalesBrowserRooms.SingleAsync()).ExpiresUtc);
        var cancel = await ApprovedChange(f, invitation, SalesMeetingChangeOperation.Cancel);
        await dispatcher.DispatchAsync(new(f.CompanyId, cancel.Id, cancel.IdempotencyKey, null), default);
        await dispatcher.DispatchAsync(new(f.CompanyId, cancel.Id, cancel.IdempotencyKey, null), default);
        Assert.Equal(1, f.Provider.CancelCalls); Assert.Equal(SalesMeetingInvitationStatus.Cancelled, invitation.Status);
        Assert.Equal("ending", (await f.Db.SalesBrowserRooms.SingleAsync()).State);
        await Assert.ThrowsAsync<CalendarProviderException>(() => browser.Service.DeliveryLinkAsync(f.CompanyId, invitation.Id, default));
    }
    [Fact]
    public async Task Browser_reschedule_cannot_silently_switch_to_a_provider_meeting()
    {
        await using var f = await Fixture.CreateAsync(); var invitation = await f.CreateApprovedInvitationAsync("browser"); invitation.MarkScheduled("event", null, null, null, DateTime.UtcNow); await f.Db.SaveChangesAsync();
        var request = new CreateSalesMeetingRescheduleRequest(invitation.StartsUtc.AddHours(1), invitation.EndsUtc.AddHours(1), invitation.TimeZoneId, invitation.Title, invitation.Description, null, true, "google_meet");
        await Assert.ThrowsAsync<SalesValidationException>(() => f.Service.RequestRescheduleAsync(f.CompanyId, f.UserId, invitation.Id, request, default));
        Assert.Empty(await f.Db.SalesMeetingChangeRequests.ToListAsync());
    }
    [Fact]
    public async Task Failed_change_can_be_retried_only_by_organizer_with_its_existing_approval()
    {
        await using var f=await Fixture.CreateAsync();var invitation=await f.CreateApprovedInvitationAsync("browser");var browser=Browser(f);
        invitation.MarkScheduled("event",null,null,null,DateTime.UtcNow);var change=await ApprovedChange(f,invitation,SalesMeetingChangeOperation.Cancel);
        change.MarkFailed("calendar_authorization_required","Reconnect the calendar.");await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<SalesRoomAccessException>(()=>browser.Service.RetryChangeAsync(f.CompanyId,Guid.NewGuid(),invitation.Id,change.Id,default));
        await browser.Service.RetryChangeAsync(f.CompanyId,f.UserId,invitation.Id,change.Id,default);
        Assert.Equal(SalesMeetingChangeRequestStatus.Queued,change.Status);
        Assert.Single(await f.Db.CompanyOutboxMessages.Where(x=>x.Topic==CompanyOutboxTopics.SalesMeetingChangeDeliveryRequested).ToListAsync());
        await Assert.ThrowsAsync<SalesRoomAccessException>(()=>browser.Service.RetryChangeAsync(f.CompanyId,f.UserId,invitation.Id,change.Id,default));
    }
    private static async Task<SalesMeetingChangeRequest> ApprovedChange(Fixture f, SalesMeetingInvitation invitation, SalesMeetingChangeOperation operation)
    {
        var change = new SalesMeetingChangeRequest(Guid.NewGuid(), f.CompanyId, invitation.Id, operation, f.UserId,
            operation == SalesMeetingChangeOperation.Reschedule ? invitation.StartsUtc.AddDays(1) : null, operation == SalesMeetingChangeOperation.Reschedule ? invitation.EndsUtc.AddDays(1) : null,
            operation == SalesMeetingChangeOperation.Reschedule ? invitation.TimeZoneId : null, operation == SalesMeetingChangeOperation.Reschedule ? invitation.Title : null,
            operation == SalesMeetingChangeOperation.Reschedule ? invitation.Description : null, null, false);
        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), f.CompanyId, ApprovalTargetEntityType.SalesMeetingChangeRequest, change.Id, "user", f.UserId,
            operation == SalesMeetingChangeOperation.Reschedule ? SalesMeetingApprovalTypes.RescheduleInvitation : SalesMeetingApprovalTypes.CancelInvitation, new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { { "operation", System.Text.Json.Nodes.JsonValue.Create(operation.ToString()) } }, "owner", null, []);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, f.UserId, null); change.SubmitForApproval(approval.Id); change.MarkApproved(f.UserId, DateTime.UtcNow);
        f.Db.ApprovalRequests.Add(approval); f.Db.SalesMeetingChangeRequests.Add(change); await f.Db.SaveChangesAsync(); return change;
    }
}
