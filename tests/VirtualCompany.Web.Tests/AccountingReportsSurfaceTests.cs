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
        var app = Read("src", "VirtualCompany.Web", "App.razor");
        var globalCss = Read("src", "VirtualCompany.Web", "wwwroot", "css", "app.css");

        Assert.Contains("@page \"/finance/accounting/reports\"", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"CountryNeutralTaxNotice\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"ImmutableJournalDetail\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"PeriodCloseChecklist\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"LockHistory\"]", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"ExportJobs\"]", page, StringComparison.Ordinal);
        Assert.Contains("VatReturnWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("AllowedActions", Read("src", "VirtualCompany.Web", "Components", "Finance", "VatReturnWorkspace.razor"), StringComparison.Ordinal);
        Assert.Contains("RequestStatutoryExportAsync", code, StringComparison.Ordinal);
        Assert.Contains("GetVatReturnPackageDownloadUrl", code, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"button\" tabindex=\"0\"", page, StringComparison.Ordinal);
        Assert.Contains("HandleAccountKeyAsync", code, StringComparison.Ordinal);
        Assert.Contains("CloseAndLockAccountingPeriodAsync", code, StringComparison.Ordinal);
        Assert.Contains("ReopenAccountingPeriodAsync", code, StringComparison.Ordinal);
        Assert.Contains("Idempotency", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media(max-width:800px)", css, StringComparison.Ordinal);
        Assert.Contains("20260825-swedish-statutory-reporting", app, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", globalCss, StringComparison.Ordinal);
        Assert.Contains(".finance-module-shell__content > *", globalCss, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enum", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reports_reference_image_is_saved_for_screenshot_first_workflow()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-reports-close-reference.png")));
    }

    [Fact]
    public void Currency_revaluation_workspace_exposes_reproducible_controlled_workflow()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var workspace = Read("src", "VirtualCompany.Web", "Components", "Finance", "CurrencyRevaluationWorkspace.razor");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.CurrencyRevaluation.cs");

        Assert.Contains("CurrencyRevaluationWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"ReproducibleEvidence\"]", workspace, StringComparison.Ordinal);
        Assert.Contains("PopulationChecksum", workspace, StringComparison.Ordinal);
        Assert.Contains("RateSetChecksum", workspace, StringComparison.Ordinal);
        Assert.Contains("ProposalChecksum", workspace, StringComparison.Ordinal);
        Assert.Contains("SubmitCurrencyRevaluationAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("ReverseCurrencyRevaluationAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("PreviewRequestIdentity ??=", workspace, StringComparison.Ordinal);
        Assert.Contains("GetCurrencyRevaluationRunAsync(companyId, requestedRunId)", workspace, StringComparison.Ordinal);
        Assert.Contains("ExpectedVersion", client, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "finance-currency-revaluation-reference.png")));
    }

    [Fact]
    public void Accounting_dimensions_workspace_exposes_governance_allocation_and_immutable_drilldown()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingReportsPage.razor");
        var workspace = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingDimensionsWorkspace.razor");
        var css = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingDimensionsWorkspace.razor.css");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AccountingDimensions.cs");

        Assert.Contains("AccountingDimensionsWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"DimensionCatalogue\"]", workspace, StringComparison.Ordinal);
        Assert.Contains("FinanceText[\"MappingConflicts\"]", workspace, StringComparison.Ordinal);
        Assert.Contains("PreviewAccountingAllocationAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("HierarchyPath", workspace, StringComparison.Ordinal);
        Assert.Contains("LedgerEntryId", client, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", client, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:1000px)", css, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references",
            "finance-accounting-dimensions-reference.png")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
