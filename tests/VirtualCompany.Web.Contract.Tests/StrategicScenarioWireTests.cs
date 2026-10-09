using System.Text.Json;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class StrategicScenarioWireTests
{
    [Fact]public async Task Full_typed_wire_retains_sources_outputs_duplicates_and_comparisons()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);var api=new StrategicScenarioApiClient(new CompanyApiTransport(h));var c=s.Annual.Quarter.Company.Company;
        var options=await api.Options(c,2026,default);Assert.Contains(options.AnnualPlans,x=>x.Id==s.PlanId);Assert.Contains(options.Forecasts,x=>x.Id==s.ForecastId);
        var input=JsonSerializer.Deserialize<StrategicScenarioInput>(JsonSerializer.Serialize(s.Input))!;var p=await api.Preview(c,input,default);var baseline=await api.Save(c,new(Guid.NewGuid(),input,p.Fingerprint),default);
        var duplicate=await api.Duplicate(c,baseline.Summary.Id,new(Guid.NewGuid(),"Typed alternative"),default);var compare=await api.Compare(c,baseline.Summary.Id,duplicate.Summary.Id,default);Assert.All(compare.Deltas,x=>Assert.Equal(0,x.ClosingCash));
        var open=await api.Open(c,baseline.Summary.Id,default);Assert.Equal(baseline.Scenario.Fingerprint,open.Scenario.Fingerprint);Assert.Equal(2,(await api.History(c,0,default)).Items.Count);Assert.Single(open.Scenario.Input.Checkpoints);
    }
}
