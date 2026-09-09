using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingSessionConfiguration : IEntityTypeConfiguration<SalesMeetingSession>
{
    public void Configure(EntityTypeBuilder<SalesMeetingSession> builder)
    {
        builder.ToTable("sales_meeting_sessions", table =>
        {
            table.HasCheckConstraint("CK_sales_meeting_sessions_duration", "planned_duration_minutes >= 5 AND planned_duration_minutes <= 480");
            table.HasCheckConstraint("CK_sales_meeting_sessions_slide", "current_slide_index >= 0 AND current_talking_point_index >= 0");
            table.HasCheckConstraint("CK_sales_meeting_sessions_retention", "retention_days >= 1 AND retention_days <= 3650");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.PresenterAgentId).HasColumnName("presenter_agent_id");
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.InvitationId).HasColumnName("invitation_id").IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id").IsRequired();
        builder.Property(x => x.MeetingGoal).HasColumnName("meeting_goal").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.IntendedAudience).HasColumnName("intended_audience").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.PlannedDurationMinutes).HasColumnName("planned_duration_minutes").IsRequired();
        builder.Property(x => x.DemoScenario).HasColumnName("demo_scenario").HasMaxLength(4000);
        builder.Property(x => x.ProviderMeetingId).HasColumnName("provider_meeting_id").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => SalesMeetingSessionStatusValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.CurrentSlideIndex).HasColumnName("current_slide_index").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CurrentTalkingPointIndex).HasColumnName("current_talking_point_index").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ResumeMarker).HasColumnName("resume_marker").HasMaxLength(1000);
        builder.Property(x => x.ConsentStatus).HasColumnName("consent_status")
            .HasConversion(value => value.ToStorageValue(), value => SalesMeetingConsentStatusValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConsentRecordedUtc).HasColumnName("consent_recorded_at");
        builder.Property(x => x.ConsentRecordedByUserId).HasColumnName("consent_recorded_by_user_id");
        builder.Property(x => x.RetentionPolicy).HasColumnName("retention_policy")
            .HasConversion(value => value.ToStorageValue(), value => SalesMeetingRetentionPolicyValues.Parse(value))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.RetentionDays).HasColumnName("retention_days").IsRequired();
        builder.Property(x => x.RetentionStartsUtc).HasColumnName("retention_starts_at").IsRequired();
        builder.Property(x => x.RetentionUntilUtc).HasColumnName("retention_until_at").IsRequired();
        builder.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(1000);
        builder.Property(x => x.EndedUtc).HasColumnName("ended_at");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version")
            .HasDefaultValue(1L).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.LastPresentationSequence).HasColumnName("last_presentation_sequence")
            .HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.LastPresentationCommandId).HasColumnName("last_presentation_command_id");
        builder.Property(x => x.PresentationControlMode).HasColumnName("presentation_control_mode")
            .HasMaxLength(16).HasDefaultValue("manual").IsRequired();
        builder.Property(x => x.PresentationControlUpdatedByUserId).HasColumnName("presentation_control_updated_by_user_id").IsRequired();
        builder.Property(x => x.PresentationControlUpdatedUtc).HasColumnName("presentation_control_updated_at").IsRequired();
        builder.Property(x => x.CaptureVersion).HasColumnName("capture_version").HasDefaultValue(0L).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.LastCaptureBatchId).HasColumnName("last_capture_batch_id");
        builder.Property(x => x.TranscriptReconciliationVersion).HasColumnName("transcript_reconciliation_version").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.LastTranscriptReconciliationId).HasColumnName("last_transcript_reconciliation_id");

        builder.HasIndex(x => new { x.CompanyId, x.InvitationId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderMeetingId });
        builder.HasIndex(x => new { x.CompanyId, x.RetentionUntilUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Invitation).WithMany()
            .HasForeignKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.InvitationId))
            .HasPrincipalKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Lead).WithMany()
            .HasForeignKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.LeadId))
            .HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal).WithMany()
            .HasForeignKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact).WithMany()
            .HasForeignKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CustomerCompany).WithMany()
            .HasForeignKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.CustomerCompanyId))
            .HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
