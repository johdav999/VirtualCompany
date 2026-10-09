using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Work;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
using VirtualCompany.Api.Tests;

namespace VirtualCompany.Web.Tests;

public sealed class AgentSupervisionJourneyTests
{
    [Fact]
    public void Bottleneck_rows_have_exact_work_and_decision_routes_with_filtered_returns_and_unavailable_time()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var report=Report(company);
        context.Services.AddSingleton(new AgentSupervisionApiClient(new Transport(_=>Task.FromResult(Reply(report)))));
        var nav=context.Services.GetRequiredService<NavigationManager>();nav.NavigateTo(Location(company));
        var cut=context.RenderComponent<AgentSupervisionReports>();cut.WaitForAssertion(()=>Assert.Contains("Review pending",cut.Markup));
        Assert.Contains("Unavailable",cut.Markup);Assert.Contains("company timezone Europe/Stockholm",cut.Markup);
        Assert.Contains("Current work status is observed now",cut.Markup);
        var work=cut.FindAll("a").Single(x=>x.TextContent=="Open exact work").GetAttribute("href")!;
        var decision=cut.FindAll("a").Single(x=>x.TextContent=="Review decision and history").GetAttribute("href")!;
        Assert.Contains(report.Rows.Single().WorkId.ToString(),work);Assert.Contains("supervisionReturnUrl",work);
        Assert.Contains("/work?",decision);Assert.Contains("tab=approvals",decision);Assert.Contains(report.Rows.Single().ApprovalId!.Value.ToString(),decision);
        Assert.Contains("supervisionReturnUrl",decision);Assert.Contains("finance_review",Uri.UnescapeDataString(decision));
        var contribution=cut.Find("a[href*=artifactId]").GetAttribute("href")!;Assert.Contains(report.Rows.Single().Evidence.Last().Id.ToString(),contribution);Assert.Contains("/collaboration?",contribution);Assert.Contains("supervisionReturnUrl",contribution);
    }
    [Fact]
    public void Applying_native_date_responsibility_type_and_agent_filters_keeps_company_scope()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var report=Report(company);
        context.Services.AddSingleton(new AgentSupervisionApiClient(new Transport(_=>Task.FromResult(Reply(report)))));
        var nav=context.Services.GetRequiredService<NavigationManager>();nav.NavigateTo(Location(company));var cut=context.RenderComponent<AgentSupervisionReports>();cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll("input[type=date]").Count));
        cut.FindAll("input[type=date]")[0].Change("2030-03-30");cut.FindAll("select")[0].Change("finance");cut.FindAll("select")[1].Change("finance_review");cut.FindAll("select")[2].Change(report.Agents.Single().Id.ToString());cut.Find("form").Submit();
        Assert.Contains("from=2030-03-30",nav.Uri);Assert.Contains("responsibility=finance",nav.Uri);Assert.Contains("taskType=finance_review",nav.Uri);Assert.Contains(report.Agents.Single().Id.ToString(),nav.Uri);Assert.Contains(company.ToString(),nav.Uri);Assert.DoesNotContain("metric=",nav.Uri);
    }
    [Fact]
    public void Authorized_export_refreshes_display_from_same_response_and_calls_existing_download_module()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var report=Report(company);var fresh=report with{SnapshotHash="fresh",Rows=[]};
        context.Services.AddSingleton(new AgentSupervisionApiClient(new Transport(uri=>Task.FromResult(uri.Contains("/export?")?Reply(new SupervisionCsv("agent-supervision-20300331-20300331.csv","permitted csv",fresh)):Reply(report)))));
        var module=context.JSInterop.SetupModule("./js/reportDownload.js");module.SetupVoid("downloadReport",_=>true).SetVoidResult();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(Location(company));var cut=context.RenderComponent<AgentSupervisionReports>();cut.WaitForAssertion(()=>Assert.Contains("Review pending",cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent=="Download these permitted rows").Click();cut.WaitForAssertion(()=>Assert.Contains("Download prepared for 0 permitted rows",cut.Markup));Assert.DoesNotContain("Review pending",cut.Markup);
        var download=Assert.Single(module.Invocations);Assert.Equal("downloadReport",download.Identifier);Assert.Equal("permitted csv",download.Arguments[1]);Assert.DoesNotContain("saved successfully",cut.Markup,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Denied_report_has_no_cards_rows_or_download_and_can_refresh()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();
        context.Services.AddSingleton(new AgentSupervisionApiClient(new Transport(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(Location(company));var cut=context.RenderComponent<AgentSupervisionReports>();cut.WaitForAssertion(()=>Assert.Contains("Manager access",cut.Markup));
        Assert.Empty(cut.FindAll(".supervision-measure"));Assert.Empty(cut.FindAll(".supervision-row"));Assert.DoesNotContain("Download these",cut.Markup);Assert.Contains("Refresh report",cut.Markup);
    }
    [Fact]
    public void Newly_denied_export_clears_previously_displayed_scope_and_never_calls_download()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var report=Report(company);
        context.Services.AddSingleton(new AgentSupervisionApiClient(new Transport(uri=>Task.FromResult(uri.Contains("/export?")?new HttpResponseMessage(HttpStatusCode.Forbidden):Reply(report)))));
        var module=context.JSInterop.SetupModule("./js/reportDownload.js");context.Services.GetRequiredService<NavigationManager>().NavigateTo(Location(company));var cut=context.RenderComponent<AgentSupervisionReports>();cut.WaitForAssertion(()=>Assert.Contains("Review pending",cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent=="Download these permitted rows").Click();cut.WaitForAssertion(()=>Assert.Contains("Manager access",cut.Markup));Assert.Empty(cut.FindAll(".supervision-measure"));Assert.DoesNotContain("Review pending",cut.Markup);Assert.Empty(module.Invocations);
    }
    [Theory]
    [InlineData("https://example.com/agents/staff/reports?companyId=")]
    [InlineData("/work?companyId=")]
    [InlineData("//example.com/agents/staff/reports?companyId=")]
    public void Return_link_rejects_external_or_wrong_route(string path)
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/work?companyId={company}&supervisionReturnUrl={Uri.EscapeDataString(path+company)}");
        var cut=context.RenderComponent<AgentWorkReturnLink>(p=>p.Add(x=>x.CompanyId,company));Assert.Empty(cut.FindAll("[data-testid=supervision-return]"));
    }
    [Fact]
    public void Return_link_preserves_exact_filters_and_rejects_foreign_company()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var nav=context.Services.GetRequiredService<NavigationManager>();var origin=Location(company);
        nav.NavigateTo($"/work?companyId={company}&supervisionReturnUrl={Uri.EscapeDataString(origin)}");var cut=context.RenderComponent<AgentWorkReturnLink>(p=>p.Add(x=>x.CompanyId,company));Assert.Equal(origin,cut.Find("[data-testid=supervision-return]").GetAttribute("href"));
        nav.NavigateTo($"/work?companyId={company}&supervisionReturnUrl={Uri.EscapeDataString(Location(Guid.NewGuid()))}");cut.WaitForAssertion(()=>Assert.Empty(cut.FindAll("[data-testid=supervision-return]")));
    }
    [Fact]
    public async Task Typed_client_rejects_foreign_filters_null_evidence_and_offline_access()
    {
        var company=Guid.NewGuid();var report=Report(company);var q=report.Query;
        var client=new AgentSupervisionApiClient(new Transport(_=>Task.FromResult(Reply(report with{Query=q with{CompanyId=Guid.NewGuid()}}))));await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(q));
        client=new(new Transport(_=>Task.FromResult(Reply(report with{Rows=[report.Rows.Single() with{Evidence=null!}]}))));await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(q));
        client=new(new Transport(_=>throw new Exception("No offline transport")),true);await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(q));
    }
    private static string Location(Guid company)=>$"/agents/staff/reports?companyId={company}&from=2030-03-31&to=2030-03-31&responsibility=finance&taskType=finance_review&view=bottlenecks&metric=approval_wait";
    private static AgentSupervisionReport Report(Guid company)
    {
        var agent=Guid.NewGuid();var task=Guid.NewGuid();var now=DateTime.UtcNow;
        return new(new(company,new(2030,3,31),new(2030,3,31),"finance","finance_review",null,"bottlenecks","approval_wait"),now,now.AddDays(-1),now,"Europe/Stockholm",false,["Current work status is observed now; retained events only."],["finance"],["finance_review"],[new(agent,"Finance agent","finance")],
            [new("approval_wait","bottlenecks","Approval intervals",1,1,"Permitted linked approvals","Unique request and clipped calendar seconds","Retained evidence",7200,10800),new("blocked_duration","bottlenecks","Historical blocked duration",null,0,"Complete transition history","Unavailable","No complete transitions")],
            [new("approval_wait:one","approval_wait","task",task,"Review pending","finance","finance_review",agent,"Finance agent","pending",now,"approval_interval",$"/agents/work/task/{task}?companyId={company}",Guid.NewGuid(),7200,10800,true,[new(task,"approval_request",now,"Retained timing facts"),new(Guid.NewGuid(),"collaboration_contribution",now,"Retained version 2")])],"snapshot");
    }
    private static HttpResponseMessage Reply<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Transport(Func<string,Task<HttpResponseMessage>> send):ICompanyApiTransport{
        public Uri? BaseAddress=>new("http://localhost/");public Task<HttpResponseMessage> SendAsync(Guid company,HttpMethod method,string uri,HttpContent? body,CancellationToken ct=default)=>send(uri);
    }
}
