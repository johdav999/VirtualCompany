using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class MemoryItemConfiguration : IEntityTypeConfiguration<MemoryItem>
{
    public void Configure(EntityTypeBuilder<MemoryItem> builder)
    {
        builder.ToTable("memory_items", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_memory_items_memory_type", MemoryTypeValues.BuildCheckConstraintSql("\"MemoryType\""));
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.MemoryType)
            .HasConversion(value => value.ToStorageValue(), value => MemoryTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SourceEntityType).HasMaxLength(100);
        builder.Property(x => x.Salience).HasColumnType("numeric(4,3)").IsRequired();
        builder.Property(x => x.ValidFromUtc).IsRequired();
        builder.Property(x => x.ValidToUtc);
        builder.Property(x => x.DeletedUtc);
        builder.Property(x => x.DeletedByActorType).HasMaxLength(64);
        builder.Property(x => x.DeletedByActorId);
        builder.Property(x => x.DeletionReason).HasMaxLength(512);
        builder.Property(x => x.ExpiredByActorType).HasMaxLength(64);
        builder.Property(x => x.ExpiredByActorId);
        builder.Property(x => x.ExpirationReason).HasMaxLength(512);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Embedding).HasColumnType("vector");
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200);
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.AgentId).IsRequired(false);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId });
        builder.HasIndex(x => new { x.CompanyId, x.MemoryType });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DeletedUtc, x.ValidToUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ValidToUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

