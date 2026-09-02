using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyResponsibilityMigrationTests
{
    [Fact]
    public void Migration_is_tenant_scoped_unique_and_safely_backfills_unambiguous_owners()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddCompanyResponsibilityOwnership();
        typeof(AddCompanyResponsibilityOwnership).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "company_responsibility_assignments");
        Assert.Contains(table.Columns, x => x.Name == "CompanyId" && !x.IsNullable);
        var primary = Assert.Single(builder.Operations.OfType<CreateIndexOperation>(), x => x.Name == "UX_company_responsibility_primary");
        Assert.True(primary.IsUnique);
        Assert.Equal("[assignment_kind] = N'primary'", primary.Filter);
        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("HAVING COUNT_BIG(*) = 1", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("match_count = 1", sql, StringComparison.Ordinal);
    }
}
