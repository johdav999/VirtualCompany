namespace VirtualCompany.Web.Tests;

public sealed class AccountingAuthoritySurfaceTests
{
    [Fact]
    public void Connections_workspace_uses_guided_switch_read_models_and_preserves_exports()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingConnectionsPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingConnectionsPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AccountingAuthority.cs");
        var controller = Read("src", "VirtualCompany.Api", "Controllers", "InternalFinanceController.AccountingAuthority.cs");
        var sales = Read("src", "VirtualCompany.Infrastructure.Sales", "Sales", "SalesOperationsService.cs");
        var support = Read("src", "VirtualCompany.Infrastructure.Support", "Support", "SupportRefundFinanceService.cs");
        var agentTools = Read("src", "VirtualCompany.Infrastructure.Operations", "Companies", "InternalCompanyToolContract.cs");

        Assert.Contains("@page \"/finance/accounting/connections\"", page, StringComparison.Ordinal);
        Assert.Contains("AccountingMigrationWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("StartGuidedMigrationAsync", page, StringComparison.Ordinal);
        Assert.Contains("accounting-exports", page, StringComparison.Ordinal);
        Assert.Contains("GetAccountingMigrationGuidanceAsync", code, StringComparison.Ordinal);
        Assert.Contains("GetAccountingProviderSwitchMappingsAsync", code, StringComparison.Ordinal);
        Assert.Contains("CompleteAccountingAuthorityCutoverAsync", code, StringComparison.Ordinal);
        Assert.Contains("ReconcileAccountingProviderExportAsync", client, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = CompanyPolicies.AccountingAdmin)]", controller, StringComparison.Ordinal);
        Assert.Contains("IFinanceAccountingActionService", sales, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFortnoxDraftPayload", sales, StringComparison.Ordinal);
        Assert.DoesNotContain("FinanceIntegrationProviderKeys.Fortnox", sales, StringComparison.Ordinal);
        Assert.Contains("IFinanceAccountingActionService", support, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestCustomerInvoiceFortnoxExportCommand", support, StringComparison.Ordinal);
        Assert.DoesNotContain("FinanceIntegrationProviderKeys.Fortnox", agentTools, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_workspace_reference_image_is_saved_for_the_screenshot_first_workflow()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-migration-workspace-reference.png")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-migration-monitoring-reference.png")));
    }

    [Fact]
    public void Migration_workspace_has_accessible_major_states_and_no_raw_payload_rendering()
    {
        var component = Read("src", "VirtualCompany.Web", "Components", "Finance", "AccountingMigrationWorkspace.razor");
        Assert.Contains("data-testid=\"migration-gap-detail\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-mapping-detail\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"ambiguous-provider-outcome\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-no-permission\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-cancelled-state\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-completed-state\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"stale-migration-guidance\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-post-activation-monitoring\"", component, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-operations-dashboard\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-current", component, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceJson", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ReasonCode", component, StringComparison.Ordinal);
    }

    [Fact]
    public void Connections_page_has_loading_empty_and_connection_lost_presentations()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingConnectionsPage.razor");

        Assert.Contains("LoadingMessage=\"@FinanceText[\"LoadingAccountingMigration\"]\"", page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"no-active-accounting-migration\"", page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"migration-connection-lost\"", page, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
