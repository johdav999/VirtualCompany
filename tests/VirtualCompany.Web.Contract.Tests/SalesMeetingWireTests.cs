using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class SalesMeetingWireTests
{
    [Fact]
    public async Task Sales_client_reads_existing_lead_booking_on_pipeline_and_opportunity()
    {
        using var factory = new TestWebApplicationFactory();
        var company = Guid.NewGuid();
        var user = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var deal = Guid.NewGuid();
        var calendar = Guid.NewGuid();
        var external = Guid.NewGuid();
        var invitation = Guid.NewGuid();
        var room = Guid.NewGuid();
        var starts = new DateTime(2026, 10, 1, 7, 49, 0, DateTimeKind.Utc);
        await factory.SeedAsync(db =>
        {
            db.Users.Add(new User(user, "meeting-wire@example.test", "Meeting wire", "dev-header", "meeting-wire"));
            db.Companies.Add(new Company(company, "Meeting wire company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Leads.Add(new Lead(lead, company, "Meeting buyer", SalesPipelineStage.NewStageId));
            db.Deals.Add(new Deal(deal, company, "Converted opportunity", SalesPipelineStage.QualifiedStageId,
                1000, "USD", sourceLeadId: lead));
            db.ExternalAccountConnections.Add(new ExternalAccountConnection(external, company, user,
                ExternalAccountProvider.Google, "organizer@example.test", "Wire calendar", null, "meeting-wire"));
            db.CalendarConnections.Add(new CalendarConnection(calendar, company, user, external,
                ExternalAccountProvider.Google, "organizer@example.test", "Wire calendar"));
            var meeting = new SalesMeetingInvitation(invitation, company, lead, null, null, calendar,
                ExternalAccountProvider.Google, "organizer@example.test", "buyer@example.test", null,
                "Existing buyer demo", "Recorded booking", starts, starts.AddHours(1), "Europe/Stockholm", null, false, user);
            meeting.SelectConferencing(SalesMeetingConferencing.Browser);
            meeting.BindBrowserRoom(room,"fixture-protected-link");
            meeting.MarkScheduled("fixture-event", null, null, null, starts.AddDays(-1));
            db.SalesMeetingInvitations.Add(meeting);
            return Task.CompletedTask;
        });
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "meeting-wire");
        http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "meeting-wire@example.test");
        var client = new SalesApiClient(http);
        var pipeline = await client.GetPipelineAsync(company);
        var opportunity = await client.GetDealAsync(company, deal);
        var card = Assert.Single(pipeline.Stages.SelectMany(x => x.Deals));
        var meetingOnCard = Assert.Single(card.Meetings!);
        var meetingOnRecord = Assert.Single(opportunity!.Meetings!);
        Assert.Equal(meetingOnCard, meetingOnRecord);
        Assert.Equal(invitation, meetingOnCard.Id);
        Assert.Equal(lead, meetingOnCard.LeadId);
        Assert.Null(meetingOnCard.DealId);
        Assert.Equal("Existing buyer demo", meetingOnCard.Title);
        Assert.Equal(starts, meetingOnCard.StartsUtc);
        Assert.Equal(starts.AddHours(1), meetingOnCard.EndsUtc);
        Assert.Equal("Europe/Stockholm", meetingOnCard.TimeZoneId);
        Assert.Equal("scheduled", meetingOnCard.Status);
        Assert.Equal(room,meetingOnCard.BrowserRoomId);
    }
}
