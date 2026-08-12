using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingInvitationConfiguration : IEntityTypeConfiguration<SalesMeetingInvitation>
{
    public void Configure(EntityTypeBuilder<SalesMeetingInvitation> builder)
    {
        builder.ToTable("sales_meeting_invitations");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CalendarConnectionId).HasColumnName("calendar_connection_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion(x => x.ToStorageValue(), x => ExternalAccountProviderValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CalendarId).HasColumnName("calendar_id").HasMaxLength(256).IsRequired();
        builder.Property(x => x.OrganizerEmail).HasColumnName("organizer_email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.AttendeeEmail).HasColumnName("attendee_email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.AttendeeName).HasColumnName("attendee_name").HasMaxLength(160);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.StartsUtc).HasColumnName("starts_at").IsRequired();
        builder.Property(x => x.EndsUtc).HasColumnName("ends_at").IsRequired();
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.CreateOnlineMeeting).HasColumnName("create_online_meeting").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => SalesMeetingInvitationStatusValues.Parse(x)).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(512);
        builder.Property(x => x.ExternalICalUid).HasColumnName("external_ical_uid").HasMaxLength(512);
        builder.Property(x => x.ProviderWebUrl).HasColumnName("provider_web_url").HasMaxLength(2000);
        builder.Property(x => x.OnlineMeetingUrl).HasColumnName("online_meeting_url").HasMaxLength(2000);
        builder.Property(x => x.ConfirmationStatus).HasColumnName("confirmation_status").HasConversion(x => x.ToStorageValue(), x => SalesMeetingConfirmationStatusValues.Parse(x)).HasMaxLength(40).HasDefaultValue(SalesMeetingConfirmationStatus.NotQueued).IsRequired();
        builder.Property(x => x.ConfirmationIdempotencyKey).HasColumnName("confirmation_idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConfirmationMailboxConnectionId).HasColumnName("confirmation_mailbox_connection_id");
        builder.Property(x => x.ConfirmationProviderMessageId).HasColumnName("confirmation_provider_message_id").HasMaxLength(512);
        builder.Property(x => x.ConfirmationProviderThreadId).HasColumnName("confirmation_provider_thread_id").HasMaxLength(512);
        builder.Property(x => x.ConfirmationThreadingMode).HasColumnName("confirmation_threading_mode").HasConversion(x => x.ToStorageValue(), x => MailboxReplyThreadingModeValues.Parse(x)).HasMaxLength(32).HasSentinel((MailboxReplyThreadingMode)0).HasDefaultValue(MailboxReplyThreadingMode.Unknown).IsRequired();
        builder.Property(x => x.ConfirmationAttemptCount).HasColumnName("confirmation_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ConfirmationErrorCode).HasColumnName("confirmation_error_code").HasMaxLength(120);
        builder.Property(x => x.ConfirmationErrorSummary).HasColumnName("confirmation_error_summary").HasMaxLength(1000);
        builder.Property(x => x.ConfirmationSentUtc).HasColumnName("confirmation_sent_at");
        builder.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120);
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.ScheduledUtc).HasColumnName("scheduled_at");
        builder.HasIndex(x => new { x.CompanyId, x.LeadId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ConfirmationIdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).HasFilter("[approval_request_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.ExternalEventId }).HasFilter("[external_event_id] IS NOT NULL");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_sales_meeting_invitations_time_range", "ends_at > starts_at");
            t.HasCheckConstraint("CK_sales_meeting_invitations_confirmation_threading_mode", "confirmation_threading_mode IN ('unknown', 'native', 'header_based')");
        });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Lead).WithMany().HasForeignKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.LeadId)).HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal).WithMany().HasForeignKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.DealId)).HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact).WithMany().HasForeignKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.ContactId)).HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CalendarConnection).WithMany().HasForeignKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.CalendarConnectionId)).HasPrincipalKey(nameof(CalendarConnection.CompanyId), nameof(CalendarConnection.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConfirmationMailboxConnection).WithMany().HasForeignKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.ConfirmationMailboxConnectionId)).HasPrincipalKey(nameof(MailboxConnection.CompanyId), nameof(MailboxConnection.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
