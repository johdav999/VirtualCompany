using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsTenantRegistrationMigrationTests
{
    [Fact]
    public void Migration_enforces_one_to_one_tenant_binding_and_single_use_consent_state()
    {
        var migration = new AddTeamsPresenterTenantIdentity();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var registrations = Assert.Single(builder.Operations.OfType<CreateTableOperation>(),
            item => item.Name == "teams_tenant_registrations");
        var sessions = Assert.Single(builder.Operations.OfType<CreateTableOperation>(),
            item => item.Name == "teams_admin_consent_sessions");
        Assert.Contains(registrations.Columns, item => item.Name == "concurrency_version" && !item.IsNullable);
        Assert.Contains(registrations.CheckConstraints, item => item.Name == "CK_teams_tenant_registration_status");
        Assert.Contains(sessions.Columns, item => item.Name == "state_hash" && !item.IsNullable);
        Assert.DoesNotContain(registrations.Columns, item => item.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                                                             item.Name.Contains("secret", StringComparison.OrdinalIgnoreCase));

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, item => item.Table == registrations.Name && item.IsUnique && item.Columns.SequenceEqual(["company_id"]));
        Assert.Contains(indexes, item => item.Table == registrations.Name && item.IsUnique && item.Columns.SequenceEqual(["entra_tenant_id"]));
        Assert.Contains(indexes, item => item.Table == sessions.Name && item.IsUnique && item.Columns.SequenceEqual(["state_hash"]));
        Assert.Contains(sessions.ForeignKeys, item =>
            item.Columns.SequenceEqual(["company_id", "registration_id"]) &&
            item.PrincipalColumns!.SequenceEqual(["company_id", "id"]));
    }
}
