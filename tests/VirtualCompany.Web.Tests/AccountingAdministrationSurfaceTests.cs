namespace VirtualCompany.Web.Tests;

public sealed class AccountingAdministrationSurfaceTests
{
    [Fact]
    public void Accounting_pages_expose_localized_accessible_operational_states_and_responsive_layouts()
    {
        var setup = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingSetupPage.razor");
        var setupCode = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingSetupPage.razor.cs");
        var setupCss = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingSetupPage.razor.css");
        var accounts = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingAccountsPage.razor");
        var accountsCss = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingAccountsPage.razor.css");
        var periods = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingPeriodsPage.razor");
        var periodsCss = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingPeriodsPage.razor.css");

        Assert.Contains("@page \"/finance/accounting/setup\"", setup, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", setup, StringComparison.Ordinal);
        Assert.Contains("FinanceDataState", setup, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", setup, StringComparison.Ordinal);
        Assert.Contains("CanManageAccounting", setup, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", setupCode, StringComparison.Ordinal);
        Assert.Contains("CountryNeutralNotice", setup, StringComparison.Ordinal);
        Assert.Contains("LauraAccountingSetupAdvice", setup, StringComparison.Ordinal);
        Assert.Contains("StatutoryReadinessSummary", setup, StringComparison.Ordinal);
        Assert.Contains("LegalIdentityStep", setupCode, StringComparison.Ordinal);
        Assert.Contains("VatRegistrationStep", setupCode, StringComparison.Ordinal);
        Assert.Contains("DocumentSeriesStep", setupCode, StringComparison.Ordinal);
        Assert.Contains("SaveStatutoryProfileAsync", setupCode, StringComparison.Ordinal);
        Assert.Contains("CreateDocumentSeriesAsync", setupCode, StringComparison.Ordinal);
        Assert.Contains("CurrentStep < Steps.Count && Preview?.Issues.Any(issue => issue.IsBlocking) == true", setup, StringComparison.Ordinal);
        Assert.Contains("if (Preview is null)", setupCode, StringComparison.Ordinal);
        Assert.DoesNotContain("if (Preview?.IsValid != true)", setupCode, StringComparison.Ordinal);

        Assert.Contains("@page \"/finance/accounting/accounts\"", accounts, StringComparison.Ordinal);
        Assert.Contains("FinanceDataState", accounts, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", accounts, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", accounts, StringComparison.Ordinal);
        Assert.Contains("Selected.IsProtected", accounts, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeactivate", accounts, StringComparison.Ordinal);

        Assert.Contains("@page \"/finance/accounting/periods\"", periods, StringComparison.Ordinal);
        Assert.Contains("FinanceDataState", periods, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", periods, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", periods, StringComparison.Ordinal);
        Assert.Contains("IsReportingLocked", periods, StringComparison.Ordinal);
        Assert.Contains("PeriodCloseLaterHelp", periods, StringComparison.Ordinal);

        Assert.Contains("@media", setupCss, StringComparison.Ordinal);
        Assert.Contains("@media", accountsCss, StringComparison.Ordinal);
        Assert.Contains("@media", periodsCss, StringComparison.Ordinal);
        Assert.DoesNotContain("policyPackKey", setup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", setup + accounts + periods, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accounting_reference_artifacts_cover_setup_accounts_and_periods()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-setup-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "chart-of-accounts-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "fiscal-periods-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "swedish-accounting-setup-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "swedish-vat-statutory-reporting-reference.png")));
        var prompts = Read("docs", "design", "references", "accounting-administration-reference-prompts.md");
        Assert.Contains("Accounting setup", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chart of accounts", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fiscal periods", prompts, StringComparison.OrdinalIgnoreCase);
        var swedishPrompts = Read("docs", "design", "references", "swedish-accounting-reference-prompts.md");
        Assert.Contains("Swedish accounting setup", swedishPrompts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAT and statutory reporting", swedishPrompts, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

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
