namespace VirtualCompany.Web.Tests;

public sealed class FixedAssetsSurfaceTests
{
    [Fact]
    public void Asset_workspace_exposes_reconciliation_depreciation_timeline_and_responsive_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var workspace = Read("src", "VirtualCompany.Web", "Components", "Finance", "FixedAssetsWorkspace.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.FixedAssets.cs");
        var css = Read("src", "VirtualCompany.Web", "Components", "Finance", "FixedAssetsWorkspace.razor.css");

        Assert.Contains("FixedAssetsWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("ReconciledToLedger", workspace, StringComparison.Ordinal);
        Assert.Contains("LegacyAssetsNeedReview", workspace, StringComparison.Ordinal);
        Assert.Contains("AssetTimeline", workspace, StringComparison.Ordinal);
        Assert.Contains("AssetComponents", workspace, StringComparison.Ordinal);
        Assert.Contains("FixedAssetComponentResponse", client, StringComparison.Ordinal);
        Assert.Contains("ReviewDepreciation", workspace, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", workspace, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", workspace, StringComparison.Ordinal);
        Assert.Contains("EnsureOnlineMutation", client, StringComparison.Ordinal);
        Assert.Contains("PopulationHash", client, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:640px)", css, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "finance-fixed-assets-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "finance-fixed-assets-reference-prompt.md")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
