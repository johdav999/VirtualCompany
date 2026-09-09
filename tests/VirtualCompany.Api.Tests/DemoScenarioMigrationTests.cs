using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class DemoScenarioMigrationTests
{
    [Fact]
    public void Model_contains_demo_marker_runs_command_evidence_and_safety_constraints()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var company = db.Model.FindEntityType(typeof(Company))!;
        var run = db.Model.FindEntityType(typeof(DemoScenarioRun))!;
        var execution = db.Model.FindEntityType(typeof(DemoScenarioCommandExecution))!;

        Assert.Equal("is_demo_tenant", company.FindProperty(nameof(Company.IsDemoTenant))!.GetColumnName());
        Assert.Equal("demo_scenario_runs", run.GetTableName());
        Assert.Equal("demo_scenario_command_executions", execution.GetTableName());
        Assert.Contains(run.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Company));
        Assert.Contains(execution.GetIndexes(), x => x.IsUnique &&
            x.Properties.Any(p => p.Name == nameof(DemoScenarioCommandExecution.IdempotencyKey)));
    }
}

