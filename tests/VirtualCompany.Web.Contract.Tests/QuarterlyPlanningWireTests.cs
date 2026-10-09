using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class QuarterlyPlanningWireTests
{
    [Fact] public async Task Native_options_preview_save_history_and_reproduction_use_the_full_typed_wire()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var h = s.Client(f);
        var client = new QuarterlyPlanningApiClient(new CompanyApiTransport(h)); var o = await client.Options(s.Company.Company, 2026, 3, default); Assert.Contains(o.Goals, x => x.Id == s.Goal); Assert.Contains(o.Evidence, x => x.Id == s.Snapshots[0]);
        var input = System.Text.Json.JsonSerializer.Deserialize<PreviewQuarterReview>(System.Text.Json.JsonSerializer.Serialize(s.Proposal))!;
        var p = await client.Preview(s.Company.Company, input, default); Assert.Equal(s.Actual, p.Objectives.Single().Actual); Assert.Equal(10, p.Conflicts.Single().Shortfall);
        var saved = await client.Save(s.Company.Company, new(input, null, 0, Guid.NewGuid(), p.Fingerprint), default); Assert.Equal(saved.Summary.Id, (await client.History(s.Company.Company, 2026, 3, default)).Single().Id);
        var opened = await client.Open(s.Company.Company, saved.Summary.Id, default); Assert.Equal(saved.Review.Fingerprint, opened.Review.Fingerprint); Assert.Equal(s.Actual, opened.Review.Objectives.Single().Actual);
    }
}
