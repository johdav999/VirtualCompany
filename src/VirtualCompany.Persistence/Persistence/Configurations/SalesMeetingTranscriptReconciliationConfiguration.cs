using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingTranscriptSubscriptionConfiguration : IEntityTypeConfiguration<SalesMeetingTranscriptSubscription>
{
    public void Configure(EntityTypeBuilder<SalesMeetingTranscriptSubscription> builder)
    {
        builder.ToTable("sales_meeting_transcript_subscriptions"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.CalendarConnectionId).HasColumnName("calendar_connection_id");
        builder.Property(x => x.ProviderMeetingId).HasColumnName("provider_meeting_id").HasMaxLength(512); builder.Property(x => x.ProviderOnlineMeetingId).HasColumnName("provider_online_meeting_id").HasMaxLength(512); builder.Property(x => x.ProviderSubscriptionId).HasColumnName("provider_subscription_id").HasMaxLength(256); builder.Property(x => x.ProviderResource).HasColumnName("provider_resource").HasMaxLength(1000); builder.Property(x => x.ClientStateHash).HasColumnName("client_state_hash").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(v => v.ToStorageValue(), v => SalesMeetingTranscriptReconciliationEnumValues.ParseSubscriptionStatus(v)).HasMaxLength(32); builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at"); builder.Property(x => x.RetentionUntilUtc).HasColumnName("retention_until_at"); builder.Property(x => x.LastNotificationUtc).HasColumnName("last_notification_at"); builder.Property(x => x.LastRenewedUtc).HasColumnName("last_renewed_at"); builder.Property(x => x.RenewalAttemptCount).HasColumnName("renewal_attempt_count"); builder.Property(x => x.AuthenticityFailureCount).HasColumnName("authenticity_failure_count");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120); builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000); builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.CreatedUtc).HasColumnName("created_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId }); builder.HasIndex(x => x.ProviderSubscriptionId).IsUnique(); builder.HasIndex(x => new { x.Status, x.ExpiresUtc }); builder.HasIndex(x => x.RetentionUntilUtc);
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptSubscription.CompanyId), nameof(SalesMeetingTranscriptSubscription.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CalendarConnection).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptSubscription.CompanyId), nameof(SalesMeetingTranscriptSubscription.CalendarConnectionId)).HasPrincipalKey(nameof(CalendarConnection.CompanyId), nameof(CalendarConnection.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesMeetingTranscriptIngestionConfiguration : IEntityTypeConfiguration<SalesMeetingTranscriptIngestion>
{
    public void Configure(EntityTypeBuilder<SalesMeetingTranscriptIngestion> builder)
    {
        builder.ToTable("sales_meeting_transcript_ingestions"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id"); builder.Property(x => x.ProviderMeetingId).HasColumnName("provider_meeting_id").HasMaxLength(512); builder.Property(x => x.ProviderTranscriptId).HasColumnName("provider_transcript_id").HasMaxLength(512); builder.Property(x => x.ProviderVersion).HasColumnName("provider_version").HasMaxLength(512); builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(v => v.ToStorageValue(), v => SalesMeetingTranscriptReconciliationEnumValues.ParseIngestionStatus(v)).HasMaxLength(32); builder.Property(x => x.ReceivedUtc).HasColumnName("received_at"); builder.Property(x => x.RetentionUntilUtc).HasColumnName("retention_until_at"); builder.Property(x => x.AttemptCount).HasColumnName("attempt_count"); builder.Property(x => x.EquivalentCount).HasColumnName("equivalent_count"); builder.Property(x => x.AddedCount).HasColumnName("added_count"); builder.Property(x => x.SpeakerCorrectionCount).HasColumnName("speaker_correction_count"); builder.Property(x => x.ConflictCount).HasColumnName("conflict_count"); builder.Property(x => x.MateriallyChanged).HasColumnName("materially_changed");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(120); builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); builder.Property(x => x.ProcessingStartedUtc).HasColumnName("processing_started_at"); builder.Property(x => x.CompletedUtc).HasColumnName("completed_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ReceivedUtc }); builder.HasIndex(x => new { x.Status, x.UpdatedUtc }); builder.HasIndex(x => x.RetentionUntilUtc);
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptIngestion.CompanyId), nameof(SalesMeetingTranscriptIngestion.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Subscription).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptIngestion.CompanyId), nameof(SalesMeetingTranscriptIngestion.SubscriptionId)).HasPrincipalKey(nameof(SalesMeetingTranscriptSubscription.CompanyId), nameof(SalesMeetingTranscriptSubscription.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesMeetingProviderTranscriptConfiguration : IEntityTypeConfiguration<SalesMeetingProviderTranscript>
{
    public void Configure(EntityTypeBuilder<SalesMeetingProviderTranscript> builder)
    {
        builder.ToTable("sales_meeting_provider_transcripts"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id"); builder.Property(x => x.ProviderTranscriptId).HasColumnName("provider_transcript_id").HasMaxLength(512); builder.Property(x => x.ProviderVersion).HasColumnName("provider_version").HasMaxLength(512); builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128); builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasMaxLength(4000); builder.Property(x => x.ProviderCreatedUtc).HasColumnName("provider_created_at"); builder.Property(x => x.FetchedUtc).HasColumnName("fetched_at"); builder.Property(x => x.RetentionUntilUtc).HasColumnName("retention_until_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ProviderTranscriptId }).IsUnique(); builder.HasIndex(x => x.RetentionUntilUtc);
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingProviderTranscript.CompanyId), nameof(SalesMeetingProviderTranscript.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Subscription).WithMany().HasForeignKey(nameof(SalesMeetingProviderTranscript.CompanyId), nameof(SalesMeetingProviderTranscript.SubscriptionId)).HasPrincipalKey(nameof(SalesMeetingTranscriptSubscription.CompanyId), nameof(SalesMeetingTranscriptSubscription.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesMeetingTranscriptProvenanceConfiguration : IEntityTypeConfiguration<SalesMeetingTranscriptProvenance>
{
    public void Configure(EntityTypeBuilder<SalesMeetingTranscriptProvenance> builder)
    {
        builder.ToTable("sales_meeting_transcript_provenance"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id"); builder.Property(x => x.ProviderTranscriptId).HasColumnName("provider_transcript_id"); builder.Property(x => x.TranscriptSegmentId).HasColumnName("transcript_segment_id"); builder.Property(x => x.ProviderSegmentId).HasColumnName("provider_segment_id").HasMaxLength(256); builder.Property(x => x.ProviderVersion).HasColumnName("provider_version").HasMaxLength(512); builder.Property(x => x.ProviderContentHash).HasColumnName("provider_content_hash").HasMaxLength(128); builder.Property(x => x.ProviderContent).HasColumnName("provider_content").HasMaxLength(8000); builder.Property(x => x.ProviderSpeakerLabel).HasColumnName("provider_speaker_label").HasMaxLength(160); builder.Property(x => x.ProviderStartedUtc).HasColumnName("provider_started_at"); builder.Property(x => x.ProviderEndedUtc).HasColumnName("provider_ended_at");
        builder.Property(x => x.MatchKind).HasColumnName("match_kind").HasConversion(v => v.ToStorageValue(), v => SalesMeetingTranscriptReconciliationEnumValues.ParseMatchKind(v)).HasMaxLength(32); builder.Property(x => x.BeforeContent).HasColumnName("before_content").HasMaxLength(8000); builder.Property(x => x.BeforeSpeakerLabel).HasColumnName("before_speaker_label").HasMaxLength(160); builder.Property(x => x.ConflictSummary).HasColumnName("conflict_summary").HasMaxLength(1000); builder.Property(x => x.RequiresReview).HasColumnName("requires_review"); builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.ProviderTranscriptId, x.ProviderSegmentId, x.ProviderVersion }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.RequiresReview });
        // ProviderTranscript already cascades from Session; keep the direct tenant-scoped
        // relationship non-cascading so SQL Server has one deterministic delete path.
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptProvenance.CompanyId), nameof(SalesMeetingTranscriptProvenance.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ProviderTranscript).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptProvenance.CompanyId), nameof(SalesMeetingTranscriptProvenance.ProviderTranscriptId)).HasPrincipalKey(nameof(SalesMeetingProviderTranscript.CompanyId), nameof(SalesMeetingProviderTranscript.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.TranscriptSegment).WithMany().HasForeignKey(nameof(SalesMeetingTranscriptProvenance.CompanyId), nameof(SalesMeetingTranscriptProvenance.TranscriptSegmentId)).HasPrincipalKey(nameof(SalesMeetingTranscriptSegment.CompanyId), nameof(SalesMeetingTranscriptSegment.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
