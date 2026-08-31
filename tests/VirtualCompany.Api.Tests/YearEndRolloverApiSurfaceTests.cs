namespace VirtualCompany.Api.Tests;

public sealed class YearEndRolloverApiSurfaceTests
{
    [Fact]
    public void Controller_exposes_company_scoped_governed_lifecycle_and_stable_problems()
    {
        var code = Read("src", "VirtualCompany.Api", "Controllers", "YearEndRolloverController.cs");

        Assert.Contains("CompanyPolicies.YearEndGovernance", code, StringComparison.Ordinal);
        Assert.Contains("RequireCompanyContext", code, StringComparison.Ordinal);
        Assert.Contains("year-end-runs", code, StringComparison.Ordinal);
        Assert.Contains("readiness/refresh", code, StringComparison.Ordinal);
        Assert.Contains("/submit", code, StringComparison.Ordinal);
        Assert.Contains("/review", code, StringComparison.Ordinal);
        Assert.Contains("/execute", code, StringComparison.Ordinal);
        Assert.Contains("/reconcile", code, StringComparison.Ordinal);
        Assert.Contains("/finalize", code, StringComparison.Ordinal);
        Assert.Contains("subsequent-events", code, StringComparison.Ordinal);
        Assert.Contains("reasonCode", code, StringComparison.Ordinal);
        Assert.Contains("currentVersion", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Execution_rechecks_evidence_and_rolls_back_both_postings_atomically()
    {
        var code = Read("src", "VirtualCompany.Infrastructure.Finance", "Finance", "YearEndRolloverService.cs");

        Assert.Contains("IsolationLevel.Serializable", code, StringComparison.Ordinal);
        Assert.Contains("EvaluateAsync(run", code, StringComparison.Ordinal);
        Assert.Contains("year_end_opening_balance", code, StringComparison.Ordinal);
        Assert.Contains("year_end_retained_earnings", code, StringComparison.Ordinal);
        Assert.Contains("await _posting.PostAsync", code, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync", code, StringComparison.Ordinal);
        Assert.Contains("no partial journal remains", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IdempotencyConflict", code, StringComparison.Ordinal);
        Assert.Contains("year_end_candidate_id", code, StringComparison.Ordinal);
        Assert.Contains("DocumentCurrency", code, StringComparison.Ordinal);
        Assert.Contains("DimensionKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_additive_and_contains_every_year_end_evidence_table()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations", "Persistence", "Migrations");
        var migration = Assert.Single(Directory.GetFiles(directory, "*AddFormalYearEndRollover.cs"),
            x => !x.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var code = File.ReadAllText(migration);
        var required = new[] { "year_end_runs", "year_end_readiness_snapshots", "year_end_retained_earnings_proposals",
            "year_end_opening_balance_candidates", "year_end_approval_signoffs", "year_end_subsequent_events",
            "year_end_history", "year_end_correction_records", "year_end_operations" };
        foreach (var table in required) Assert.Contains(table, code, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
