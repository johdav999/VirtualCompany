using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingRealtimeMigrationTests
{
    [Fact]
    public void Migration_adds_tenant_safe_voice_sessions_and_idempotent_event_receipts()
    {
        var migration = new AddSalesMeetingRealtimeVoicePilot();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var sessions = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "sales_meeting_voice_sessions");
        var events = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "sales_meeting_voice_event_receipts");
        Assert.Contains(sessions.Columns, x => x.Name == "concurrency_version");
        Assert.Contains(sessions.Columns, x => x.Name == "audio_duration_ms");
        Assert.Contains(events.Columns, x => x.Name == "provider_event_id");
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), x => x.Table == events.Name && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "voice_session_id", "provider_event_id"]));
        Assert.All(events.ForeignKeys, key => Assert.Contains("company_id", key.Columns));

        var agentForeignKey = Assert.Single(sessions.ForeignKeys, key => key.PrincipalTable == "agents");
        Assert.Equal(["company_id", "agent_id"], agentForeignKey.Columns);
        var principalColumns = Assert.IsType<string[]>(agentForeignKey.PrincipalColumns);
        Assert.Equal(["CompanyId", "Id"], principalColumns);
    }
}
