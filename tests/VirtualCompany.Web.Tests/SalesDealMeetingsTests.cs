using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Web;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;
public sealed class SalesDealMeetingsTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static SalesDealMeetingResponse Meeting(string status = "scheduled", Guid? room = null) => new(Guid.NewGuid(), Lead, null,
        "Booked lead demo", new(2026,10,1,7,0,0,DateTimeKind.Utc), new(2026,10,1,8,0,0,DateTimeKind.Utc), "UTC", status, room);

    [Fact]
    public void Pipeline_summary_shows_booking_and_time_without_nested_links()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = context.RenderComponent<SalesDealMeetings>(p => p.Add(x=>x.Compact,true)
            .Add(x=>x.Meetings,new[]{Meeting(room:Guid.NewGuid()),Meeting("cancelled")}));
        var summary=cut.Find("[data-testid='pipeline-meeting']");
        Assert.Contains("Invitation scheduled",summary.TextContent);
        Assert.Contains("Booked lead demo",summary.TextContent);
        Assert.Contains("2026",summary.TextContent);
        Assert.Contains("07:00",summary.TextContent);
        Assert.Contains("UTC",summary.TextContent);
        Assert.Contains("+1 other invitations",summary.TextContent);
        Assert.Empty(cut.FindAll("a"));
    }

    [Theory]
    [InlineData("scheduled",false,2)]
    [InlineData("scheduled",true,3)]
    [InlineData("cancelled",false,1)]
    [InlineData("cancelled",true,1)]
    [InlineData("failed",false,1)]
    [InlineData("failed",true,1)]
    public void Deal_bookings_preserve_returns_and_only_scheduled_bookings_with_a_room_offer_host_access(string status,bool hasRoom,int links)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var origin=DashboardRoutes.BuildTodayPath(Company,"sales");
        var location=DashboardRoutes.EnsureWorkspaceContext("/app/sales/deals/11111111-1111-1111-1111-111111111111",Company,origin);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(location);
        var room=hasRoom?Guid.NewGuid():(Guid?)null;
        var meeting=Meeting(status,room);
        var cut=context.RenderComponent<SalesDealMeetings>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Meetings,new[]{meeting}));
        Assert.Contains(meeting.Title,cut.Markup);
        Assert.Equal(links,cut.FindAll("a").Count);
        foreach(var link in cut.FindAll("a"))
        {
            var href=link.GetAttribute("href")!;
            var query=HttpUtility.ParseQueryString(new Uri("http://localhost"+href).Query);
            Assert.Equal(Company.ToString("D"),query["companyId"]);
            Assert.Equal(origin,query["returnUrl"]);
            Assert.Equal(location,query["recordReturnUrl"]);
            Assert.Equal(location,SalesJourneyRoutes.Back("http://localhost"+href,Company));
        }
        Assert.StartsWith($"/app/sales/leads/{Lead:D}",cut.Find("a").GetAttribute("href"));
        if(status=="scheduled"&&hasRoom)Assert.StartsWith($"/app/sales/rooms/{room:D}",cut.Find("a.deal-meeting-host").GetAttribute("href"));
        else Assert.Empty(cut.FindAll("a.deal-meeting-host"));
        if(status!="scheduled")Assert.DoesNotContain("Invitation scheduled",cut.Markup);
    }

    [Theory]
    [InlineData("Europe/Stockholm", "09:00", "Europe/Stockholm")]
    [InlineData("Invalid/Fixture", "07:00", "UTC")]
    public void Booking_time_uses_its_recorded_zone_or_an_explicit_utc_fallback(string zone, string time, string shownZone)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var meeting = Meeting() with { TimeZoneId = zone };
        var cut = context.RenderComponent<SalesDealMeetings>(p => p.Add(x => x.Meetings, new[] { meeting }));
        var label = cut.Find("time").TextContent;
        Assert.Contains(time, label);
        Assert.Contains(shownZone, label);
        if (zone == "Invalid/Fixture") Assert.DoesNotContain(zone, label);
    }

    [Fact]
    public void Deal_without_bookings_shows_no_booking_claim()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut=context.RenderComponent<SalesDealMeetings>();
        Assert.Empty(cut.FindAll("[data-testid='deal-meetings'],[data-testid='pipeline-meeting']"));
    }
}
