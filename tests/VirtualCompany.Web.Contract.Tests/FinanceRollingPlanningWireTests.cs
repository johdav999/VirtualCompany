using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;

public sealed class FinanceRollingPlanningWireTests
{
    [Fact]public async Task Native_variance_preview_save_reopen_comparison_export_and_monthly_snapshot_reconcile(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);var transport=new CompanyApiTransport(h);var c=new FinanceRollingPlanningApiClient(transport,false);
        var q=new FinancePlanningQuery(2026,9,4,"approved",null,"SEK",s.Account);var r=await c.Report(s.Company.Company,q,default);Assert.Equal(99.90m,r.Rows.Single(x=>x.MonthUtc.Month==9).Variance);
        var i=new PreviewFinanceForecast(q,FinanceRollingPlanningFixture.Date(2026,10),[new(FinanceRollingPlanningFixture.Date(2026,11),s.Account,null,"SEK",-1400.25m,"Typed explicit revenue assumption")],"Typed retained explanation");
        var p=await c.Preview(s.Company.Company,i,default);var saved=await c.Save(s.Company.Company,new(Guid.NewGuid(),"Typed outlook",i,p.Fingerprint),default);
        var open=await c.Open(s.Company.Company,saved.Summary.Id,default);Assert.Equal(saved.Checksum,open.Checksum);Assert.Equal(-1400.25m,open.Preview.Values.Single(x=>x.MonthUtc.Month==11).Forecast);
        var nextInput=i with{Assumptions=[i.Assumptions[0] with{Amount=-1500.25m}]};var nextPreview=await c.Preview(s.Company.Company,nextInput,default);var next=await c.Save(s.Company.Company,new(Guid.NewGuid(),"Typed later outlook",nextInput,nextPreview.Fingerprint,saved.Summary.Id),default);
        var comparison=await c.Compare(s.Company.Company,saved.Summary.Id,next.Summary.Id,"SEK",null,default);Assert.Equal(-100,comparison.Rows.Single(x=>x.MonthUtc.Month==11).Change);
        Assert.Contains("\"-100.00\"",(await c.ComparisonExport(s.Company.Company,saved.Summary.Id,next.Summary.Id,"SEK",null,default)).Content);Assert.Equal(2,(await c.History(s.Company.Company,0,default)).Count);
        var export=await c.Export(s.Company.Company,q,default);Assert.Contains("\"99.90\"",export.Content);
        var monthly=await new MonthlyReviewApiClient(transport,false).SaveAsync(s.Company.Company,"finance",2026,9,Guid.NewGuid(),"Typed Finance review",default);Assert.NotNull(monthly.Workspace.FinancePlanning);
    }
}
