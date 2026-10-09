using System.Text.Json;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;
public sealed class DecisionWorkWireTests
{
    [Fact] public async Task Native_creation_preview_source_and_review_roundtrip_through_typed_transport()
    {
        using var f = new TestWebApplicationFactory(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var h = s.Planning.Annual.Client(f); var api = new DecisionWorkApiClient(new CompanyApiTransport(h));
        var input = JsonSerializer.Deserialize<DecisionWorkInput>(JsonSerializer.Serialize(s.Input()))!; var context = await api.Context(s.Company, input.Source, default); Assert.Empty(context.Work);
        var preview = await api.Preview(s.Company, input, default); var command = new ConfirmDecisionWork(Guid.NewGuid(), input, preview.Fingerprint); var created = await api.Create(s.Company, command, default);
        Assert.Equal(created.TaskId, (await api.Create(s.Company, command, default)).TaskId); Assert.Single((await api.Context(s.Company, input.Source, default)).Work);
        var reviewed = await api.Review(s.Company, created.TaskId, default); Assert.Equal("awaiting_approval", reviewed.Status); Assert.NotNull(reviewed.ApprovalId);
        Assert.Equal(created.Created.Source.EvidenceJson, (await api.Open(s.Company, created.TaskId, default)).Created.Source.EvidenceJson);
    }
}
