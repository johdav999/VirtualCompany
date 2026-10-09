using System.Net.Http.Json;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Api.Tests;

[Trait("Category","SqlServer")]
public sealed class AgentSupervisionSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Migrated_SQL_queries_preserve_period_clipping_authorization_and_exact_export_rows()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new AgentSupervisionFixture.Clock());var f=await AgentSupervisionFixture.Seed(factory);using var client=AgentSupervisionFixture.Client(factory);
        var route=AgentSupervisionIntegrationTests.Route(f.Company,"bottlenecks","approval_wait");var report=(await client.GetFromJsonAsync<AgentSupervisionReport>(route))!;
        Assert.Equal(3,report.Rows.Count);Assert.Equal(23,(report.UntilUtc-report.FromUtc).TotalHours);Assert.Equal(90000,report.Measures.Single(x=>x.Code=="approval_wait").SecondsInPeriod);
        var export=(await client.GetFromJsonAsync<SupervisionCsv>(route.Replace("agent-supervision?","agent-supervision/export?")))!;Assert.Equal(report.SnapshotHash,export.Report.SnapshotHash);Assert.Equal(report.Rows.Select(x=>x.Key),export.Report.Rows.Select(x=>x.Key));
    }
}
