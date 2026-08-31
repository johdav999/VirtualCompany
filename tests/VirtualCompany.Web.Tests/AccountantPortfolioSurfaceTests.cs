namespace VirtualCompany.Web.Tests;

public sealed class AccountantPortfolioSurfaceTests
{
    [Fact]
    public void Portfolio_exposes_explicit_grant_risk_evidence_and_separation_of_duties_states()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "AccountantPortfolio.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "AccountantCollaborationApiClient.cs");
        var service = Read("src", "VirtualCompany.Infrastructure.Operations", "Companies", "AccountantCollaborationService.cs");
        Assert.Contains("@page \"/accountant/portfolio\"", page, StringComparison.Ordinal);
        Assert.Contains("Explicit grants only", page, StringComparison.Ordinal);
        Assert.Contains("No implicit group access", page, StringComparison.Ordinal);
        Assert.Contains("Preparers cannot sign off their own work", page, StringComparison.Ordinal);
        Assert.Contains("inaccessible attachments remain hidden", page, StringComparison.Ordinal);
        Assert.Contains("api/accountant/portfolio", client, StringComparison.Ordinal);
        Assert.Contains("x.AccountantUserId == userId", service, StringComparison.Ordinal);
        Assert.Contains("engagement.GrantId != access.Grant!.Id", service, StringComparison.Ordinal);
        Assert.Contains("self_signoff_forbidden", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_is_saved()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accountant-portfolio-collaboration-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accountant-portfolio-collaboration-reference-prompt.md")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
