using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyKnowledgeDocumentConfiguration : IEntityTypeConfiguration<CompanyKnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DocumentType)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceType)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentSourceTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceRef).HasMaxLength(512);
        builder.Property(x => x.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.StorageUrl).HasMaxLength(2048);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(255);
        builder.Property(x => x.FileExtension).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FileSizeBytes).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AccessScope)
            .HasColumnName("access_scope_json")
            .HasJsonConversion<CompanyKnowledgeDocumentAccessScope>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.IngestionStatus)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentIngestionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValueSql("'uploaded'")
            .HasSentinel((CompanyKnowledgeDocumentIngestionStatus)0)
            .IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.FailureMessage).HasMaxLength(2000);
        builder.Property(x => x.FailureAction).HasMaxLength(500);
        builder.Property(x => x.FailureTechnicalDetail).HasMaxLength(4000);
        builder.Property(x => x.ExtractedText);
        builder.Property(x => x.IndexingStatus)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentIndexingStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValueSql("'not_indexed'")
            .HasSentinel((CompanyKnowledgeDocumentIndexingStatus)0)
            .IsRequired();
        builder.Property(x => x.IndexingFailureCode).HasMaxLength(100);
        builder.Property(x => x.IndexingFailureMessage).HasMaxLength(2000);
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200);
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.CurrentChunkSetFingerprint).HasMaxLength(128);
        builder.Property(x => x.CurrentChunkSetVersion).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ActiveChunkCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CanRetry).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.Property(x => x.IndexedUtc);
        builder.Property(x => x.IndexingFailedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IngestionStatus });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus, x.IndexingRequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus, x.IndexingStartedUtc });
        builder.HasOne(x => x.Company).WithMany(x => x.Documents).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

