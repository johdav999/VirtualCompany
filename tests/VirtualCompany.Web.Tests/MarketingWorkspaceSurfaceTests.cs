namespace VirtualCompany.Web.Tests;

public sealed class MarketingWorkspaceSurfaceTests
{
    [Fact]
    public void Creative_workspace_exposes_fail_closed_scan_and_recovery_states()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");

        Assert.Contains("Quarantined — an authoritative safety scan must pass", source, StringComparison.Ordinal);
        Assert.Contains("Request changes", source, StringComparison.Ordinal);
        Assert.Contains(">Rescan</button>", source, StringComparison.Ordinal);
        Assert.Contains("scan?.Result != \"passed\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("malwareScan = \"storage_provider_required\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Segment_workspace_allows_reviewable_bootstrap_drafts_and_exposes_retry()
    {
        var source = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor");

        Assert.Contains("!segmentProposal.CanCreateDraft", source, StringComparison.Ordinal);
        Assert.Contains("Create reviewable draft", source, StringComparison.Ordinal);
        Assert.Contains("Retry Maya", source, StringComparison.Ordinal);
        Assert.Contains("Evidence gaps remain visible", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_client_uses_company_scoped_scan_and_recovery_routes()
    {
        var source = Read("src", "VirtualCompany.Web", "Services", "MarketingApiClient.cs");

        Assert.Contains("GetCreativeAssetScansAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("RequestCreativeAssetChangesAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("RescanCreativeAssetAsync(Guid companyId", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/scans", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/request-changes", source, StringComparison.Ordinal);
        Assert.Contains("api/marketing/creative-assets/{assetId:D}/rescan", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Marketing_workspace_retains_narrow_width_layout_rules()
    {
        var css = Read("src", "VirtualCompany.Web", "Pages", "Marketing", "MarketingDashboard.razor.css");

        Assert.Contains("@media", css, StringComparison.Ordinal);
        Assert.Contains("max-width", css, StringComparison.Ordinal);
        Assert.Contains("overflow", css, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
