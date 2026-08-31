namespace VirtualCompany.Web.Tests;

public sealed class ComplianceCalendarSurfaceTests
{
    [Fact]
    public void Calendar_surface_preserves_evidence_state_boundaries()
    {
        var page=Read("src","VirtualCompany.Web","Pages","Finance","ComplianceCalendarPage.razor");
        var client=Read("src","VirtualCompany.Web","Services","FinanceApiClient.ComplianceObligations.cs");
        Assert.Contains("Direct submission unavailable",page,StringComparison.Ordinal);
        Assert.Contains("Manual submission recorded",page,StringComparison.Ordinal);
        Assert.Contains("Authority received",page,StringComparison.Ordinal);
        Assert.Contains("Authority approved",page,StringComparison.Ordinal);
        Assert.Contains("ReviewComplianceEvidenceAsync",client,StringComparison.Ordinal);
        Assert.Contains("compliance-obligations",client,StringComparison.Ordinal);
    }

    [Fact]
    public void Calendar_is_routed_and_responsive()
    {
        var routes=Read("src","VirtualCompany.Web","Services","FinanceRoutes.cs");
        var nav=Read("src","VirtualCompany.Web","Components","Finance","AccountingNavigation.razor");
        var css=Read("src","VirtualCompany.Web","Pages","Finance","ComplianceCalendarPage.razor.css");
        Assert.Contains("AccountingComplianceCalendar",routes,StringComparison.Ordinal);
        Assert.Contains("Compliance calendar",nav,StringComparison.Ordinal);
        Assert.Contains("@media(max-width:950px)",css,StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"src")))directory=directory.Parent;var root=directory?.FullName??throw new DirectoryNotFoundException("Repository root was not found.");
        return File.ReadAllText(Path.Combine([root,..parts]));
    }
}
