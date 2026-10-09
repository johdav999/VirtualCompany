using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class MarketingManagementWireTests
{
    [Fact]
    public async Task Native_report_budget_revision_history_export_and_monthly_snapshot_reconcile_through_typed_clients()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=WeeklyWorkspaceFixture.Client(f);var transport=new CompanyApiTransport(http);var client=new MarketingManagementApiClient(transport,false);var query=new MarketingManagementQuery(2026,9,"SEK",ModelId:s.Model);
        var r=await client.Report(s.Company.Company,query);Assert.Equal(20,Assert.Single(r.Channels.Single(x=>x.Channel=="email").Economics).CostPerAttributedUnit);Assert.Null(Assert.Single(r.Channels.Single(x=>x.Channel=="social").Economics).CostPerAttributedUnit);Assert.False(Assert.Single(r.Experiments).Decision!.CausalEligible);
        var c=new SaveMarketingBudgetProposal(Guid.NewGuid(),query,new("SEK",300,500,[new(s.Company.Campaign,300,"Recorded assumption")],"Typed planning notes"));var saved=await client.Save(s.Company.Company,c);var opened=await client.Open(s.Company.Company,saved.Summary.Id);Assert.Equal(saved.Checksum,opened.Checksum);Assert.Equal(100,opened.Result.Difference);Assert.Equal(saved.Summary.Id,Assert.Single(await client.History(s.Company.Company,0)).Id);
        var next=await client.Save(s.Company.Company,c with{RequestId=Guid.NewGuid(),PreviousId=saved.Summary.Id,ExpectedRevision=1});Assert.Equal(2,next.Summary.Revision);Assert.Equal(saved.Summary.Id,next.Summary.PreviousId);
        var export=await client.Export(s.Company.Company,query);Assert.Contains(s.Email.ToString(),export.Csv);Assert.Contains("\"leads\"",export.Csv);
        var reviews=new MonthlyReviewApiClient(transport,false);var monthly=await reviews.SaveAsync(s.Company.Company,"marketing",2026,9,Guid.NewGuid(),"Recorded Marketing review",default);Assert.Equal(100,monthly.Workspace.MarketingManagement!.Channels.Single(x=>x.Channel=="email").KnownCost);var old=await reviews.OpenAsync(s.Company.Company,monthly.Summary.Id,default);Assert.Equal(monthly.Checksum,old.Checksum);Assert.Equal(r.Costs.Count,old.Workspace.MarketingManagement!.Costs.Count);
    }
}
