namespace VirtualCompany.Web.Tests;

public sealed class AccountingReportsSurfaceTests
{
    [Fact]
    public void Reports_workspace_exposes_evidence_close_export_and_responsive_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor.css");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AccountingReports.cs");

        Assert.Contains("@page \"/finance/accounting/reports\"", page, StringComparison.Ordinal);
        Assert.Contains("Country-neutral bookkeeping summary", page, StringComparison.Ordinal);
        Assert.Contains("Immutable journal detail", page, StringComparison.Ordinal);
        Assert.Contains("Period close checklist", page, StringComparison.Ordinal);
        Assert.Contains("Lock history", page, StringComparison.Ordinal);
        Assert.Contains("Export jobs", page, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("CloseAndLockAccountingPeriodAsync", code, StringComparison.Ordinal);
        Assert.Contains("ReopenAccountingPeriodAsync", code, StringComparison.Ordinal);
        Assert.Contains("Idempotency", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media(max-width:800px)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enum", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reports_reference_image_is_saved_for_screenshot_first_workflow()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-reports-close-reference.png")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
