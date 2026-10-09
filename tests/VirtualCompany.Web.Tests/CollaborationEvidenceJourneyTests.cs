using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
using VirtualCompany.Api.Tests;

namespace VirtualCompany.Web.Tests;

public sealed class CollaborationEvidenceJourneyTests
{
    private static readonly Guid Company=Guid.NewGuid(), Work=Guid.NewGuid(), Input=Guid.NewGuid(), Output=Guid.NewGuid();
    [Fact]
    public void Flow_list_and_artifact_panel_share_durable_versions_and_keep_context()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport(HttpStatusCode.OK, Evidence())));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agents/work/task/{Work}/collaboration?companyId={Company}&artifactId={Output}&boardReturnUrl="+Uri.EscapeDataString($"/agents/staff?companyId={Company}&objective=renewal&state=awaiting_approval"));
        var cut=context.RenderComponent<AgentCollaboration>(p=>p.Add(x=>x.Kind,"task").Add(x=>x.Id,Work));
        cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll("[data-artifact-id]").Count));
        Assert.Contains("Revised proposal",cut.Markup); Assert.Contains("Laura, version 1",cut.Markup);
        Assert.Contains("Business rationale",cut.Markup); Assert.Contains("No review conclusion",cut.Markup);
        Assert.Contains("objective",cut.Find("nav a:nth-child(2)").GetAttribute("href"));
        var ids=cut.FindAll("[data-artifact-id]").Select(x=>x.GetAttribute("data-artifact-id")).ToArray();
        cut.Find(".collaboration-switch button:nth-child(2)").Click();
        cut.WaitForAssertion(()=>Assert.Single(cut.FindAll(".collaboration-list")));
        Assert.Equal(ids,cut.FindAll("[data-artifact-id]").Select(x=>x.GetAttribute("data-artifact-id")));
        Assert.Contains("Revised proposal",cut.Find("aside").TextContent);
        cut.Find(".handoff button").Click();
        cut.WaitForAssertion(()=>Assert.Contains("Margin output",cut.Find("aside").TextContent));
        Assert.Contains("artifactId",context.Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] [InlineData(HttpStatusCode.NotFound)]
    public async Task Revoked_or_foreign_evidence_returns_unavailable(HttpStatusCode status) =>
        Assert.Null(await new AgentWorkApiClient(new Transport(status)).CollaborationAsync(Company,"task",Work));
    [Fact]
    public async Task Malformed_mismatched_and_offline_evidence_fail_without_fabricated_contributions()
    {
        await Assert.ThrowsAsync<OnboardingApiException>(()=>new AgentWorkApiClient(new Transport(HttpStatusCode.OK,"bad JSON")).CollaborationAsync(Company,"task",Work));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>new AgentWorkApiClient(new Transport(HttpStatusCode.OK,Evidence() with {CompanyId=Guid.NewGuid()})).CollaborationAsync(Company,"task",Work));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>new AgentWorkApiClient(new Transport(HttpStatusCode.OK),true).CollaborationAsync(Company,"task",Work));
    }
    private static CollaborationEvidenceDto Evidence() => new(Company,"task",Work,"Renewal review","awaiting_approval","Owner","Review terms",DateTime.UtcNow,
        [new(Input,Work,Guid.NewGuid(),1,1,new(Guid.NewGuid(),"Laura","Finance"),"contributor","parallel","Margin check","completed","Margin output","Recorded floor",null,DateTime.UtcNow,[],[]),
         new(Output,Work,Guid.NewGuid(),2,2,new(Guid.NewGuid(),"Alex","Sales"),"contributor","sequential_handoff","Proposal revision","needs_review","Revised proposal","Uses the margin input",null,DateTime.UtcNow,[Input],[])],
        [new(Guid.NewGuid(),Input,Output,true,null,DateTime.UtcNow)],[],false,false,[]);
    private sealed class Transport(HttpStatusCode status, object? body=null):ICompanyApiTransport
    {
        public Uri? BaseAddress=>new("http://localhost/");
        public Task<HttpResponseMessage> SendAsync(Guid companyId,HttpMethod method,string uri,HttpContent? content,CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status){Content=body is string text?new StringContent(text):JsonContent.Create(body)});
    }
}
