using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingQuestionConfiguration : IEntityTypeConfiguration<SalesMeetingQuestion>
{
    public void Configure(EntityTypeBuilder<SalesMeetingQuestion> builder)
    {
        builder.ToTable("sales_meeting_questions", table =>
        {
            table.HasCheckConstraint("CK_sales_meeting_questions_sequence", "sequence_number > 0");
            table.HasCheckConstraint("CK_sales_meeting_questions_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
        });
        builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.ClientQuestionId).HasColumnName("client_question_id"); builder.Property(x => x.Sequence).HasColumnName("sequence_number"); builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.QuestionText).HasColumnName("question_text").HasMaxLength(2000); builder.Property(x => x.AnswerText).HasColumnName("answer_text").HasMaxLength(8000);
        builder.Property(x => x.AskerType).HasColumnName("asker_type").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseSpeakerType(v)).HasMaxLength(32);
        builder.Property(x => x.AskerLabel).HasColumnName("asker_label").HasMaxLength(160);
        builder.Property(x => x.InputSource).HasColumnName("input_source").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseInputSource(v)).HasMaxLength(32);
        builder.Property(x => x.VisibleSlideId).HasColumnName("visible_slide_id"); builder.Property(x => x.PresentationVersion).HasColumnName("presentation_version");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseQuestionStatus(v)).HasMaxLength(32);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4); builder.Property(x => x.FollowUpRequired).HasColumnName("follow_up_required");
        builder.Property(x => x.ReviewState).HasColumnName("review_state").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseReviewState(v)).HasMaxLength(32);
        builder.Property(x => x.Visibility).HasColumnName("visibility").HasConversion(v => v.ToStorageValue(), v => SalesMeetingCaptureEnumValues.ParseAnswerVisibility(v)).HasMaxLength(32);
        builder.Property(x => x.AiRunId).HasColumnName("ai_run_id"); builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.AskedByUserId).HasColumnName("asked_by_user_id"); builder.Property(x => x.StageApprovedByUserId).HasColumnName("stage_approved_by_user_id");
        builder.Property(x => x.AskedUtc).HasColumnName("asked_at"); builder.Property(x => x.AnsweredUtc).HasColumnName("answered_at"); builder.Property(x => x.StageApprovedUtc).HasColumnName("stage_approved_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.ClientQuestionId }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(nameof(SalesMeetingQuestion.CompanyId), nameof(SalesMeetingQuestion.SessionId)).HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id)).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(nameof(SalesMeetingQuestion.CompanyId), nameof(SalesMeetingQuestion.AgentId)).HasPrincipalKey(nameof(Agent.CompanyId), nameof(Agent.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.VisibleSlide).WithMany().HasForeignKey(nameof(SalesMeetingQuestion.CompanyId), nameof(SalesMeetingQuestion.VisibleSlideId)).HasPrincipalKey(nameof(SalesPresentationSlide.CompanyId), nameof(SalesPresentationSlide.Id)).OnDelete(DeleteBehavior.NoAction);
    }
}
