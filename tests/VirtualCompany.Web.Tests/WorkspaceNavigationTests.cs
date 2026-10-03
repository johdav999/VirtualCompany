using System.Web;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class WorkspaceNavigationTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("/app/sales/deals/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?view=proposal")]
    [InlineData("/support/cases/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/marketing?campaignId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/finance/invoices/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/work?tab=approvals&itemId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/history")]
    public void Business_links_keep_selection_and_exact_monthly_origin(string target)
    {
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "sales", 2026, 8);
        var route = DashboardRoutes.EnsureWorkspaceContext(target, Company, origin);
        var query = HttpUtility.ParseQueryString(new Uri("http://localhost" + route).Query);
        Assert.Equal(Company.ToString("D"), query["companyId"]);
        Assert.Equal(origin, query["returnUrl"]);
        foreach (string key in HttpUtility.ParseQueryString(new Uri("http://localhost" + target).Query))
            Assert.Equal(HttpUtility.ParseQueryString(new Uri("http://localhost" + target).Query)[key], query[key]);
    }

    [Fact]
    public void Stale_company_context_is_replaced_and_return_context_is_not_duplicated()
    {
        var origin = DashboardRoutes.BuildTodayPath(Company, "customers");
        var route = DashboardRoutes.EnsureWorkspaceContext(
            $"/support?companyId={Other:D}&returnUrl=%2Fdashboard%3FcompanyId%3D{Other:D}#cases", Company, origin);
        var uri = new Uri("http://localhost" + route);
        Assert.Equal(Company.ToString("D"), HttpUtility.ParseQueryString(uri.Query)["companyId"]);
        Assert.Equal(origin, HttpUtility.ParseQueryString(uri.Query)["returnUrl"]);
        Assert.Equal("#cases", uri.Fragment);
        Assert.Equal(1, route.Split("returnUrl=").Length - 1);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    [InlineData("/%5cexample.com")]
    [InlineData("/%2fexample.com")]
    [InlineData("/finance-unrelated")]
    public void Unsafe_or_unrecognized_destinations_fall_back_to_current_overview(string target)
    {
        var origin = DashboardRoutes.BuildTodayPath(Company, "sales");
        Assert.Equal(origin, DashboardRoutes.EnsureWorkspaceContext(target, Company, origin));
    }

    [Fact]
    public void Return_navigation_survives_module_links_but_clears_on_company_switch()
    {
        var context = new OverviewNavigationContext();
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "finance", 2026, 8);
        Assert.Equal(origin, context.Resolve("http://localhost" + origin, Company));
        Assert.Equal(origin, context.Resolve($"http://localhost/finance/invoices?companyId={Company:D}", Company));
        Assert.Equal(DashboardRoutes.BuildTodayPath(Other), context.Resolve($"http://localhost/dashboard?companyId={Other:D}", Other));
        Assert.Equal(DashboardRoutes.BuildTodayPath(Company), context.Resolve($"http://localhost/finance?companyId={Company:D}", Company));
    }

    [Fact]
    public void Reload_reads_context_from_return_url_and_rejects_another_company_origin()
    {
        var context = new OverviewNavigationContext();
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "sales", 2026, 8);
        var route = DashboardRoutes.EnsureWorkspaceContext("/app/sales", Company, origin);
        Assert.Equal(origin, context.Resolve("http://localhost" + route, Company));
        Assert.Null(DashboardRoutes.NormalizeOverviewPath(origin, Other));
        Assert.Null(DashboardRoutes.NormalizeOverviewPath($"/dashboard?companyId={Company:D}&lens=administrator", Company));
    }
}
