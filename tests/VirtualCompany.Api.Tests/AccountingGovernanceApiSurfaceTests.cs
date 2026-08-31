using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingGovernanceApiSurfaceTests
{
    [Theory]
    [InlineData("PreviewAccountingAccountLifecycleAsync", CompanyPolicies.AccountingView)]
    [InlineData("ApplyAccountingAccountLifecycleAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("GetAccountingSeriesPoliciesAsync", CompanyPolicies.AccountingView)]
    [InlineData("SaveAccountingSeriesPolicyAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("RecordAccountingVoucherGapAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("GetCommerceAccountingCapabilityAsync", CompanyPolicies.AccountingView)]
    [InlineData("SubmitCommerceAccountingEventAsync", CompanyPolicies.AccountingAdmin)]
    public void Governance_endpoints_require_the_expected_company_policy(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == policy);
    }

    [Fact]
    public void Migration_contains_lifecycle_series_gap_and_commerce_boundary_state()
    {
        var migration = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations",
                "Persistence", "Migrations"), "*_CompleteAccountingAdministrationGovernance.cs").Single();
        var source = File.ReadAllText(migration);
        Assert.Contains("accounting_account_lifecycle_history", source, StringComparison.Ordinal);
        Assert.Contains("accounting_series_policies", source, StringComparison.Ordinal);
        Assert.Contains("accounting_voucher_gap_evidence", source, StringComparison.Ordinal);
        Assert.Contains("accounting_commerce_event_receipts", source, StringComparison.Ordinal);
        Assert.Contains("scope_key", source, StringComparison.Ordinal);
        Assert.Contains("replacement_account_id", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO accounting_account_lifecycle_history", source, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory_quantity", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
