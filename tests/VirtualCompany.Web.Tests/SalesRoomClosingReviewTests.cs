using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class SalesRoomClosingReviewTests
{
    private readonly Guid company=Guid.NewGuid(), room=Guid.NewGuid(), session=Guid.NewGuid();
    [Fact]
    public void Partial_capture_does_not_automatically_generate_or_send()
    {
        using var c=Context(out var requests);
        var cut=c.RenderComponent<SalesRoomClosingReview>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RoomId,room));
        cut.WaitForAssertion(()=>Assert.Contains("Partial capture",cut.Markup));
        Assert.Contains("Please send the implementation plan.",cut.Markup);
        Assert.All(requests,r=>Assert.Equal(HttpMethod.Get,r.Method));
        Export("closing-partial",cut.Markup);
    }
    [Fact]
    public void Permission_failure_has_retry_and_no_evidence()
    {
        using var c=Context(out _,forbidden:true);
        var cut=c.RenderComponent<SalesRoomClosingReview>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RoomId,room));
        cut.WaitForAssertion(()=>Assert.Contains("Retry loading review",cut.Markup));
        Assert.NotEmpty(cut.FindAll("[role=alert]"));
        Assert.DoesNotContain("Please send",cut.Markup);
    }
    [Fact]
    public void Live_call_cannot_prepare_closing()
    {
        using var c=Context(out _,state:"live");
        var cut=c.RenderComponent<SalesRoomClosingReview>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RoomId,room));
        cut.WaitForAssertion(()=>Assert.Contains("End the call",cut.Markup));
        Assert.True(cut.FindAll("button").Single(x=>x.TextContent=="Prepare closing draft").HasAttribute("disabled"));
    }
    [Fact]
    public void Saving_excerpt_review_posts_explicit_selection()
    {
        using var c=Context(out var requests);
        var cut=c.RenderComponent<SalesRoomClosingReview>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RoomId,room));
        cut.WaitForAssertion(()=>Assert.Single(cut.FindAll("input[type=checkbox]")));
        cut.Find("input[type=checkbox]").Change(true);
        cut.FindAll("button").Single(x=>x.TextContent=="Save excerpt review").Click();
        cut.WaitForAssertion(()=>Assert.Contains(requests,x=>x.Method==HttpMethod.Post && x.Uri.Contains("autosave")));
        Assert.Contains("\"reviewState\":\"reviewed\"",requests.Single(x=>x.Method==HttpMethod.Post).Body);
        Assert.DoesNotContain(requests,x=>x.Uri.Contains("execute"));
    }
    [Fact]
    public void Draft_separates_private_notes_and_requires_delivery_approval()
    {
        using var c=Context(out _,closing:true);
        var cut=c.RenderComponent<SalesRoomClosingReview>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RoomId,room));
        cut.WaitForAssertion(()=>Assert.Contains("Customer minutes",cut.Markup));
        Assert.Contains("private to your company",cut.Find("details").TextContent);
        Assert.True(cut.FindAll("button").Single(x=>x.TextContent=="Create delivery proposal").HasAttribute("disabled"));
        Export("closing-draft",cut.Markup);
    }
    private TestContext Context(out List<Request> requests,bool forbidden=false,string state="ended",bool closing=false)
    {
        var captured=new List<Request>();requests=captured;
        var now=new DateTime(2026,9,10,12,0,0,DateTimeKind.Utc);var agent=Guid.NewGuid();var minutesId=Guid.NewGuid();
        var segment=new SalesMeetingTranscriptSegmentViewModel(Guid.NewGuid(),Guid.NewGuid(),1,"customer","Casey","browser_room",
            "Please send the implementation plan.",now,now.AddSeconds(3),null,"unreviewed",now,1);
        var minutes=new SalesMeetingMinutesViewModel(minutesId,session,null,1,"draft",1,now,agent,null,"v1","v1",now.AddDays(365),
            null,null,now,1,false,null,null,[new(Guid.NewGuid(),0,"action","Send the implementation plan.",null,null,"transcript:"+segment.Id.ToString("N"),null,true)]);
        var internalNotes=new SalesMeetingInternalIntelligenceViewModel(Guid.NewGuid(),session,minutesId,1,"draft",1,now,agent,null,"v1","v1",
            now.AddDays(365),null,null,now,1,false,null,null,[]);
        var review=new BrowserRoomCaptureReview(room,session,agent,state,1,1,null,now.AddDays(365),"partial",[segment],
            closing?new(minutes,internalNotes):null,Guid.NewGuid());
        var http=new HttpClient(new Handler(async request=>{
            captured.Add(new(request.Method,request.RequestUri!.ToString(),request.Content is null?"":await request.Content.ReadAsStringAsync()));
            if(forbidden)return new(HttpStatusCode.Forbidden);
            object response=request.RequestUri.AbsolutePath.EndsWith("change-proposals")?Array.Empty<SalesMeetingChangeProposalViewModel>():
                request.RequestUri.AbsolutePath.EndsWith("autosave")?new SalesMeetingCaptureSaveResultViewModel("accepted",
                    new(session,2,Guid.NewGuid(),now,[segment],[],[],[])):review;
            return new(HttpStatusCode.OK){Content=JsonContent.Create(response,response.GetType())};
        })){BaseAddress=new("https://api.example.test/")};
        var transport=new CompanyApiTransport(http);
        var c=new TestContext().AddVirtualCompanyWebPresentationServices();
        c.Services.AddSingleton(new SalesBrowserRoomApiClient(transport,false));
        c.Services.AddSingleton(new SalesMeetingCaptureApiClient(transport,false));
        c.Services.AddSingleton(new SalesMeetingClosingApiClient(transport,false));
        c.Services.AddSingleton(new SalesMeetingChangeProposalApiClient(transport,false));
        return c;
    }
    public sealed record Request(HttpMethod Method,string Uri,string Body);
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r);}
    private static void Export(string name,string markup)
    {
        var path=Environment.GetEnvironmentVariable("VC_BROWSER_UAT_DIRECTORY");
        if(path is not null){Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,name+".html"),markup);}
    }
}
