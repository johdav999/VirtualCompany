using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Finance.Tests;

public sealed class ReportDefinitionTests
{
    [Fact]
    public void Safe_formula_engine_evaluates_only_line_references_and_arithmetic()
    {
        var analysis = ReportFormulaEngine.Analyze("SUM([OPERATING], [INVESTING]) - [FINANCING] / 2");

        Assert.True(analysis.IsValid);
        Assert.Equal(["FINANCING", "INVESTING", "OPERATING"], analysis.References);
        Assert.Equal(115m, ReportFormulaEngine.Evaluate("SUM([OPERATING], [INVESTING]) - [FINANCING] / 2",
            new Dictionary<string, decimal> { ["OPERATING"] = 100m, ["INVESTING"] = 25m, ["FINANCING"] = 20m }));
    }

    [Theory]
    [InlineData("System.Diagnostics.Process.Start('cmd')")]
    [InlineData("SELECT * FROM journal_entries")]
    [InlineData("[OTHER_TENANT:REVENUE]")]
    [InlineData("1 / 0")]
    public void Safe_formula_engine_rejects_executable_query_cross_scope_and_invalid_expressions(string formula)
    {
        var analysis = ReportFormulaEngine.Analyze(formula);
        if (formula == "1 / 0")
            Assert.ThrowsAny<Exception>(() => ReportFormulaEngine.Evaluate(formula, new Dictionary<string, decimal>()));
        else
            Assert.False(analysis.IsValid);
    }

    [Fact]
    public void Formula_cycle_detection_returns_the_involved_path()
    {
        var cycles = ReportFormulaEngine.FindCycles(new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = ["B"], ["B"] = ["C"], ["C"] = ["A"], ["SAFE"] = []
        });

        Assert.Single(cycles);
        Assert.Equal(["A", "B", "C", "A"], cycles[0]);
    }

    [Fact]
    public void Version_lifecycle_requires_validation_revision_and_approval_before_activation()
    {
        var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var version = NewVersion(now);

        Assert.Throws<InvalidOperationException>(() => version.Submit(1, now));
        version.MarkValidated(new string('a', 64), 1, now);
        Assert.Throws<ReportDefinitionConcurrencyException>(() => version.Submit(1, now));
        version.Submit(2, now);
        Assert.Throws<InvalidOperationException>(() => version.Activate(new DateOnly(2026, 9, 1), 3, now));
        version.Approve(3, now);
        version.Activate(new DateOnly(2026, 9, 1), 4, now);

        Assert.Equal(ReportDefinitionVersionStatuses.Active, version.Status);
        Assert.True(version.IsEffectiveOn(new DateOnly(2026, 9, 1)));
        Assert.False(version.IsEffectiveOn(new DateOnly(2026, 8, 31)));

        version.Retire(new DateOnly(2026, 10, 1), 5, now);
        Assert.True(version.IsEffectiveOn(new DateOnly(2026, 9, 30)));
        Assert.False(version.IsEffectiveOn(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Snapshot_keeps_exact_definition_version_and_hash()
    {
        var versionId = Guid.NewGuid();
        var hash = new string('b', 64);
        var snapshot = new FinancialReportSuiteSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cash_flow",
            "v1", "mapping-v1", new string('c', 64), new string('d', 64), "{}", Guid.NewGuid(), "capture-1",
            DateTime.UtcNow, versionId, 7, hash);

        Assert.Equal(versionId, snapshot.ReportDefinitionVersionId);
        Assert.Equal(7, snapshot.ReportDefinitionVersionNumber);
        Assert.Equal(hash, snapshot.ReportDefinitionHash);
    }

    [Fact]
    public void Csv_export_embeds_the_exact_definition_identity()
    {
        var versionId = Guid.NewGuid();
        var hash = new string('e', 64);
        var report = new VirtualCompany.Application.Finance.CompleteFinancialReportDto(Guid.NewGuid(), Guid.NewGuid(),
            "2026-08", "cash_flow", DateTime.UtcNow, DateTime.UtcNow, new DateOnly(2026, 8, 31), "SEK", "v1",
            "mapping-v1", new string('a', 64), new string('b', 64), true, true, true, Guid.NewGuid(), DateTime.UtcNow,
            [], new(0, 0, 0, 0, 0, true), [], 1, 200, 0, false, 2_000, 10, versionId, 4, hash);

        var csv = System.Text.Encoding.UTF8.GetString(FinancialReportExportFormatter.ToCsv(report));

        Assert.Contains(versionId.ToString("D"), csv);
        Assert.Contains("# definition_version_number,\"4\"", csv);
        Assert.Contains(hash, csv);
    }

    [Fact]
    public void Persistence_model_enforces_tenant_keys_idempotency_and_optimistic_concurrency()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);

        var definition = Entity<ReportDefinition>(db);
        AssertIndex(definition, true, nameof(ReportDefinition.CompanyId), nameof(ReportDefinition.Code));

        var version = Entity<ReportDefinitionVersion>(db);
        Assert.True(version.FindProperty(nameof(ReportDefinitionVersion.Revision))!.IsConcurrencyToken);
        AssertIndex(version, true, nameof(ReportDefinitionVersion.CompanyId), nameof(ReportDefinitionVersion.DefinitionId),
            nameof(ReportDefinitionVersion.VersionNumber));

        var receipt = Entity<ReportDefinitionCommandReceipt>(db);
        AssertIndex(receipt, true, nameof(ReportDefinitionCommandReceipt.CompanyId),
            nameof(ReportDefinitionCommandReceipt.IdempotencyKey));

        var snapshot = Entity<FinancialReportSuiteSnapshot>(db);
        AssertIndex(snapshot, false, nameof(FinancialReportSuiteSnapshot.CompanyId),
            nameof(FinancialReportSuiteSnapshot.ReportDefinitionVersionId));
    }

    [Fact]
    public void Every_report_definition_record_is_tenant_filtered()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        Type[] types =
        [
            typeof(ReportDefinition), typeof(ReportDefinitionVersion), typeof(ReportDefinitionSection),
            typeof(ReportDefinitionLine), typeof(ReportDefinitionAccountGroup),
            typeof(ReportDefinitionAccountGroupMember), typeof(ReportDefinitionComparison),
            typeof(ReportDefinitionValidationResult), typeof(ReportDefinitionValidationIssue),
            typeof(ReportDefinitionApproval), typeof(ReportDefinitionCommandReceipt)
        ];

        foreach (var type in types)
            Assert.NotNull(db.Model.FindEntityType(type)?.GetQueryFilter());
    }

    [Fact]
    public void Report_definition_roles_separate_administration_from_approval()
    {
        Assert.True(FinanceAccess.CanManageAccounting("manager"));
        Assert.True(FinanceAccess.CanApproveInvoices("manager"));
        Assert.False(FinanceAccess.CanManageAccounting("finance_approver"));
        Assert.True(FinanceAccess.CanApproveInvoices("finance_approver"));
        Assert.False(FinanceAccess.CanManageAccounting("tester"));
        Assert.False(FinanceAccess.CanApproveInvoices("tester"));
    }

    private static ReportDefinitionVersion NewVersion(DateTime now) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        1, "Management cash flow", "cash_flow", Guid.NewGuid(), now);

    private static IEntityType Entity<T>(VirtualCompanyDbContext db) =>
        db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is missing.");

    private static void AssertIndex(IEntityType entity, bool unique, params string[] properties) =>
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique == unique &&
            index.Properties.Select(property => property.Name).SequenceEqual(properties));
}
