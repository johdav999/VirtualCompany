using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class AgentSupervisionWireTests
{
    [Fact]
    public async Task Real_typed_report_and_fresh_export_have_same_company_period_filters_definitions_and_rows()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var http=AgentSupervisionFixture.Client(factory);var client=new AgentSupervisionApiClient(new CompanyApiTransport(http));
        var query=new AgentSupervisionQuery(f.Company,new(2030,3,31),new(2030,3,31),"finance","finance_review",f.Agent,"bottlenecks","approval_wait");
        var report=await client.Get(query);var export=await client.Export(query);Assert.Equal(query,report.Query);Assert.Equal(query,export.Report.Query);Assert.Equal(report.SnapshotHash,export.Report.SnapshotHash);
        Assert.Equal(3,report.Rows.Count);Assert.All(report.Rows,x=>{Assert.Equal(f.Agent,x.AgentId);Assert.Equal("approval_wait",x.Metric);Assert.NotNull(x.ApprovalId);Assert.Contains(f.Company.ToString(),x.WorkRoute);});
        Assert.Equal(report.Rows.Select(x=>x.Key),export.Report.Rows.Select(x=>x.Key));Assert.Equal(23,(report.UntilUtc-report.FromUtc).TotalHours);Assert.Contains(report.SnapshotHash,export.Content);
    }
    [Fact]
    public async Task Real_typed_client_cannot_export_foreign_company_or_hidden_agent_scope()
    {
        using var factory=new TestWebApplicationFactory(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var http=AgentSupervisionFixture.Client(factory);var client=new AgentSupervisionApiClient(new CompanyApiTransport(http));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Export(new(Guid.NewGuid(),new(2030,3,31),new(2030,3,31))));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(new(f.Company,new(2030,3,31),new(2030,3,31),AgentId:Guid.NewGuid())));
    }
}
