using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentAuthorityMigrationTests
{
    [Fact]
    public void Delegation_authority_migration_is_tenant_scoped_indexed_and_reversible()
    {
        var up = Operations<AddFinanceAgentDelegationAuthority>("Up");
        var table = Assert.Single(up.OfType<CreateTableOperation>());
        Assert.Equal("finance_agent_delegation_authorities", table.Name);
        Assert.Contains(table.Columns, column => column.Name == "company_id" && !column.IsNullable);
        Assert.Contains(table.Columns, column => column.Name == "agent_id" && !column.IsNullable);
        Assert.Contains(table.Columns, column => column.Name == "expires_utc" && !column.IsNullable);
        Assert.Contains(up.OfType<CreateIndexOperation>(), index =>
            index.Columns.SequenceEqual(["company_id", "agent_id", "expires_utc"]));
        Assert.DoesNotContain(up, operation => operation is DropTableOperation or DropColumnOperation);

        var down = Operations<AddFinanceAgentDelegationAuthority>("Down");
        Assert.Contains(down.OfType<DropTableOperation>(), operation =>
            operation.Name == "finance_agent_delegation_authorities");
    }

    [Fact]
    public void Effective_authority_binding_migration_is_nullable_and_reversible()
    {
        var up = Operations<AddAgentEffectiveAuthorityVersion>("Up");
        var additions = up.OfType<AddColumnOperation>().ToDictionary(operation => operation.Name);
        Assert.Equal(2, additions.Count);
        Assert.All(additions.Values, operation =>
        {
            Assert.Equal("agent_orchestration_runs", operation.Table);
            Assert.True(operation.IsNullable);
        });

        var down = Operations<AddAgentEffectiveAuthorityVersion>("Down")
            .OfType<DropColumnOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["EffectiveAuthorityHash", "EffectiveAuthorityVersion"], down);
    }

    private static IReadOnlyList<MigrationOperation> Operations<TMigration>(string methodName)
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new TMigration();
        typeof(TMigration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
