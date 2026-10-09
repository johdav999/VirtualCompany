using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

internal static class DecisionReviewFixture
{
    public static readonly Dictionary<string, Guid> Approvals = [];
    public static Guid InvitationId, HumanId, CompanyId;
    public static readonly RecordingProvider Provider = new();
    public static async Task InitializeAsync(TestWebApplicationFactory factory, Guid company, Guid human)
    {
        CompanyId = company; HumanId = human;
        var tasks = new Dictionary<string, Guid>();
        await factory.SeedAsync(async db =>
        {
            foreach (var name in new[] { "approve", "reject", "changes", "stale", "expired" })
            {
                var task = new WorkTask(Guid.NewGuid(), company, "sales_review", "P12 " + name + " renewal terms",
                    "Synthetic acceptance record: review the proposed discount before continuing work.", WorkTaskPriority.High, null, null, "user", human,
                    new Dictionary<string, JsonNode?>() { ["discount"] = JsonValue.Create("8%") });
                tasks[name] = task.Id; db.Add(task);
            }
            var original = await db.SalesMeetingInvitations.IgnoreQueryFilters().SingleAsync(x => x.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            var invitation = new SalesMeetingInvitation(Guid.NewGuid(), company, original.LeadId, original.DealId, original.ContactId,
                original.CalendarConnectionId, ExternalAccountProvider.Google, original.OrganizerEmail, "p12-controlled-recipient@example.invalid",
                "P12 controlled recipient", "P12 controlled adapter invitation", "Synthetic test adapter only; no real calendar service is contacted.",
                DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddMinutes(30), "Europe/Stockholm", null, false, human);
            InvitationId = invitation.Id; db.Add(invitation);
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p01-owner");
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p01-owner@example.com");
        foreach (var task in tasks)
        {
            var response = await client.PostAsync($"/api/companies/{company}/approvals", JsonContent.Create(new CreateApprovalRequestCommand("task", task.Value,
                "user", human, "renewal_terms", new() { ["reason"] = JsonValue.Create("Review the revised customer discount."),
                    ["before"] = new JsonObject { ["Discount"] = "12%" }, ["proposed"] = new JsonObject { ["Discount"] = "8%" },
                    ["expiresUtc"] = JsonValue.Create(task.Key == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(1)) }, RequiredUserId: human)));
            response.EnsureSuccessStatusCode(); Approvals[task.Key] = (await response.Content.ReadFromJsonAsync<ApprovalRequestDto>())!.Id;
        }
        var delivery = await client.PostAsync($"/api/companies/{company}/approvals", JsonContent.Create(new CreateApprovalRequestCommand("sales_meeting_invitation",
            InvitationId, "user", human, "sales_meeting_invitation_send", new() { ["reason"] = JsonValue.Create("Review the controlled adapter recipient and invitation.") }, RequiredUserId: human)));
        delivery.EnsureSuccessStatusCode(); Approvals["delivery"] = (await delivery.Content.ReadFromJsonAsync<ApprovalRequestDto>())!.Id;
        await factory.SeedAsync(async db => (await db.SalesMeetingInvitations.IgnoreQueryFilters().SingleAsync(x => x.Id == InvitationId)).SubmitForApproval(Approvals["delivery"]));
    }
    public static async Task<object> DeliverAsync(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyContext(new(Guid.NewGuid(), CompanyId, HumanId,
            "P12 acceptance company", CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var invitation = await db.SalesMeetingInvitations.SingleAsync(x => x.CompanyId == CompanyId && x.Id == InvitationId);
        var dispatcher = new SalesMeetingInvitationDeliveryDispatcher(db, new TokenLease(), new CalendarProviderRegistry([Provider]),
            scope.ServiceProvider.GetRequiredService<ICompanyOutboxEnqueuer>());
        var message = new SalesMeetingInvitationDeliveryRequestedMessage(CompanyId, InvitationId, invitation.IdempotencyKey, Approvals["delivery"].ToString("N"));
        await dispatcher.DispatchAsync(message, CancellationToken.None); await dispatcher.DispatchAsync(message, CancellationToken.None);
        return new { adapter = "synthetic controlled calendar; no real provider delivery", calls = Provider.Calls, status = invitation.Status.ToStorageValue(), invitation.ExternalEventId };
    }
    public static async Task EditAsync(TestWebApplicationFactory factory)
    {
        await factory.SeedAsync(async db =>
        {
            var approval = await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(x => x.Id == Approvals["stale"] && x.CompanyId == CompanyId);
            var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == approval.TargetEntityId && x.CompanyId == CompanyId);
            task.InputPayload["discount"] = JsonValue.Create("10%");
        });
    }
    private sealed class TokenLease : ICalendarOAuthAccessTokenLeaseService
    {
        public Task<CalendarOAuthAccessTokenLease> AcquireAsync(Guid companyId, Guid calendarConnectionId, IReadOnlyCollection<string> scopes, CancellationToken ct) =>
            Task.FromResult(new CalendarOAuthAccessTokenLease(calendarConnectionId, calendarConnectionId, companyId, ExternalAccountProvider.Google,
                "p12-adapter@example.invalid", "synthetic-local-adapter-token", DateTime.UtcNow.AddHours(1), scopes, "primary"));
    }
    public sealed class RecordingProvider : ICalendarProviderClient
    {
        public int Calls; public ExternalAccountProvider Provider => ExternalAccountProvider.Google;
        public IReadOnlyCollection<string> RequiredScopes => [];
        public Task<IReadOnlyList<CalendarBusyWindow>> GetBusyWindowsAsync(CalendarProviderContext context, DateTime from, DateTime to, string zone, CancellationToken ct) => Task.FromResult<IReadOnlyList<CalendarBusyWindow>>([]);
        public Task<CalendarMeetingCreateResult> CreateMeetingAsync(CalendarProviderContext context, CalendarMeetingCreateRequest request, CancellationToken ct)
        { if (request.AttendeeEmail != "p12-controlled-recipient@example.invalid") throw new InvalidOperationException("Fixture recipient guard."); Calls++; return Task.FromResult(new CalendarMeetingCreateResult("p12-controlled-event", null, null, null)); }
        public Task<CalendarMeetingCreateResult> UpdateMeetingAsync(CalendarProviderContext context, CalendarMeetingUpdateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task CancelMeetingAsync(CalendarProviderContext context, string eventId, string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
    }
}
