using System.Net.Http.Json;
using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Api.Tests;

public sealed class WeeklyWorkspaceSqlServerTests
{
    [ApiSqlServerFact]
    public async Task SQL_Server_executes_all_five_weekly_owners_with_scoped_source_reconciliation()
    {
        using var factory = TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=company&week=2026-09-28");
        Assert.Equal(5, w!.Contributions.Count); Assert.Empty(w.Diagnostics);
        var metrics = w.Contributions.SelectMany(x => x.Metrics).ToDictionary(x => x.Key);
        Assert.Equal(2, metrics["sales.stage_changes"].Value); Assert.Equal(50, metrics["support.sla"].Value);
        Assert.Equal(200, metrics["finance.cash.SEK"].ComparisonValue); Assert.Equal(130, metrics["finance.due.invoice.SEK"].Value);
        foreach (var m in metrics.Values.Where(x => x.Kind == "activity"))
        { Assert.Equal(m.Value, m.Sources.Count); Assert.Equal(m.ComparisonValue, m.ComparisonSources!.Count); }
    }
}
