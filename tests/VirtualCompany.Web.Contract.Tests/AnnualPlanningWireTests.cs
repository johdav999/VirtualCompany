using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class AnnualPlanningWireTests
{
 [Fact]public async Task Annual_full_typed_wire_reopens_the_exact_reviewed_version(){using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await AnnualPlanningFixture.Seed(f);using var h=s.Client(f);var api=new AnnualPlanningApiClient(new CompanyApiTransport(h));var o=await api.Options(s.Quarter.Company.Company,2026,default);Assert.Contains(o.Budgets,x=>x.Id==s.BudgetId);var input=System.Text.Json.JsonSerializer.Deserialize<AnnualPlanInput>(System.Text.Json.JsonSerializer.Serialize(s.Input))!;var p=await api.Preview(s.Quarter.Company.Company,input,default);Assert.Empty(p.ReviewBlocks);var d=await api.Save(s.Quarter.Company.Company,new(input,null,0,Guid.NewGuid(),p.Fingerprint),default);var reviewed=await api.Review(s.Quarter.Company.Company,d.Summary.Id,new(d.Summary.StateRevision,d.Plan.Fingerprint),default);Assert.Equal("reviewed",reviewed.Summary.Status);Assert.NotNull(reviewed.Summary.ApprovalId);var opened=await api.Open(s.Quarter.Company.Company,d.Summary.Id,default);Assert.Equal(reviewed.Plan.Fingerprint,opened.Plan.Fingerprint);Assert.Equal(4,opened.Plan.Objectives.Single().Input.Milestones.Count);Assert.Single(await api.History(s.Quarter.Company.Company,2026,default));}
}
