using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationDeckMigrationTests
{
    [Fact]
    public void Migration_contains_versioned_tenant_safe_deck_slide_and_artifact_schema()
    {
        var operations = UpOperations(new AddSalesPresentationDeckProcessing());
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);

        var deck = tables["sales_presentation_decks"];
        Assert.Contains(deck.Columns, column => column.Name == "content_hash" && !column.IsNullable);
        Assert.Contains(deck.Columns, column => column.Name == "processing_version" && !column.IsNullable);
        Assert.Contains(deck.ForeignKeys, key =>
            key.PrincipalTable == "sales_meeting_sessions" &&
            key.Columns.SequenceEqual(["company_id", "session_id"]) &&
            key.PrincipalColumns!.SequenceEqual(["company_id", "id"]));

        var slide = tables["sales_presentation_slides"];
        Assert.Contains(slide.Columns, column => column.Name == "image_width_pixels" && !column.IsNullable);
        Assert.Contains(slide.Columns, column => column.Name == "image_height_pixels" && !column.IsNullable);
        Assert.Contains(slide.ForeignKeys, key =>
            key.PrincipalTable == "sales_presentation_decks" &&
            key.Columns.SequenceEqual(["company_id", "deck_id"]));

        var artifact = tables["sales_meeting_artifacts"];
        Assert.Contains(artifact.Columns, column => column.Name == "classification" && !column.IsNullable);
        Assert.Contains(artifact.Columns, column => column.Name == "source_id");

        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, index =>
            index.Table == "sales_presentation_decks" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "session_id", "content_hash", "processing_version"]));
        Assert.Contains(indexes, index =>
            index.Table == "sales_presentation_decks" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "session_id", "is_active"]) &&
            !string.IsNullOrWhiteSpace(index.Filter));
        Assert.Contains(indexes, index =>
            index.Table == "sales_presentation_slides" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "deck_id", "processing_version", "slide_number"]));
    }

    private static IReadOnlyList<MigrationOperation> UpOperations(Migration migration)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);
        return builder.Operations;
    }
}
