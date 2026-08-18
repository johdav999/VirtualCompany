using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class NavigationRaceRegressionTests
{
    [Fact]
    public void MarketingNavigation_PreservesTheActiveCompany()
    {
        var companyId = Guid.Parse("43e6a825-d1b7-429a-8608-7e668087d005");

        var route = DashboardRoutes.EnsureCompanyContext("/marketing", companyId, "/marketing");

        Assert.Equal($"/marketing?companyId={companyId:D}", route);
    }

    [Fact]
    public void CultureSynchronization_ReloadsTheCurrentRouteWithoutFollowingAStaleRedirect()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "VirtualCompany.Web",
            "wwwroot",
            "js",
            "localization.js"));

        Assert.Contains("redirect: \"manual\"", script, StringComparison.Ordinal);
        Assert.Contains("window.location.reload()", script, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
