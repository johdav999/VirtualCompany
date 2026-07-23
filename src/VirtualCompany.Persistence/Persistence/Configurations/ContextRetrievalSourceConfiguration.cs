using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ContextRetrievalSourceConfiguration : IEntityTypeConfiguration<ContextRetrievalSource>
{
    public void Configure(EntityTypeBuilder<ContextRetrievalSource> builder)
    {
        builder.ToTable("context_retrieval_sources");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ParentSourceType).HasMaxLength(64);
        builder.Property(x => x.ParentSourceEntityId).HasMaxLength(128);
        builder.Property(x => x.ParentTitle).HasMaxLength(256);
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Snippet).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SectionId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SectionTitle).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SectionRank).IsRequired();
        builder.Property(x => x.Locator).HasMaxLength(512);
        builder.Property(x => x.Rank).IsRequired();
        builder.Property(x => x.Score);
        builder.Property(x => x.TimestampUtc);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.RetrievalId, x.Rank }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RetrievalId, x.SectionId, x.SectionRank });
        builder.HasIndex(x => new { x.CompanyId, x.ParentSourceType, x.ParentSourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceEntityId });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Retrieval)
            .WithMany(x => x.Sources)
            .HasForeignKey(x => x.RetrievalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

