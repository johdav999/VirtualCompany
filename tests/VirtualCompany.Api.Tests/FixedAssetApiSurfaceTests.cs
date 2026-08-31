using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Api.Tests;

public sealed class FixedAssetApiSurfaceTests
{
    [Theory]
    [InlineData("ListFixedAssetsAsync", CompanyPolicies.AccountingView)]
    [InlineData("GetFixedAssetAsync", CompanyPolicies.AccountingView)]
    [InlineData("ReconcileFixedAssetsAsync", CompanyPolicies.AccountingView)]
    [InlineData("RegisterFixedAssetAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("CapitalizeFixedAssetAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("TransferFixedAssetAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("ImpairFixedAssetAsync", CompanyPolicies.FinanceApproval)]
    [InlineData("DisposeFixedAssetAsync", CompanyPolicies.FinanceApproval)]
    [InlineData("RunFixedAssetDepreciationAsync", CompanyPolicies.FinanceApproval)]
    [InlineData("ReverseFixedAssetEventAsync", CompanyPolicies.FinanceApproval)]
    public void Fixed_asset_endpoints_require_the_expected_company_policy(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == policy);
    }

    [Fact]
    public void Migration_contains_register_runs_events_controls_and_legacy_conflicts()
    {
        var migration = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations",
                "Persistence", "Migrations"), "*_AddFixedAssetSubledger.cs").Single();
        var source = File.ReadAllText(migration);
        Assert.Contains("fixed_asset_classes", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_register_items", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_book_events", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_components", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_depreciation_runs", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_depreciation_run_items", source, StringComparison.Ordinal);
        Assert.Contains("fixed_asset_migration_conflicts", source, StringComparison.Ordinal);
        Assert.Contains("asset_class_hash", source, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Maintenance_worker_is_bounded_and_does_not_own_depreciation_posting()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src",
            "VirtualCompany.Infrastructure.Finance", "Finance",
            "FixedAssetMaintenanceBackgroundService.cs"));
        Assert.Contains(nameof(FixedAssetMaintenanceOptions.CompanyBatchSize), source, StringComparison.Ordinal);
        Assert.Contains("DiscoverLegacyConflictsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunDepreciationAsync", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
