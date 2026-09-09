using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingVoiceSessionConfiguration : IEntityTypeConfiguration<SalesMeetingVoiceSession>
{
    public void Configure(EntityTypeBuilder<SalesMeetingVoiceSession> builder)
    {
        builder.ToTable("sales_meeting_voice_sessions", table =>
        {
            table.HasCheckConstraint("CK_sales_meeting_voice_sessions_usage", "audio_duration_ms >= 0 AND input_tokens >= 0 AND output_tokens >= 0 AND reconnect_count >= 0");
            table.HasCheckConstraint("CK_sales_meeting_voice_sessions_expiry", "expires_at > created_at");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MeetingSessionId).HasColumnName("meeting_session_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.StartedByUserId).HasColumnName("started_by_user_id").IsRequired();
        builder.Property(x => x.MediaRoute).HasColumnName("media_route").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.ProviderSessionId).HasColumnName("provider_session_id").HasMaxLength(200);
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(120);
        builder.Property(x => x.MediaTransport).HasColumnName("media_transport").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => SalesMeetingVoiceSessionStatusValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.ConnectedUtc).HasColumnName("connected_at");
        builder.Property(x => x.EndedUtc).HasColumnName("ended_at");
        builder.Property(x => x.LastProviderSequence).HasColumnName("last_provider_sequence").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.ReconnectCount).HasColumnName("reconnect_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.AudioDurationMilliseconds).HasColumnName("audio_duration_ms").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.InputTokens).HasColumnName("input_tokens").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.OutputTokens).HasColumnName("output_tokens").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120);
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").HasDefaultValue(1L).IsConcurrencyToken().IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.MeetingSessionId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderSessionId }).IsUnique().HasFilter("[provider_session_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ExpiresUtc });
        builder.HasOne(x => x.MeetingSession).WithMany()
            .HasForeignKey(nameof(SalesMeetingVoiceSession.CompanyId), nameof(SalesMeetingVoiceSession.MeetingSessionId))
            .HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany()
            .HasForeignKey(nameof(SalesMeetingVoiceSession.CompanyId), nameof(SalesMeetingVoiceSession.AgentId))
            .HasPrincipalKey(nameof(Agent.CompanyId), nameof(Agent.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesMeetingVoiceEventReceiptConfiguration : IEntityTypeConfiguration<SalesMeetingVoiceEventReceipt>
{
    public void Configure(EntityTypeBuilder<SalesMeetingVoiceEventReceipt> builder)
    {
        builder.ToTable("sales_meeting_voice_event_receipts", table =>
            table.HasCheckConstraint("CK_sales_meeting_voice_event_receipts_sequence", "sequence >= 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.VoiceSessionId).HasColumnName("voice_session_id").IsRequired();
        builder.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome")
            .HasConversion(x => x.ToStorageValue(), x => SalesMeetingVoiceEventOutcomeValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.QuestionId).HasColumnName("question_id");
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(8000);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(120);
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.VoiceSessionId, x.ProviderEventId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.VoiceSessionId, x.Sequence });
        builder.HasOne(x => x.VoiceSession).WithMany(x => x.Events)
            .HasForeignKey(nameof(SalesMeetingVoiceEventReceipt.CompanyId), nameof(SalesMeetingVoiceEventReceipt.VoiceSessionId))
            .HasPrincipalKey(nameof(SalesMeetingVoiceSession.CompanyId), nameof(SalesMeetingVoiceSession.Id)).OnDelete(DeleteBehavior.Cascade);
    }
}
