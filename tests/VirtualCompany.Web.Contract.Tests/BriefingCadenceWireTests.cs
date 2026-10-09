using System.Text.Json;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class BriefingCadenceWireTests
{
    [Fact] public async Task Real_api_serialization_roundtrips_settings_preview_and_delivered_record()
    {
        using var f = new TestWebApplicationFactory(new BriefingCadenceFixture.Clock()); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f); var api = new BriefingCadenceApiClient(new CompanyApiTransport(h));
        var settings = JsonSerializer.Deserialize<BriefingCadenceSettings>(JsonSerializer.Serialize(BriefingCadenceFixture.Settings))!;
        Assert.True((await api.Save(s.Company, settings, default)).Configured); Assert.Equal(s.SalesTask, Assert.Single((await api.Preview(s.Company, default)).Items).Id);
        await s.Schedule(f, new(2026,10,6,7,0,0,DateTimeKind.Utc)); await s.RunJobs(f); await s.Dispatch(f); var audit = Assert.Single((await api.Preview(s.Company, default)).Audit); Assert.Equal("sent", audit.Status);
        Assert.Equal(s.SalesTask, Assert.Single((await api.Open(s.Company, audit.Id, default)).Items).Id);
    }
}
