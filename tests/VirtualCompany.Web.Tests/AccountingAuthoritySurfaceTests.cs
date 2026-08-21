namespace VirtualCompany.Web.Tests;

public sealed class AccountingAuthoritySurfaceTests
{
    [Fact]
    public void Connections_workspace_explains_authority_cutover_exports_and_reconciliation()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingConnectionsPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "AccountingConnectionsPage.razor.cs");
        var client = Read("src", "VirtualCompany.Web", "Services", "FinanceApiClient.AccountingAuthority.cs");
        var controller = Read("src", "VirtualCompany.Api", "Controllers", "InternalFinanceController.AccountingAuthority.cs");
        var sales = Read("src", "VirtualCompany.Infrastructure.Sales", "Sales", "SalesOperationsService.cs");
        var support = Read("src", "VirtualCompany.Infrastructure.Support", "Support", "SupportRefundFinanceService.cs");
        var agentTools = Read("src", "VirtualCompany.Infrastructure.Operations", "Companies", "InternalCompanyToolContract.cs");

        Assert.Contains("@page \"/finance/accounting/connections\"", page, StringComparison.Ordinal);
        Assert.Contains("Each accounting period has exactly one authoritative book.", page, StringComparison.Ordinal);
        Assert.Contains("accounting-authority-timeline", page, StringComparison.Ordinal);
        Assert.Contains("Pending exports", page, StringComparison.Ordinal);
        Assert.Contains("Export and reconciliation", page, StringComparison.Ordinal);
        Assert.Contains("PreviewAccountingAuthorityChangeAsync", code, StringComparison.Ordinal);
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
    public void Authority_reference_image_is_saved_for_the_screenshot_first_workflow()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "design", "references", "accounting-authority-connections-reference.png")));
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
