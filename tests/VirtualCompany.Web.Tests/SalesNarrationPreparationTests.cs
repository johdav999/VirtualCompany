using VirtualCompany.Api.Tests;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesNarrationPreparationTests
{
    private readonly Guid company = Guid.NewGuid(), session = Guid.NewGuid();
    [Fact]
    public void Draft_requires_explicit_review_and_edited_text_cannot_approve_old_version()
    {
        using var context = Context("draft");
        var cut = context.RenderComponent<SalesNarrationPreparation>(p => p.Add(x=>x.CompanyId,company).Add(x=>x.SessionId,session));
        cut.WaitForAssertion(()=>Assert.Contains("Approve and generate",cut.Markup));
        var approve = cut.FindAll("button").Single(x=>x.TextContent=="Approve and generate");
        Assert.True(approve.HasAttribute("disabled"));
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("textarea").Change("A revised customer-safe script.");
        cut.FindAll("button").Single(x=>x.TextContent=="Approve and generate").Click();
        Assert.Contains("Save your edits as a new revision",cut.Markup);
        Export("narration-draft",cut.Markup);
    }
    [Fact]
    public void Ready_preview_uses_scoped_HTTP_audio_and_has_separate_usage()
    {
        using var context = Context("ready");
        var cut = context.RenderComponent<SalesNarrationPreparation>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.SessionId,session));
        cut.WaitForAssertion(()=>Assert.Single(cut.FindAll("audio")));
        var audio = cut.Find("audio");
        Assert.Contains(company.ToString(),audio.GetAttribute("src"));
        Assert.Equal("none",audio.GetAttribute("preload"));
        Assert.Contains("Preview delivery",cut.Markup);
        Assert.Contains("Generated",cut.Markup);
        Assert.DoesNotContain("data:audio",cut.Markup);
        Export("narration-ready",cut.Markup);
    }
    [Fact]
    public void Revoked_audio_cannot_render_a_player()
    {
        using var context = Context("revoked");
        var cut = context.RenderComponent<SalesNarrationPreparation>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.SessionId,session));
        cut.WaitForAssertion(()=>Assert.Contains("Revoked",cut.Markup));
        Assert.Empty(cut.FindAll("audio"));
    }
    [Fact]
    public void Permission_failure_has_actionable_feedback()
    {
        using var context = Context("forbidden");
        var cut = context.RenderComponent<SalesNarrationPreparation>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.SessionId,session));
        cut.WaitForAssertion(()=>Assert.Contains("Check your access",cut.Markup));
        Assert.Empty(cut.FindAll("audio"));
    }
    private TestContext Context(string status)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var segment = new NarrationSegment(Guid.NewGuid(),1,1,"Welcome to this fictional product presentation.",
            "Welcome to this fictional product presentation.",status=="ready"?"ready":"pending",false,4800,230444,1,null);
        var revision = new NarrationRevision(Guid.NewGuid(),Guid.NewGuid(),2,"en","marin","model",Guid.NewGuid(),status,1,
            new DateTime(2026,9,10,12,0,0,DateTimeKind.Utc),status=="draft"?null:DateTime.UtcNow,[segment],120,180,0,.08,.16,.0048m);
        var http = new HttpClient(new Handler(request=>{
            Assert.Equal(company.ToString(), Assert.Single(request.Headers.GetValues("X-Company-Id")));
            return status=="forbidden" ? new(HttpStatusCode.Forbidden) :
                new(HttpStatusCode.OK) { Content=JsonContent.Create(new NarrationWorkspace(new(true,"model","marin","v1"),[revision])) };
        })) { BaseAddress=new("https://api.example.test/") };
        context.Services.AddSingleton(new SalesNarrationApiClient(new CompanyApiTransport(http)));
        return context;
    }
    private static void Export(string name,string markup)
    {
        var root=Environment.GetEnvironmentVariable("VC_BROWSER_UAT_DIRECTORY");
        if (root is not null) { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root,name+".html"),markup); }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(handler(request)); }
}



