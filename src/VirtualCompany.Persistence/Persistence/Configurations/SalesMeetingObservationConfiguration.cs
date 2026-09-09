using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingObservationConfiguration : IEntityTypeConfiguration<SalesMeetingObservation>
{
    public void Configure(EntityTypeBuilder<SalesMeetingObservation> builder)
    {
        builder.ToTable("sales_meeting_observations", table =>
        {
            table.HasCheckConstraint("CK_sales_meeting_observations_sequence", "sequence_number > 0");
            table.HasCheckConstraint("CK_sales_meeting_observations_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
        });
        builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.ClientItemId).HasColumnName("client_item_id"); builder.Property(x => x.Sequence).HasColumnName("sequence_number");
        builder.Property(x => x.Category).HasColumnName("category").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseObservationCategory(v)).HasMaxLength(48);
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000); builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(500);
        builder.Property(x => x.ReviewState).HasColumnName("review_state").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseReviewState(v)).HasMaxLength(32);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.LastClientBatchId).HasColumnName("last_client_batch_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ClientItemId }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingObservation.CompanyId), nameof(SalesMeetingObservation.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
    }
}
