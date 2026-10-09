using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class SalesManagementWireTests
{
    [Fact]
    public async Task Native_management_report_proposal_history_revision_and_monthly_snapshot_keep_typed_contracts()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var transport=new CompanyApiTransport(http);var client=new SalesManagementApiClient(transport,false);var r=await client.Report(s.Company,new(2026,9));Assert.Equal(s.Company,r.CompanyId);Assert.Contains(s.Deal,r.CurrentOpenOpportunityIds);
        var request=new SaveSalesCapacityProposal(Guid.NewGuid(),new(2026,9),new(160,8,[new("Planning allocation",100)],"Typed recorded notes"));
        var saved=await client.Save(s.Company,request);var opened=await client.Open(s.Company,saved.Summary.Id);Assert.Equal(saved.Checksum,opened.Checksum);Assert.Equal(20,opened.Result.OpportunityCapacity);Assert.Equal(saved.Summary.Id,Assert.Single(await client.History(s.Company,0)).Id);
        var next=await client.Save(s.Company,request with{RequestId=Guid.NewGuid(),PreviousId=saved.Summary.Id,ExpectedRevision=1});Assert.Equal(2,next.Summary.Revision);
        var reviews=new MonthlyReviewApiClient(transport,false);var monthly=await reviews.SaveAsync(s.Company,"sales",2026,9,Guid.NewGuid(),"Sales analysis",default);Assert.NotNull(monthly.Workspace.SalesManagement);Assert.Equal(r.Selected,monthly.Workspace.SalesManagement!.Selected);
        var old=await reviews.OpenAsync(s.Company,monthly.Summary.Id,default);Assert.Equal(monthly.Workspace.SalesManagement.Selected,old.Workspace.SalesManagement!.Selected);
    }
}
