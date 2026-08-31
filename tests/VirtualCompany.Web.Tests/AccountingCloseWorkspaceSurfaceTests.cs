namespace VirtualCompany.Web.Tests;

public sealed class AccountingCloseWorkspaceSurfaceTests
{
    [Fact]
    public void Cockpit_exposes_current_evidence_dependencies_safe_actions_and_all_close_areas()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingCloseWorkspacePage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingCloseWorkspacePage.razor.cs");
        var contract = Read("src", "VirtualCompany.Application", "Finance", "Contracts", "AccountingCloseWorkspaceContracts.cs");
        var service = Read("src", "VirtualCompany.Infrastructure.Finance", "Finance", "AccountingCloseWorkspaceService.cs");

        Assert.Contains("@page \"/finance/accounting/close-workspace\"", page, StringComparison.Ordinal);
        Assert.Contains("CloseReadinessSummary", page, StringComparison.Ordinal);
        Assert.Contains("DependenciesAndEvidence", page, StringComparison.Ordinal);
        Assert.Contains("SafeNextAction", page, StringComparison.Ordinal);
        Assert.Contains("ActionCenter", page, StringComparison.Ordinal);
        Assert.Contains("CloseEvidenceAreas", page, StringComparison.Ordinal);
        Assert.Contains("Readiness.PreparedUtc", page, StringComparison.Ordinal);
        Assert.Contains("accounting_close_evidence_stale", code, StringComparison.Ordinal);
        Assert.Contains("RefreshAccountingCloseReadinessAsync", code, StringComparison.Ordinal);
        Assert.Contains("CompanyName, string MembershipRole", contract, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters().AsNoTracking()", service, StringComparison.Ordinal);
        Assert.Contains("x.CompanyId == query.CompanyId", service, StringComparison.Ordinal);
        Assert.Contains("INotificationInboxService", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_client_preserves_company_scope_evidence_versions_and_backend_problem_reason()
    {
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.CloseWorkspace.cs");
        var baseClient = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.cs");
        var accountant = Read("src", "VirtualCompany.Web", "Pages", "AccountantPortfolio.razor");

        Assert.Contains("api/companies/{companyId:D}/finance/close-workspace", client, StringComparison.Ordinal);
        Assert.Contains("expectedEvidenceHash = readiness.EvidenceHash", client, StringComparison.Ordinal);
        Assert.Contains("expectedVersion = readiness.Version", client, StringComparison.Ordinal);
        Assert.Contains("task.Evidence.Select", client, StringComparison.Ordinal);
        Assert.Contains("ReasonCode", baseClient, StringComparison.Ordinal);
        Assert.Contains("Open the same close evidence", accountant, StringComparison.Ordinal);
        Assert.Contains("companyId={selectedCompany.CompanyId:D}", accountant, StringComparison.Ordinal);
    }

    [Fact]
    public void English_swedish_narrow_accessibility_and_screenshot_first_assets_are_retained()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingCloseWorkspacePage.razor");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingCloseWorkspacePage.razor.css");
        var english = Read("src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.resx");
        var swedish = Read("src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.sv-SE.resx");

        Assert.Contains("aria-labelledby", page, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", page, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:760px)", css, StringComparison.Ordinal);
        Assert.Contains("CloseWorkspaceTitle", english, StringComparison.Ordinal);
        Assert.Contains("Stängningsarbetsyta", swedish, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "unified-close-workspace-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "unified-close-workspace-reference-prompt.md")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
