namespace VirtualCompany.Web.Tests;

public sealed class AuditPackagesSurfaceTests
{
    [Fact]
    public void Workspace_exposes_frozen_scope_integrity_and_incomplete_evidence_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AuditPackagesPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AuditPackagesPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AuditPackages.cs");
        var routes = Read("src", "VirtualCompany.Web", "Services", "FinanceRoutes.cs");

        Assert.Contains("@page \"/finance/accounting/audit-packages\"", page, StringComparison.Ordinal);
        Assert.Contains("Request final package", page, StringComparison.Ordinal);
        Assert.Contains("Missing", page, StringComparison.Ordinal);
        Assert.Contains("Inaccessible", page, StringComparison.Ordinal);
        Assert.Contains("Corrupt", page, StringComparison.Ordinal);
        Assert.Contains("Package checksum", page, StringComparison.Ordinal);
        Assert.Contains("Manifest checksum", page, StringComparison.Ordinal);
        Assert.Contains("never broadens document access", page, StringComparison.Ordinal);
        Assert.Contains("ApproveAuditPackageAsync", code, StringComparison.Ordinal);
        Assert.Contains("AuthorizeAuditPackageDownloadAsync", code, StringComparison.Ordinal);
        Assert.Contains("VerifyAuditPackageAsync", code, StringComparison.Ordinal);
        Assert.Contains("download-authorizations", client, StringComparison.Ordinal);
        Assert.Contains("AccountingAuditPackages", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_is_saved()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "audit-packages-workspace-reference.png")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
