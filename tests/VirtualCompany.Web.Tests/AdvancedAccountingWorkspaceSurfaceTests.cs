namespace VirtualCompany.Web.Tests;

public sealed class AdvancedAccountingWorkspaceSurfaceTests
{
    [Fact]
    public void Advanced_accounting_exposes_canonical_focused_routes_and_compatibility_views()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AdvancedAccountingPage.razor");
        var reports = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var reportsCode = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor.cs");
        var routes = Read("src", "VirtualCompany.Web", "Services", "FinanceRoutes.cs");
        var navigation = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingNavigation.razor");

        Assert.Contains("@page \"/finance/accounting/advanced\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/finance/accounting/currency-rates\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/finance/accounting/dimensions\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/finance/accounting/schedules\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/finance/accounting/fixed-assets\"", page, StringComparison.Ordinal);
        Assert.Contains("@page \"/finance/accounting/revaluation\"", page, StringComparison.Ordinal);
        Assert.Contains("AccountingAdvanced", routes, StringComparison.Ordinal);
        Assert.Contains("AccountingAdvancedNav", navigation, StringComparison.Ordinal);
        Assert.Contains("CurrencyRevaluationWorkspace", reports, StringComparison.Ordinal);
        Assert.Contains("AccountingDimensionsWorkspace", reports, StringComparison.Ordinal);
        Assert.Contains("AccountingSchedulesWorkspace", reports, StringComparison.Ordinal);
        Assert.Contains("FixedAssetsWorkspace", reports, StringComparison.Ordinal);
        Assert.Contains("RequestedView", reportsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Currency_rates_uses_authoritative_client_models_and_safe_recovery()
    {
        var workspace = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRatesWorkspace.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.ExchangeRates.cs");
        var css = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRatesWorkspace.razor.css");

        Assert.Contains("GetExchangeRateReadinessAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("GetExchangeRateSourcesAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("LookupExchangeRateAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("GetExchangeRateObservationAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("QueueExchangeRateRefreshAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("LastFailureSummary", workspace, StringComparison.Ordinal);
        Assert.Contains("RequestControlledRefresh", workspace, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", client, StringComparison.Ordinal);
        Assert.Contains("EnsureOnlineMutation", client, StringComparison.Ordinal);
        Assert.Contains("allowNotFound", client, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", workspace, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", workspace, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", workspace, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:640px)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", workspace, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enum", workspace, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advanced_workspaces_link_rates_subledgers_journals_reports_and_reconciliation()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AdvancedAccountingPage.razor");
        var currency = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRatesWorkspace.razor");
        var dimensions = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingDimensionsWorkspace.razor");
        var schedules = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor");
        var schedulesCode = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor.cs");
        var assets = Read("src", "VirtualCompany.Web", "Components", "Finance", "FixedAssetsWorkspace.razor");
        var revaluation = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRevaluationWorkspace.razor");

        Assert.Contains("AdvancedEvidenceChain", page, StringComparison.Ordinal);
        Assert.Contains("AccountingJournal", page, StringComparison.Ordinal);
        Assert.Contains("AccountingReports", page, StringComparison.Ordinal);
        Assert.Contains("AccountingReconciliation", page, StringComparison.Ordinal);
        Assert.Contains("GetExchangeRateObservationAsync", currency, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", dimensions, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", schedules, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", assets, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", revaluation, StringComparison.Ordinal);
        Assert.Contains("FinanceRoutes.WithCompanyContext", schedulesCode, StringComparison.Ordinal);
        Assert.Contains("entryId=", schedulesCode, StringComparison.Ordinal);
        Assert.Contains("entryId=", dimensions, StringComparison.Ordinal);
        Assert.Contains("entryId=", revaluation, StringComparison.Ordinal);
        Assert.Contains("SafeNextAction", schedules, StringComparison.Ordinal);
    }

    [Fact]
    public void Advanced_workspaces_support_keyboard_selection_and_safe_retry()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AdvancedAccountingPage.razor");
        var dimensions = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingDimensionsWorkspace.razor");
        var schedules = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingSchedulesWorkspace.razor");
        var assets = Read("src", "VirtualCompany.Web", "Components", "Finance", "FixedAssetsWorkspace.razor");
        var revaluation = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRevaluationWorkspace.razor");

        Assert.Contains("SelectMemberOnKeyAsync", dimensions, StringComparison.Ordinal);
        Assert.Contains("aria-selected", dimensions, StringComparison.Ordinal);
        Assert.Contains("@onkeydown", dimensions, StringComparison.Ordinal);
        Assert.All(new[] { page, dimensions, schedules, assets, revaluation }, source =>
            Assert.Contains("TryAgain", source, StringComparison.Ordinal));
    }

    [Fact]
    public void Advanced_accounting_reference_and_localization_are_complete()
    {
        var english = Read("src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.resx");
        var swedish = Read("src", "VirtualCompany.Web", "Localization", "Finance", "FinanceResources.sv-SE.resx");

        Assert.Contains("AdvancedAccountingTitle", english, StringComparison.Ordinal);
        Assert.Contains("AdvancedAccountingTitle", swedish, StringComparison.Ordinal);
        Assert.Contains("RequestControlledRefresh", english, StringComparison.Ordinal);
        Assert.Contains("RequestControlledRefresh", swedish, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "advanced-accounting-workspace-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "advanced-accounting-workspace-reference-prompt.md")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
