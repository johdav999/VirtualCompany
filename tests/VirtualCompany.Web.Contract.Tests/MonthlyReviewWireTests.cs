using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class MonthlyReviewWireTests
{
    [Theory][InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public async Task Typed_Web_client_roundtrips_all_monthly_reviews_and_snapshot_commands(string lens)
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var transport=new CompanyApiTransport(http);var monthly=new MonthlyWorkspaceApiClient(transport,false);var reviews=new MonthlyReviewApiClient(transport,false);
        var live=await monthly.GetAsync(s.Company,lens,2026,9);Assert.Equal(lens,live!.ActiveLens);Assert.NotNull(live.Review);
        var saved=await reviews.SaveAsync(s.Company,lens,2026,9,Guid.NewGuid(),"Retained typed decision",default);
        var opened=await reviews.OpenAsync(s.Company,saved.Summary.Id,default);Assert.Equal(saved.Checksum,opened.Checksum);Assert.Equal("Retained typed decision",opened.ReviewNotes);
        var history=await reviews.ListAsync(s.Company,lens,2026,9,0,default);Assert.Equal(saved.Summary.Id,Assert.Single(history.Items).Id);
        var revision=await reviews.RefreshAsync(s.Company,saved.Summary.Id,1,Guid.NewGuid(),"Refreshed typed decision",default);Assert.Equal(2,revision.Summary.Revision);Assert.Equal(saved.Summary.Id,revision.Summary.PreviousId);
        var export=await reviews.ExportAsync(s.Company,saved.Summary.Id,default);Assert.Contains(saved.Checksum,export.Csv);Assert.NotEmpty(export.FileName);
    }
}
