namespace VirtualCompany.Web.Tests;

public sealed class YearEndRolloverSurfaceTests
{
    [Fact]
    public void Workspace_exposes_readiness_approval_reconciliation_and_subsequent_event_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "YearEndRolloverPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "YearEndRolloverPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.YearEndRollover.cs");

        Assert.Contains("@page \"/finance/accounting/year-end\"", page, StringComparison.Ordinal);
        Assert.Contains("Six controlled gates", page, StringComparison.Ordinal);
        Assert.Contains("Approve independently", page, StringComparison.Ordinal);
        Assert.Contains("account, currency, and dimension", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prior year remains immutable", page, StringComparison.Ordinal);
        Assert.Contains("Subsequent events", page, StringComparison.Ordinal);
        Assert.Contains("Opening activation is blocked", page, StringComparison.Ordinal);
        Assert.Contains("EvidenceWorkAsync", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedEvidenceHash", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subsequent-events", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_and_prompt_are_retained()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "year-end-rollover-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "year-end-rollover-reference-prompt.md")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
