using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class PresentationRunWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Applying_a_preset_sends_only_explicit_overrides_and_uses_invitation_when_no_session(bool hasSession)
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var handler=new Handler();
        var transport=new CompanyApiTransport(new HttpClient(handler){BaseAddress=new Uri("http://localhost/")});
        context.Services.AddSingleton(sp=>new SalesPresentationPresetApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(sp=>new SalesPresentationRunApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        var cut=context.RenderComponent<PresentationRunWorkflow>(p=>p
            .Add(x=>x.CompanyId,handler.Company).Add(x=>x.InvitationId,handler.Invitation)
            .Add(x=>x.SessionId,hasSession?handler.Session:Guid.Empty));
        cut.WaitForElement("#meeting-preset").Change(handler.Preset.Id.ToString());
        cut.WaitForElement(".run-defaults");
        Assert.Contains("Inherited from the preset",cut.Markup);
        Assert.False(cut.Find(".run-overrides").HasAttribute("open"));
        cut.FindAll("button").Single(x=>x.TextContent=="Use preset").Click();
        cut.WaitForAssertion(()=>Assert.NotNull(handler.Request));
        Assert.Null(handler.Request!.Goal);Assert.Null(handler.Request.Audience);
        Assert.Null(handler.Request.DurationMinutes);Assert.Null(handler.Request.ControlMode);
        Assert.Contains(hasSession?"meeting-sessions/":"meeting-invitations/",handler.PostPath);
        cut.WaitForAssertion(()=>Assert.Contains("Pinned to version 1",cut.Markup));
    }

    [Fact]
    public void Meeting_selector_has_no_authoring_fields_and_applies_only_the_preset()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var handler=new Handler();
        var transport=new CompanyApiTransport(new HttpClient(handler){BaseAddress=new Uri("http://localhost/")});
        context.Services.AddSingleton(sp=>new SalesPresentationPresetApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(sp=>new SalesPresentationRunApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        var cut=context.RenderComponent<SalesMeetingPresetSelector>(p=>p
            .Add(x=>x.CompanyId,handler.Company).Add(x=>x.InvitationId,handler.Invitation));
        cut.WaitForElement("#launch-preset").Change(handler.Preset.Id.ToString());
        cut.WaitForElement(".selector-summary");
        Assert.Single(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("input,textarea,audio,details"));
        Assert.DoesNotContain("Save a reusable copy",cut.Markup);
        cut.FindAll("button").Single(x=>x.TextContent=="Use preset").Click();
        cut.WaitForAssertion(()=>Assert.NotNull(handler.Request));
        Assert.Null(handler.Request!.PresenterAgentId); Assert.Null(handler.Request.Goal);
        Assert.Null(handler.Request.Audience); Assert.Null(handler.Request.DurationMinutes);
        Assert.Null(handler.Request.DemoScenario); Assert.Null(handler.Request.Language);
        Assert.Null(handler.Request.ControlMode);
        Assert.Contains("meeting-invitations/",handler.PostPath);
        cut.WaitForAssertion(()=>Assert.Contains("Applied to this meeting",cut.Markup));
    }

    [Theory]
    [InlineData("presenting","already started")]
    [InlineData("completed","has ended")]
    [InlineData("cancelled","has ended")]
    public void Started_or_ended_meeting_does_not_offer_preset_mutation(string status,string message)
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var handler=new Handler();
        var transport=new CompanyApiTransport(new HttpClient(handler){BaseAddress=new Uri("http://localhost/")});
        context.Services.AddSingleton(sp=>new SalesPresentationPresetApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        context.Services.AddSingleton(sp=>new SalesPresentationRunApiClient(transport,false,sp.GetRequiredService<IApiProblemMessageResolver>()));
        var cut=context.RenderComponent<SalesMeetingPresetSelector>(p=>p.Add(x=>x.CompanyId,handler.Company)
            .Add(x=>x.InvitationId,handler.Invitation).Add(x=>x.SessionId,handler.Session).Add(x=>x.SessionStatus,status));
        Assert.True(cut.WaitForElement("#launch-preset").HasAttribute("disabled"));
        Assert.Contains(message,cut.Markup);
        Assert.DoesNotContain("Prepare the presentation",cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"),x=>x.TextContent is "Use preset" or "Replace presentation");
        Assert.Null(handler.Request);
    }

    private sealed class Handler:HttpMessageHandler
    {
        public Guid Company{get;}=Guid.NewGuid();
        public Guid Invitation{get;}=Guid.NewGuid();
        public Guid Session{get;}=Guid.NewGuid();
        public SalesPresentationPresetViewModel Preset{get;}
        public ApplySalesPresentationPresetRequest? Request{get;private set;}
        public string PostPath{get;private set;}="";
        public Handler()
        {
            var now=DateTime.UtcNow;
            var version=new SalesPresentationPresetVersionViewModel(Guid.NewGuid(),1,"published",Guid.NewGuid(),null,"Reusable goal","Business leaders",30,null,"assisted","en",["sales_meeting"],null,Guid.NewGuid(),now,now,now,1,null);
            Preset=new(Guid.NewGuid(),Company,"Executive overview",null,Guid.NewGuid(),"published",version.Id,now,now,null,1,0,null,version,[version],["use"]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Post)
            {
                PostPath=path;Request=await request.Content!.ReadFromJsonAsync<ApplySalesPresentationPresetRequest>(cancellationToken:ct);
                var v=Preset.CurrentPublished!;
                return Json(new SalesPresentationRunViewModel(Guid.NewGuid(),Session,Preset.Id,Preset.Name,v.Id,1,v.DefaultPresenterAgentId!.Value,"Alex",v.Goal,v.Audience,30,null,"en","assisted",false,false,false,false,false,"ready",[],false,null,null,Guid.NewGuid(),true,null,1,[]));
            }
            if(path.EndsWith("/presentation-run"))return new(HttpStatusCode.NotFound);
            if(path.EndsWith("/presentation-presets"))return Json(new[]{new SalesPresentationPresetListItemViewModel(Preset.Id,Preset.Name,null,Preset.OwnerUserId,"published",1,null,"processed",1,0,DateTime.UtcNow,1)});
            return Json(Preset);
        }
        private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    }
}
