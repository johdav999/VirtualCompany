using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationDeckConfiguration : IEntityTypeConfiguration<SalesPresentationDeck>
{
    public void Configure(EntityTypeBuilder<SalesPresentationDeck> builder)
    {
        builder.ToTable("sales_presentation_decks", table =>
        {
            table.HasCheckConstraint("CK_sales_presentation_decks_version", "version >= 1 AND processing_version >= 1");
            table.HasCheckConstraint("CK_sales_presentation_decks_counts", "file_size_bytes > 0 AND slide_count >= 0 AND processing_attempt_count >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200);
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.StorageUrl).HasColumnName("storage_url").HasMaxLength(2000);
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => SalesPresentationDeckStatusValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProcessingVersion).HasColumnName("processing_version").IsRequired();
        builder.Property(x => x.ProcessingAttemptCount).HasColumnName("processing_attempt_count").IsRequired();
        builder.Property(x => x.SlideCount).HasColumnName("slide_count").IsRequired();
        builder.Property(x => x.RendererName).HasColumnName("renderer_name").HasMaxLength(100);
        builder.Property(x => x.RendererVersion).HasColumnName("renderer_version").HasMaxLength(50);
        builder.Property(x => x.AnimationHandling).HasColumnName("animation_handling").HasMaxLength(100);
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.CanRetry).HasColumnName("can_retry").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.BriefVersion).HasColumnName("brief_version").IsRequired();
        builder.Property(x => x.BriefRegenerationRequestedUtc).HasColumnName("brief_regeneration_requested_at");
        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ProcessingStartedUtc).HasColumnName("processing_started_at");
        builder.Property(x => x.ProcessedUtc).HasColumnName("processed_at");
        builder.Property(x => x.FailedUtc).HasColumnName("failed_at");
        builder.Property(x => x.ActivatedUtc).HasColumnName("activated_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version")
            .HasDefaultValue(1L).IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ContentHash, x.ProcessingVersion }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ProcessingStartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.IsActive })
            .IsUnique().HasFilter("[is_active] = CAST(1 AS bit)");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Session).WithMany()
            .HasForeignKey(nameof(SalesPresentationDeck.CompanyId), nameof(SalesPresentationDeck.SessionId))
            .HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Agent).WithMany()
            .HasForeignKey(nameof(SalesPresentationDeck.CompanyId), nameof(SalesPresentationDeck.AgentId))
            .HasPrincipalKey(nameof(Agent.CompanyId), nameof(Agent.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
