using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingTranscriptSegmentConfiguration : IEntityTypeConfiguration<SalesMeetingTranscriptSegment>
{
    public void Configure(EntityTypeBuilder<SalesMeetingTranscriptSegment> builder)
    {
        builder.ToTable("sales_meeting_transcript_segments", table =>
        {
            table.HasCheckConstraint("CK_sales_meeting_transcript_segments_sequence", "sequence_number > 0");
            table.HasCheckConstraint("CK_sales_meeting_transcript_segments_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
        });
        builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.ClientItemId).HasColumnName("client_item_id");
        builder.Property(x => x.Sequence).HasColumnName("sequence_number");
        builder.Property(x => x.SpeakerType).HasColumnName("speaker_type").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseSpeakerType(v)).HasMaxLength(32);
        builder.Property(x => x.SpeakerLabel).HasColumnName("speaker_label").HasMaxLength(160);
        builder.Property(x => x.InputSource).HasColumnName("input_source").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseInputSource(v)).HasMaxLength(32);
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(8000);
        builder.Property(x => x.StartedUtc).HasColumnName("started_at"); builder.Property(x => x.EndedUtc).HasColumnName("ended_at");
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.ReviewState).HasColumnName("review_state").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseReviewState(v)).HasMaxLength(32);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.LastClientBatchId).HasColumnName("last_client_batch_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ClientItemId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptSegment.CompanyId), nameof(SalesMeetingTranscriptSegment.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
    }
}
