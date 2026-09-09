using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingTranscriptMigrationTests
{
    [Fact]
    public void Migration_creates_tenant_scoped_idempotent_transcript_storage()
    {
        var migration = new AddSalesMeetingTranscriptReconciliation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToArray();
        Assert.Contains(tables, x => x.Name == "sales_meeting_transcript_subscriptions");
        Assert.Contains(tables, x => x.Name == "sales_meeting_transcript_ingestions");
        Assert.Contains(tables, x => x.Name == "sales_meeting_provider_transcripts");
        Assert.Contains(tables, x => x.Name == "sales_meeting_transcript_provenance");

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Table == "sales_meeting_transcript_ingestions" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "idempotency_key"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_provider_transcripts" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "session_id", "provider_transcript_id"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_transcript_provenance" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "provider_transcript_id", "provider_segment_id", "provider_version"]));

        var provenance = Assert.Single(tables, x => x.Name == "sales_meeting_transcript_provenance");
        Assert.Contains(provenance.ForeignKeys, x => x.PrincipalTable == "sales_meeting_sessions" &&
            x.OnDelete == ReferentialAction.NoAction);
        Assert.Contains(provenance.ForeignKeys, x => x.PrincipalTable == "sales_meeting_provider_transcripts" &&
            x.OnDelete == ReferentialAction.Cascade);
    }

    [Fact]
    public void Model_keeps_one_cascade_path_from_session_to_transcript_provenance()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var provenance = db.Model.FindEntityType(typeof(SalesMeetingTranscriptProvenance))!;
        var sessionForeignKey = Assert.Single(provenance.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType == typeof(SalesMeetingSession));
        var providerTranscriptForeignKey = Assert.Single(provenance.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType == typeof(SalesMeetingProviderTranscript));

        Assert.Equal(DeleteBehavior.NoAction, sessionForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, providerTranscriptForeignKey.DeleteBehavior);
    }
}
