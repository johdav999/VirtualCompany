using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;

public sealed class SupportQualityWireTests
{
    [Fact] public async Task Quality_grouping_capacity_retention_and_export_use_the_native_wire_contract()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);
        var transport=new CompanyApiTransport(h);var c=new SupportQualityApiClient(transport,false);var company=s.Company.Company;var q=new SupportQualityQuery(2026,9,"billing");
        var report=await c.Report(company,q,default);Assert.Equal(50,report.Metrics.ReopenRate);Assert.Equal(60,report.Metrics.MeanResponseBusinessMinutes);
        var group=await c.Correct(company,new(Guid.NewGuid(),q,s.First,"Reviewed billing","Verified against recorded case evidence.",report.Fingerprint),default);
        Assert.Equal(group.Id,(await c.Groups(company,s.First,default)).Single().Id);
        var input=new PreviewSupportCapacity(q,new(2026,10,20,5,30,1,4,80,60,"Explicit bounded assumptions."));
        var preview=await c.Preview(company,input,default);Assert.Equal(21,preview.Result.BusinessDays);Assert.Equal(12.5m,preview.Result.RequiredHours);
        var saved=await c.Save(company,new(Guid.NewGuid(),"Typed Support capacity",input,preview.Fingerprint),default);
        var open=await c.Open(company,saved.Summary.Id,default);Assert.Equal(saved.Checksum,open.Checksum);Assert.Single(await c.History(company,0,default));
        Assert.Contains("Typed Support capacity",(await c.ProposalExport(company,saved.Summary.Id,default)).Content);
        Assert.Contains("P24-A",(await c.Export(company,q,default)).Content);
        var review=await new MonthlyReviewApiClient(transport,false).SaveAsync(company,"customers",2026,9,Guid.NewGuid(),"Typed Support review",default);
        Assert.NotNull(review.Workspace.SupportQuality);Assert.Equal(2026,review.Workspace.SupportQuality.Query.Year);
    }
}
