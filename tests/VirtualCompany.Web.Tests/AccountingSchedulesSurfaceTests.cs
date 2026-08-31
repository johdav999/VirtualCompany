namespace VirtualCompany.Web.Tests;

public sealed class AccountingSchedulesSurfaceTests
{
    [Fact]
    public void Schedule_workspace_exposes_controlled_lifecycle_and_accountant_evidence()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var workspace = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor");
        var code = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AccountingSchedules.cs");
        var css = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor.css");

        Assert.Contains("AccountingSchedulesWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("AllowedActions", workspace, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"ScheduleReconciled\"]", workspace, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", workspace, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", workspace, StringComparison.Ordinal);
        Assert.Contains("CreateIdentity ??=", code, StringComparison.Ordinal);
        Assert.Contains("SubmitIdentity ??=", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion", client, StringComparison.Ordinal);
        Assert.Contains("EnsureOnlineMutation", client, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:620px)", css, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "finance-accounting-schedules-reference.png")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
