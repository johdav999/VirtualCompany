using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingArtifactConfiguration : IEntityTypeConfiguration<SalesMeetingArtifact>
{
    public void Configure(EntityTypeBuilder<SalesMeetingArtifact> builder)
    {
        builder.ToTable("sales_meeting_artifacts", table =>
            table.HasCheckConstraint("CK_sales_meeting_artifacts_order", "artifact_version >= 1 AND artifact_order >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.DeckId).HasColumnName("deck_id").IsRequired();
        builder.Property(x => x.SlideId).HasColumnName("slide_id");
        builder.Property(x => x.ArtifactVersion).HasColumnName("artifact_version").IsRequired();
        builder.Property(x => x.ArtifactType).HasColumnName("artifact_type")
            .HasConversion(value => value.ToStorageValue(), value => SalesMeetingArtifactTypeValues.Parse(value))
            .HasMaxLength(40).IsRequired();
        builder.Property(x => x.Section).HasColumnName("section").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Order).HasColumnName("artifact_order").IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Classification).HasColumnName("classification")
            .HasConversion(value => value.ToStorageValue(), value => SalesMeetingArtifactClassificationValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(500);
        builder.Property(x => x.AiRunId).HasColumnName("ai_run_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.DeckId, x.ArtifactVersion, x.ArtifactType });
        builder.HasIndex(x => new { x.CompanyId, x.SlideId, x.ArtifactVersion, x.Order });
        builder.HasOne(x => x.Session).WithMany()
            .HasForeignKey(nameof(SalesMeetingArtifact.CompanyId), nameof(SalesMeetingArtifact.SessionId))
            .HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deck).WithMany()
            .HasForeignKey(nameof(SalesMeetingArtifact.CompanyId), nameof(SalesMeetingArtifact.DeckId))
            .HasPrincipalKey(nameof(SalesPresentationDeck.CompanyId), nameof(SalesPresentationDeck.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Slide).WithMany()
            .HasForeignKey(nameof(SalesMeetingArtifact.CompanyId), nameof(SalesMeetingArtifact.SlideId))
            .HasPrincipalKey(nameof(SalesPresentationSlide.CompanyId), nameof(SalesPresentationSlide.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
