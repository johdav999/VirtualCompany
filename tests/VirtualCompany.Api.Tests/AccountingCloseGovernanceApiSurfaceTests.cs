namespace VirtualCompany.Api.Tests;

public sealed class AccountingCloseGovernanceApiSurfaceTests
{
    [Fact]
    public void Governance_Controller_Exposes_Governed_Write_Workflow()
    {
        var code = File.ReadAllText(SourcePath("src", "VirtualCompany.Api", "Controllers",
            "AccountingCloseGovernanceController.cs"));

        Assert.Contains("readiness/prepare", code, StringComparison.Ordinal);
        Assert.Contains("/submit", code, StringComparison.Ordinal);
        Assert.Contains("/review", code, StringComparison.Ordinal);
        Assert.Contains("/cancel", code, StringComparison.Ordinal);
        Assert.Contains("/lock", code, StringComparison.Ordinal);
        Assert.Contains("/waivers", code, StringComparison.Ordinal);
        Assert.Contains("reopen-requests", code, StringComparison.Ordinal);
        Assert.Contains("CompanyPolicies.AccountingAdmin", code, StringComparison.Ordinal);
        Assert.Contains("ExpectedEvidenceHash", code, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_Implementation_Rechecks_And_Uses_One_Serializable_Transaction()
    {
        var code = File.ReadAllText(SourcePath("src", "VirtualCompany.Infrastructure.Finance", "Finance",
            "AccountingCloseGovernanceService.cs"));

        Assert.Contains("IsolationLevel.Serializable", code, StringComparison.Ordinal);
        Assert.Contains("EvaluateAsync(close, policy", code, StringComparison.Ordinal);
        Assert.Contains("ApprovalRequest.Status == ApprovalRequestStatus.Approved", code, StringComparison.Ordinal);
        Assert.Contains("CurrentDocumentHash", code, StringComparison.Ordinal);
        Assert.Contains("CloseAndLockAsync", code, StringComparison.Ordinal);
        Assert.Contains("MarkLocked", code, StringComparison.Ordinal);
        Assert.Contains("FinanceReportRegeneration", code, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Governance_Migration_Is_Additive_And_Does_Not_Replay_Earlier_P3_Schema()
    {
        var migrationsPath = SourcePath("src", "VirtualCompany.Persistence.Migrations", "Persistence", "Migrations");
        var migrationFiles = Directory.GetFiles(migrationsPath, "*AddAccountingCloseGovernance.cs")
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToArray();

        var migration = Assert.Single(migrationFiles);
        var code = File.ReadAllText(migration);

        Assert.Contains("company_accounting_close_policies", code, StringComparison.Ordinal);
        Assert.Contains("accounting_close_readiness_snapshots", code, StringComparison.Ordinal);
        Assert.Contains("accounting_close_readiness_checks", code, StringComparison.Ordinal);
        Assert.Contains("accounting_close_waivers", code, StringComparison.Ordinal);
        Assert.Contains("accounting_close_reopen_requests", code, StringComparison.Ordinal);
        Assert.Contains("accounting_close_sign_offs", code, StringComparison.Ordinal);
        Assert.DoesNotContain("is_reportable", code, StringComparison.Ordinal);
        Assert.DoesNotContain("accounting_close_templates", code, StringComparison.Ordinal);
        Assert.DoesNotContain("accounting_account_lifecycle_history", code, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
