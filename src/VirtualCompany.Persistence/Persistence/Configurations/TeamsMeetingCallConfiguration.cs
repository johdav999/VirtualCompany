using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class TeamsMeetingCallConfiguration : IEntityTypeConfiguration<TeamsMeetingCall>
{
    public void Configure(EntityTypeBuilder<TeamsMeetingCall> b)
    {
        b.ToTable("teams_meeting_calls", t => t.HasCheckConstraint("CK_teams_meeting_call_state",
            "state IN ('requested','joining','waiting_in_lobby','admitted','connected','leave_requested','ending','ended','rejected','failed','reconciliation_required')"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.FirstUatAuthorizedUtc).HasColumnName("first_uat_authorized_at");
        b.Property(x => x.PresenterAgentId).HasColumnName("presenter_agent_id");
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.MeetingSessionId).HasColumnName("meeting_session_id"); b.Property(x => x.RegistrationId).HasColumnName("registration_id");
        b.Property(x => x.OrganizerUserId).HasColumnName("organizer_user_id"); b.Property(x => x.ProviderCallId).HasColumnName("provider_call_id").HasMaxLength(512);
        b.Property(x => x.MeetingReferenceHash).HasColumnName("meeting_reference_hash").HasMaxLength(128).IsRequired();
        b.Property(x => x.SafeProviderReference).HasColumnName("safe_provider_reference").HasMaxLength(160);
        b.Property(x => x.State).HasColumnName("state").HasMaxLength(40).IsRequired(); b.Property(x => x.ProviderState).HasColumnName("provider_state").HasMaxLength(64);
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(32).IsRequired(); b.Property(x => x.ActionVersion).HasColumnName("action_version");
        b.Property(x => x.JoinGeneration).HasColumnName("join_generation");
        b.Property(x => x.JoinIdempotencyKey).HasColumnName("join_idempotency_key").HasMaxLength(300).IsRequired();
        b.Property(x => x.LastCallbackSequence).HasColumnName("last_callback_sequence"); b.Property(x => x.LastCallbackVersion).HasColumnName("last_callback_version").HasMaxLength(160);
        b.Property(x => x.MediaHostInstanceId).HasColumnName("media_host_instance_id").HasMaxLength(120).IsRequired();
        b.Property(x => x.MediaStartAuthorizedByUserId).HasColumnName("media_start_authorized_by_user_id");
        b.Property(x => x.MediaStartAuthorizedUtc).HasColumnName("media_start_authorized_at");
        b.Property(x => x.ConsentEvidenceVersion).HasColumnName("consent_evidence_version"); b.Property(x => x.PolicyEvidenceVersion).HasColumnName("policy_evidence_version");
        b.Property(x => x.RetryCount).HasColumnName("retry_count"); b.Property(x => x.ReconciliationCount).HasColumnName("reconciliation_count");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(120); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.RequestedUtc).HasColumnName("requested_at"); b.Property(x => x.AdmittedUtc).HasColumnName("admitted_at"); b.Property(x => x.ConnectedUtc).HasColumnName("connected_at");
        b.Property(x => x.EndingUtc).HasColumnName("ending_at"); b.Property(x => x.EndedUtc).HasColumnName("ended_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.MeetingSessionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ProviderCallId }).IsUnique().HasFilter("provider_call_id IS NOT NULL");
        b.HasIndex(x => new { x.State, x.MediaHostInstanceId });
        b.HasOne<SalesMeetingSession>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MeetingSessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TeamsTenantRegistration>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RegistrationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamsCallNotificationReceiptConfiguration : IEntityTypeConfiguration<TeamsCallNotificationReceipt>
{
    public void Configure(EntityTypeBuilder<TeamsCallNotificationReceipt> b)
    {
        b.ToTable("teams_call_notification_receipts"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CallId).HasColumnName("call_id");
        b.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(128).IsRequired(); b.Property(x => x.ResourceVersion).HasColumnName("resource_version").HasMaxLength(160).IsRequired();
        b.Property(x => x.Sequence).HasColumnName("sequence"); b.Property(x => x.NormalizedState).HasColumnName("normalized_state").HasMaxLength(64).IsRequired();
        b.Property(x => x.ReceivedUtc).HasColumnName("received_at"); b.Property(x => x.ProcessedUtc).HasColumnName("processed_at"); b.Property(x => x.Ignored).HasColumnName("ignored");
        b.HasIndex(x => new { x.CompanyId, x.EventKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CallId, x.ProcessedUtc });
        b.HasOne<TeamsMeetingCall>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CallId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
