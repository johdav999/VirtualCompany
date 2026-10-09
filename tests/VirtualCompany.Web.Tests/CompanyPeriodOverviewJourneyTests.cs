using Bunit;
using System.Web;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class CompanyPeriodOverviewJourneyTests
{
    [Fact]
    public void Weekly_company_overview_precedes_work_details_and_links_exact_source_evidence()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var source = WeeklyWorkspaceJourneyTests.Workspace("company");
        var finance = source.Contributions[0] with { Lens = "finance", Title = "Finance", Metrics = [
            source.Contributions[0].Metrics[0] with { Key = "finance.cash.SEK", Label = "Cash SEK", Value = 109750, DisplayValue = "109750 SEK", Kind = "balance" },
            source.Contributions[0].Metrics[0] with { Key = "finance.cash.USD", Label = "Cash USD", Value = 50, DisplayValue = "50 USD", Kind = "balance" }] };
        source = source with { AvailableLenses = [new("company", "Company", true, "Owner"), new("finance", "Finance", false, "Oversight")], Contributions = [source.Contributions[0], finance] };
        var cut = context.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, source.CompanyId).Add(x => x.Workspace, source));
        var summary = cut.Find("[data-testid=company-period-overview]");
        Assert.Contains(source.CompanyName, summary.TextContent); Assert.Contains(source.Period.Label, summary.TextContent);
        Assert.Contains("109750 SEK", summary.TextContent); Assert.Contains("50 USD", summary.TextContent);
        Assert.DoesNotContain("109800", summary.TextContent);
        Assert.True(cut.Markup.IndexOf("company-period-overview", StringComparison.Ordinal) < cut.Markup.IndexOf("weekly-role-company", StringComparison.Ordinal));
        var href = summary.QuerySelector("dt a")!.GetAttribute("href")!;
        var query = HttpUtility.ParseQueryString(new Uri("https://test.local" + href).Query);
        Assert.Equal("finance.cash.SEK", query["metric"]); Assert.Equal("company", query["lens"]);
        Assert.Equal("2026-09-28", query["week"]); Assert.Equal(source.CompanyId.ToString("D"), query["companyId"]);
        cut.SetParametersAndRender(p => p.Add(x => x.CompanyId, Guid.NewGuid()));
        Assert.Empty(cut.FindAll("[data-testid=company-period-overview]"));
    }

    [Fact]
    public void Failed_or_unauthorized_sources_do_not_become_zero_figures()
    {
        var source = WeeklyWorkspaceJourneyTests.Workspace("company");
        var failed = source.Contributions[0] with { Lens = "finance", Metrics = [], Commitments = [], CoverageGaps = ["Finance source unavailable"] };
        source = source with { Contributions = [source.Contributions[0], failed] };
        Assert.Single(CompanyPeriodOverviewPresentation.Weekly(source));
        source = source with { AvailableLenses = [new("company", "Company", true, "Owner"), new("finance", "Finance", false, "Oversight")] };
        var finance = CompanyPeriodOverviewPresentation.Weekly(source).Single(x => x.Lens == "finance");
        Assert.Empty(finance.Figures); Assert.Contains("unavailable", finance.Coverage);
    }

    [Fact]
    public void Monthly_company_overview_uses_recorded_facts_and_no_longer_truncates_results_to_four()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var source = MonthlyReviewJourneyTests.Workspace("company") with { Review = null,
            AvailableLenses = [new("company", "Company", true, "Owner"), new("finance", "Finance", false, "Oversight"), new("sales", "Sales", false, "Oversight")],
            Sections = [new("finance", "Finance", "Recorded balance", "current", DateTime.UnixEpoch,
                [new("Cash", "109750 SEK"), new("Receivables", "263262 SEK"), new("Payables", "94585 SEK"), new("Runway", "Unavailable")], [], "/finance", "Retained ledger figures"),
                new("sales", "Sales", "Current recorded workload", "current", DateTime.UnixEpoch, [new("Current pipeline", "1000 USD")], [], "/app/sales", "Recorded pipeline")]
        };
        source = source with { Results = Enumerable.Range(0, 6).Select(i => source.Results[0] with { Key = "company.result" + i }).ToList() };
        var cut = context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, source.CompanyId).Add(x => x.Workspace, source));
        Assert.Equal(6, cut.FindAll(".monthly-result").Count);
        var summary = cut.Find("[data-testid=company-period-overview]");
        Assert.Contains(source.Header.CompanyName, summary.TextContent);
        Assert.Contains("109750 SEK", summary.TextContent); Assert.Contains("263262 SEK", summary.TextContent);
        Assert.Contains("94585 SEK", summary.TextContent); Assert.Contains("1000 USD", summary.TextContent);
        Assert.Contains("Unavailable", summary.TextContent);
        var retained = CompanyPeriodOverviewPresentation.Monthly(source, Guid.Parse("12345678-1234-1234-1234-123456789abc"));
        Assert.All(retained, x => Assert.Contains("snapshot%3d12345678", x.DeepLink, StringComparison.OrdinalIgnoreCase));
    }
}
