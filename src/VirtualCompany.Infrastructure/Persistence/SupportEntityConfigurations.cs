using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SupportCaseConfiguration : IEntityTypeConfiguration<SupportCase>
{
    public void Configure(EntityTypeBuilder<SupportCase> builder)
    {
        builder.ToTable("support_cases");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CaseNumber).HasColumnName("case_number").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(240).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Sentiment).HasColumnName("sentiment").HasMaxLength(80);
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("decimal(5,3)");
        builder.Property(x => x.SuggestedNextAction).HasColumnName("suggested_next_action").HasMaxLength(1000);
        builder.Property(x => x.RationaleSummary).HasColumnName("rationale_summary").HasMaxLength(2000);
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.RelatedInvoiceId).HasColumnName("related_invoice_id");
        builder.Property(x => x.RelatedPaymentId).HasColumnName("related_payment_id");
        builder.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");
        builder.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id");
        builder.Property(x => x.FirstResponseDueUtc).HasColumnName("first_response_due_at");
        builder.Property(x => x.ResolutionDueUtc).HasColumnName("resolution_due_at");
        builder.Property(x => x.LastCustomerMessageUtc).HasColumnName("last_customer_message_at");
        builder.Property(x => x.LastInternalActivityUtc).HasColumnName("last_internal_activity_at");
        builder.Property(x => x.FirstResponseSentUtc).HasColumnName("first_response_sent_at");
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        builder.Property(x => x.ClosedUtc).HasColumnName("closed_at");
        builder.Property(x => x.IsSlaRisk).HasColumnName("is_sla_risk").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsSlaBreached).HasColumnName("is_sla_breached").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsVipRisk).HasColumnName("is_vip_risk").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsChurnRisk).HasColumnName("is_churn_risk").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ProviderThreadId).HasColumnName("provider_thread_id").HasMaxLength(256);
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(256);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.CaseNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Priority, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Category, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedUserId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.FirstResponseDueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ResolutionDueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderThreadId });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderMessageId });

        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Contact>().WithMany().HasForeignKey(nameof(SupportCase.CompanyId), nameof(SupportCase.ContactId)).HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CustomerCompany>().WithMany().HasForeignKey(nameof(SupportCase.CompanyId), nameof(SupportCase.CustomerCompanyId)).HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Agent>().WithMany().HasForeignKey(x => x.AssignedAgentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.ToTable("support_messages");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Sender).HasColumnName("sender").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(256);
        builder.Property(x => x.Body).HasColumnName("body").HasColumnType("nvarchar(max)").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.EmailMessageSnapshotId).HasColumnName("email_message_snapshot_id");
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(256);
        builder.Property(x => x.ProviderThreadId).HasColumnName("provider_thread_id").HasMaxLength(256);
        builder.Property(x => x.ReplyDraftId).HasColumnName("reply_draft_id");
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderMessageId }).IsUnique().HasFilter("provider_message_id IS NOT NULL");
        builder.HasOne(x => x.SupportCase).WithMany(x => x.Messages).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportCaseEventConfiguration : IEntityTypeConfiguration<SupportCaseEvent>
{
    public void Configure(EntityTypeBuilder<SupportCaseEvent> builder)
    {
        builder.ToTable("support_case_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ActorType).HasColumnName("actor_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.OccurredUtc });
        builder.HasOne(x => x.SupportCase).WithMany(x => x.Events).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportCaseAssignmentConfiguration : IEntityTypeConfiguration<SupportCaseAssignment>
{
    public void Configure(EntityTypeBuilder<SupportCaseAssignment> builder)
    {
        builder.ToTable("support_case_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");
        builder.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id");
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
        builder.Property(x => x.AssignedUtc).HasColumnName("assigned_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId, x.AssignedUtc });
        builder.HasOne(x => x.SupportCase).WithMany(x => x.Assignments).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportSlaPolicyConfiguration : IEntityTypeConfiguration<SupportSlaPolicy>
{
    public void Configure(EntityTypeBuilder<SupportSlaPolicy> builder)
    {
        builder.ToTable("support_sla_policies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CustomerTier).HasColumnName("customer_tier").HasMaxLength(80);
        builder.Property(x => x.FirstResponseMinutes).HasColumnName("first_response_minutes").IsRequired();
        builder.Property(x => x.ResolutionMinutes).HasColumnName("resolution_minutes").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Category, x.Priority, x.CustomerTier, x.IsActive });
    }
}

internal sealed class SupportCaseResolutionConfiguration : IEntityTypeConfiguration<SupportCaseResolution>
{
    public void Configure(EntityTypeBuilder<SupportCaseResolution> builder)
    {
        builder.ToTable("support_case_resolutions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id").IsRequired();
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId }).IsUnique();
        builder.HasOne(x => x.SupportCase).WithOne(x => x.Resolution).HasForeignKey<SupportCaseResolution>(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey<SupportCase>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportReplyDraftConfiguration : IEntityTypeConfiguration<SupportReplyDraft>
{
    public void Configure(EntityTypeBuilder<SupportReplyDraft> builder)
    {
        builder.ToTable("support_reply_drafts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.DraftBody).HasColumnName("draft_body").HasColumnType("nvarchar(max)").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Tone).HasColumnName("tone").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,3)").IsRequired();
        builder.Property(x => x.Answerability).HasColumnName("answerability").HasColumnType("decimal(5,3)").IsRequired();
        builder.Property(x => x.RationaleSummary).HasColumnName("rationale_summary").HasMaxLength(2000);
        builder.Property(x => x.SourceReferencesJson).HasColumnName("source_references_json").HasColumnType("nvarchar(max)").HasMaxLength(8000);
        builder.Property(x => x.CreatedByAgentId).HasColumnName("created_by_agent_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.SentUtc).HasColumnName("sent_at");
        builder.Property(x => x.SendFailureSummary).HasColumnName("send_failure_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasOne(x => x.SupportCase).WithMany(x => x.ReplyDrafts).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportRefundRequestConfiguration : IEntityTypeConfiguration<SupportRefundRequest>
{
    public void Configure(EntityTypeBuilder<SupportRefundRequest> builder)
    {
        builder.ToTable("support_refund_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        builder.Property(x => x.PaymentId).HasColumnName("payment_id");
        builder.Property(x => x.RequestedByAgentId).HasColumnName("requested_by_agent_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.FinanceActionReferenceId).HasColumnName("finance_action_reference_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasOne(x => x.SupportCase).WithMany(x => x.RefundRequests).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportKnowledgeGapConfiguration : IEntityTypeConfiguration<SupportKnowledgeGap>
{
    public void Configure(EntityTypeBuilder<SupportKnowledgeGap> builder)
    {
        builder.ToTable("support_knowledge_gaps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupportCaseId).HasColumnName("support_case_id");
        builder.Property(x => x.SupportReplyDraftId).HasColumnName("support_reply_draft_id");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(80).IsRequired();
        builder.Property(x => x.QuestionSummary).HasColumnName("question_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.MissingInformationSummary).HasColumnName("missing_information_summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RetrievalSourceSummary).HasColumnName("retrieval_source_summary").HasMaxLength(2000);
        builder.Property(x => x.FrequencyCount).HasColumnName("frequency_count").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        builder.Property(x => x.LinkedTaskId).HasColumnName("linked_task_id");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.Category });
        builder.HasIndex(x => new { x.CompanyId, x.SupportCaseId });
        builder.HasIndex(x => new { x.CompanyId, x.SupportReplyDraftId });
        builder.HasIndex(x => new { x.CompanyId, x.Category, x.QuestionSummary });
        builder.HasOne<SupportCase>().WithMany(x => x.KnowledgeGaps).HasForeignKey(x => new { x.CompanyId, x.SupportCaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<SupportReplyDraft>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SupportReplyDraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

