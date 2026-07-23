using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyKnowledgeChunkConfiguration : IEntityTypeConfiguration<CompanyKnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChunkSetVersion).IsRequired();
        builder.Property(x => x.ChunkIndex).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Embedding).HasColumnType("vector").IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.EmbeddingDimensions).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.IsActive, x.DocumentId });
        builder.HasIndex(x => new { x.CompanyId, x.DocumentId, x.ChunkSetVersion, x.IsActive });
        builder.HasIndex(x => new { x.DocumentId, x.ChunkSetVersion, x.ChunkIndex }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasOne(x => x.Company)
            .WithMany(x => x.KnowledgeChunks)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

