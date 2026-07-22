using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPipelineStageConfiguration : IEntityTypeConfiguration<SalesPipelineStage>
{
    public void Configure(EntityTypeBuilder<SalesPipelineStage> builder)
    {
        builder.ToTable("sales_pipeline_stages");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsSystem).HasColumnName("is_system").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();

        var seededUtc = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new { Id = SalesPipelineStage.NewStageId, CompanyId = SalesPipelineStage.SystemCompanyId, Name = "New", DisplayOrder = 10, IsSystem = true, IsActive = true, CreatedUtc = seededUtc, UpdatedUtc = seededUtc, IsDeleted = false, DeletedUtc = (DateTime?)null },
            new { Id = SalesPipelineStage.QualifiedStageId, CompanyId = SalesPipelineStage.SystemCompanyId, Name = "Qualified", DisplayOrder = 20, IsSystem = true, IsActive = true, CreatedUtc = seededUtc, UpdatedUtc = seededUtc, IsDeleted = false, DeletedUtc = (DateTime?)null },
            new { Id = SalesPipelineStage.ProposalStageId, CompanyId = SalesPipelineStage.SystemCompanyId, Name = "Proposal", DisplayOrder = 30, IsSystem = true, IsActive = true, CreatedUtc = seededUtc, UpdatedUtc = seededUtc, IsDeleted = false, DeletedUtc = (DateTime?)null },
            new { Id = SalesPipelineStage.WonStageId, CompanyId = SalesPipelineStage.SystemCompanyId, Name = "Won", DisplayOrder = 40, IsSystem = true, IsActive = true, CreatedUtc = seededUtc, UpdatedUtc = seededUtc, IsDeleted = false, DeletedUtc = (DateTime?)null },
            new { Id = SalesPipelineStage.LostStageId, CompanyId = SalesPipelineStage.SystemCompanyId, Name = "Lost", DisplayOrder = 50, IsSystem = true, IsActive = true, CreatedUtc = seededUtc, UpdatedUtc = seededUtc, IsDeleted = false, DeletedUtc = (DateTime?)null });
    }
}

internal sealed class DealIntelligenceSignalConfiguration : IEntityTypeConfiguration<DealIntelligenceSignal>
{
    public void Configure(EntityTypeBuilder<DealIntelligenceSignal> builder)
    {
        builder.ToTable("deal_intelligence_signals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.SequenceId).HasColumnName("sequence_id");
        builder.Property(x => x.SequenceStepId).HasColumnName("sequence_step_id");
        builder.Property(x => x.SignalType).HasColumnName("signal_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SignalState).HasColumnName("signal_state").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceMessageId).HasColumnName("source_message_id").HasMaxLength(256);
        builder.Property(x => x.SourceThreadId).HasColumnName("source_thread_id").HasMaxLength(256);
        builder.Property(x => x.SourceMetadataJson).HasColumnName("source_metadata_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.DetectedUtc).HasColumnName("detected_at").IsRequired();
        builder.Property(x => x.SourceWindowStartedUtc).HasColumnName("source_window_started_at");
        builder.Property(x => x.SourceWindowEndedUtc).HasColumnName("source_window_ended_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_deal_intelligence_signals_confidence_score_range", "confidence_score >= 0 AND confidence_score <= 1");
            t.HasCheckConstraint("CK_deal_intelligence_signals_explanation_required", "LEN(LTRIM(RTRIM(explanation))) > 0");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.DealId);
        builder.HasIndex(x => x.ConversationId);
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SignalType, x.DetectedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DealId, x.DetectedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceMessageId, x.SignalType })
            .IsUnique()
            .HasFilter("[source_message_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.DealId, x.SourceThreadId, x.SignalType })
            .HasFilter("[deal_id] IS NOT NULL AND [source_thread_id] IS NOT NULL");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Deal)
            .WithMany(x => x.IntelligenceSignals)
            .HasForeignKey(nameof(DealIntelligenceSignal.CompanyId), nameof(DealIntelligenceSignal.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerCompanyConfiguration : IEntityTypeConfiguration<CustomerCompany>
{
    public void Configure(EntityTypeBuilder<CustomerCompany> builder)
    {
        builder.ToTable("customer_companies");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        ConfigureTenantColumns(builder);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Website).HasColumnName("website").HasMaxLength(256);
        builder.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(120);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.Name });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTenantColumns(EntityTypeBuilder<CustomerCompany> builder)
    {
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(64);
        builder.Property(x => x.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(20);
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.Email });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CustomerCompany)
            .WithMany(x => x.Contacts)
            .HasForeignKey(nameof(Contact.CompanyId), nameof(Contact.CustomerCompanyId))
            .HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<Contact> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.PrimaryContactId).HasColumnName("primary_contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.PipelineStageId).HasColumnName("pipeline_stage_id").IsRequired();
        builder.Property(x => x.ConvertedDealId).HasColumnName("converted_deal_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EstimatedValue).HasColumnName("estimated_value").HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(120);
        builder.Property(x => x.Fit).HasColumnName("fit").HasMaxLength(80);
        builder.Property(x => x.Temperature).HasColumnName("temperature").HasMaxLength(32);
        builder.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(32);
        builder.Property(x => x.SuggestedNextAction).HasColumnName("suggested_next_action").HasMaxLength(500);
        builder.Property(x => x.QualifiedUtc).HasColumnName("qualified_at");
        builder.Property(x => x.WebsiteSubmissionEmail).HasColumnName("website_submission_email").HasMaxLength(256);
        builder.Property(x => x.WebsiteLeadSubmissionId).HasColumnName("website_lead_submission_id");
        builder.Property(x => x.QualifiedByUserId).HasColumnName("qualified_by_user_id");
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PipelineStage).WithMany(x => x.Leads).HasForeignKey(x => x.PipelineStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PrimaryContact)
            .WithMany()
            .HasForeignKey(nameof(Lead.CompanyId), nameof(Lead.PrimaryContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CustomerCompany)
            .WithMany(x => x.Leads)
            .HasForeignKey(nameof(Lead.CompanyId), nameof(Lead.CustomerCompanyId))
            .HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConvertedDeal)
            .WithMany()
            .HasForeignKey(nameof(Lead.CompanyId), nameof(Lead.ConvertedDealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<Lead> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        builder.ToTable("deals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SourceLeadId).HasColumnName("source_lead_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.PrimaryContactId).HasColumnName("primary_contact_id");
        builder.Property(x => x.PipelineStageId).HasColumnName("pipeline_stage_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExpectedCloseUtc).HasColumnName("expected_close_at");
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PipelineStage).WithMany(x => x.Deals).HasForeignKey(x => x.PipelineStageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceLead)
            .WithMany(x => x.Deals)
            .HasForeignKey(nameof(Deal.CompanyId), nameof(Deal.SourceLeadId))
            .HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CustomerCompany)
            .WithMany(x => x.Deals)
            .HasForeignKey(nameof(Deal.CompanyId), nameof(Deal.CustomerCompanyId))
            .HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PrimaryContact)
            .WithMany()
            .HasForeignKey(nameof(Deal.CompanyId), nameof(Deal.PrimaryContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<Deal> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class SalesActivityConfiguration : IEntityTypeConfiguration<SalesActivity>
{
    public void Configure(EntityTypeBuilder<SalesActivity> builder)
    {
        builder.ToTable("sales_activities");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id");
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.ActivityType).HasColumnName("activity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Lead).WithMany(x => x.Activities).HasForeignKey(nameof(SalesActivity.CompanyId), nameof(SalesActivity.LeadId)).HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal).WithMany(x => x.Activities).HasForeignKey(nameof(SalesActivity.CompanyId), nameof(SalesActivity.DealId)).HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact).WithMany().HasForeignKey(nameof(SalesActivity.CompanyId), nameof(SalesActivity.ContactId)).HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CustomerCompany).WithMany().HasForeignKey(nameof(SalesActivity.CompanyId), nameof(SalesActivity.CustomerCompanyId)).HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id)).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<SalesActivity> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class SalesAgentRecommendationConfiguration : IEntityTypeConfiguration<SalesAgentRecommendation>
{
    public void Configure(EntityTypeBuilder<SalesAgentRecommendation> builder)
    {
        builder.ToTable("sales_agent_recommendations");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id");
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.Recommendation).HasColumnName("recommendation").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).HasDefaultValue("follow_up").IsRequired();
        builder.Property(x => x.TriggerCondition).HasColumnName("trigger_condition").HasMaxLength(80).HasDefaultValue("manual_review").IsRequired();
        builder.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(80).HasDefaultValue("create_draft_reply").IsRequired();
        builder.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(32).HasDefaultValue("medium").IsRequired();
        builder.Property(x => x.RequiresApproval).HasColumnName("requires_approval").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.ApprovalStatus).HasColumnName("approval_status").HasMaxLength(32).HasDefaultValue(SalesStatuses.WaitingForApproval).IsRequired();
        builder.Property(x => x.ExecutionStatus).HasColumnName("execution_status").HasMaxLength(32).HasDefaultValue(SalesStatuses.Pending).IsRequired();
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(256);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,4)");
        builder.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastExecutionErrorCode).HasColumnName("last_execution_error_code").HasMaxLength(120);
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id");
        builder.Property(x => x.ProviderThreadId).HasColumnName("provider_thread_id").HasMaxLength(256);
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(256);
        builder.Property(x => x.ProviderDraftId).HasColumnName("provider_draft_id").HasMaxLength(256);
        builder.Property(x => x.ActivityId).HasColumnName("activity_id");
        builder.Property(x => x.ExecutionIdempotencyKey).HasColumnName("execution_idempotency_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at");
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalStatus });
        builder.HasIndex(x => new { x.CompanyId, x.ExecutionStatus });
        builder.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique().HasFilter("[dedupe_key] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ExecutionIdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.MailboxConnectionId, x.ProviderMessageId }).HasFilter("[provider_message_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.MailboxConnectionId, x.ProviderDraftId }).HasFilter("[provider_draft_id] IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Lead).WithMany(x => x.Recommendations).HasForeignKey(nameof(SalesAgentRecommendation.CompanyId), nameof(SalesAgentRecommendation.LeadId)).HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal).WithMany(x => x.Recommendations).HasForeignKey(nameof(SalesAgentRecommendation.CompanyId), nameof(SalesAgentRecommendation.DealId)).HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MailboxConnection>()
            .WithMany()
            .HasForeignKey(nameof(SalesAgentRecommendation.CompanyId), nameof(SalesAgentRecommendation.MailboxConnectionId))
            .HasPrincipalKey(nameof(MailboxConnection.CompanyId), nameof(MailboxConnection.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesActivity>().WithMany().HasForeignKey(nameof(SalesAgentRecommendation.CompanyId), nameof(SalesAgentRecommendation.ActivityId)).HasPrincipalKey(nameof(SalesActivity.CompanyId), nameof(SalesActivity.Id)).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<SalesAgentRecommendation> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class SalesActionApprovalConfiguration : IEntityTypeConfiguration<SalesActionApproval>
{
    public void Configure(EntityTypeBuilder<SalesActionApproval> builder)
    {
        builder.ToTable("sales_action_approvals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RecommendationId).HasColumnName("recommendation_id");
        builder.Property(x => x.LeadId).HasColumnName("lead_id");
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ActionSummary).HasColumnName("action_summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        ConfigureAudit(builder);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Recommendation).WithMany(x => x.Approvals).HasForeignKey(nameof(SalesActionApproval.CompanyId), nameof(SalesActionApproval.RecommendationId)).HasPrincipalKey(nameof(SalesAgentRecommendation.CompanyId), nameof(SalesAgentRecommendation.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Lead).WithMany().HasForeignKey(nameof(SalesActionApproval.CompanyId), nameof(SalesActionApproval.LeadId)).HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal).WithMany().HasForeignKey(nameof(SalesActionApproval.CompanyId), nameof(SalesActionApproval.DealId)).HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id)).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAudit(EntityTypeBuilder<SalesActionApproval> builder)
    {
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");
    }
}

internal sealed class SalesEmailLinkConfiguration : IEntityTypeConfiguration<SalesEmailLink>
{
    public void Configure(EntityTypeBuilder<SalesEmailLink> builder)
    {
        builder.ToTable("sales_email_links");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(256).IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id");
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id");
        builder.Property(x => x.ExternalThreadId).HasColumnName("external_thread_id").HasMaxLength(256);
        builder.Property(x => x.InternetMessageId).HasColumnName("internet_message_id").HasMaxLength(512);
        builder.Property(x => x.LinkKind).HasColumnName("link_kind").HasMaxLength(32).HasDefaultValue("message").IsRequired();
        builder.Property(x => x.IgnoreReason).HasColumnName("ignore_reason").HasMaxLength(120);
        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(1000);
        builder.Property(x => x.DetectedIntent).HasColumnName("detected_intent").HasMaxLength(120);
        builder.Property(x => x.ProductOrServiceInterest).HasColumnName("product_or_service_interest").HasMaxLength(200);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,4)");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletedUtc).HasColumnName("deleted_at");

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.MailboxConnectionId, x.ExternalMessageId, x.LinkKind }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.MailboxConnectionId, x.ExternalThreadId, x.LinkKind });
        builder.HasIndex(x => new { x.CompanyId, x.LeadId });
        builder.HasIndex(x => new { x.CompanyId, x.DealId });
        builder.HasIndex(x => new { x.CompanyId, x.ContactId });
        builder.HasIndex(x => new { x.CompanyId, x.CustomerCompanyId });
        builder.HasOne<MailboxConnection>()
            .WithMany()
            .HasForeignKey(nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.MailboxConnectionId))
            .HasPrincipalKey(nameof(MailboxConnection.CompanyId), nameof(MailboxConnection.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Lead>().WithMany().HasForeignKey(nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.LeadId)).HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Deal>().WithMany().HasForeignKey(nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.DealId)).HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Contact>().WithMany().HasForeignKey(nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.ContactId)).HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id)).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerCompany>()
            .WithMany()
            .HasForeignKey(nameof(SalesEmailLink.CompanyId), nameof(SalesEmailLink.CustomerCompanyId))
            .HasPrincipalKey(nameof(CustomerCompany.CompanyId), nameof(CustomerCompany.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesSequenceConfiguration : IEntityTypeConfiguration<SalesSequence>
{
    public void Configure(EntityTypeBuilder<SalesSequence> builder)
    {
        builder.ToTable("sales_sequences");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(SalesStatuses.Draft).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.Name });
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesSequenceStepConfiguration : IEntityTypeConfiguration<SalesSequenceStep>
{
    public void Configure(EntityTypeBuilder<SalesSequenceStep> builder)
    {
        builder.ToTable("sales_sequence_steps");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SalesSequenceId).HasColumnName("sales_sequence_id").IsRequired();
        builder.Property(x => x.StepOrder).HasColumnName("step_order").IsRequired();
        builder.Property(x => x.DelayDays).HasColumnName("delay_days").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(32).HasDefaultValue("email").IsRequired();
        builder.Property(x => x.TemplateSubject).HasColumnName("template_subject").HasMaxLength(300);
        builder.Property(x => x.TemplateContent).HasColumnName("template_content").HasColumnType("nvarchar(max)").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.AiPersonalizationEnabled).HasColumnName("ai_personalization_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_sales_sequence_steps_step_order_positive", "step_order > 0");
            t.HasCheckConstraint("CK_sales_sequence_steps_delay_days_non_negative", "delay_days >= 0");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.SalesSequenceId, x.StepOrder }).IsUnique();
        builder.HasOne(x => x.SalesSequence)
            .WithMany(x => x.Steps)
            .HasForeignKey(nameof(SalesSequenceStep.CompanyId), nameof(SalesSequenceStep.SalesSequenceId))
            .HasPrincipalKey(nameof(SalesSequence.CompanyId), nameof(SalesSequence.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesCampaignConfiguration : IEntityTypeConfiguration<SalesCampaign>
{
    public void Configure(EntityTypeBuilder<SalesCampaign> builder)
    {
        builder.ToTable("sales_campaigns");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SalesSequenceId).HasColumnName("sales_sequence_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.AudienceType).HasColumnName("audience_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(SalesStatuses.Draft).IsRequired();
        builder.Property(x => x.CommunicationLanguage).HasColumnName("communication_language").HasMaxLength(20);
        builder.Property(x => x.OutboundEnabled).HasColumnName("outbound_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.MaxEmailsPerDay).HasColumnName("max_emails_per_day").HasDefaultValue(50).IsRequired();
        builder.Property(x => x.ApprovalRequired).HasColumnName("approval_required").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ApprovalRequestedUtc).HasColumnName("approval_requested_at");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.ApprovalStatus).HasColumnName("approval_status").HasMaxLength(32);
        builder.Property(x => x.LaunchRequestedUtc).HasColumnName("launch_requested_at");
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.PausedUtc).HasColumnName("paused_at");
        builder.Property(x => x.StoppedUtc).HasColumnName("stopped_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SalesSequenceId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SalesSequence)
            .WithMany(x => x.Campaigns)
            .HasForeignKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.SalesSequenceId))
            .HasPrincipalKey(nameof(SalesSequence.CompanyId), nameof(SalesSequence.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesCampaignContactConfiguration : IEntityTypeConfiguration<SalesCampaignContact>
{
    public void Configure(EntityTypeBuilder<SalesCampaignContact> builder)
    {
        builder.ToTable("sales_campaign_contacts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(SalesStatuses.Pending).IsRequired();
        builder.Property(x => x.CurrentStepOrder).HasColumnName("current_step_order");
        builder.Property(x => x.EnrolledUtc).HasColumnName("enrolled_at").IsRequired();
        builder.Property(x => x.LastScheduledUtc).HasColumnName("last_scheduled_at");
        builder.Property(x => x.LastSentUtc).HasColumnName("last_sent_at");
        builder.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_sales_campaign_contacts_current_step_order_positive", "current_step_order IS NULL OR current_step_order > 0");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.ContactId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ContactId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.LastScheduledUtc });
        builder.HasOne(x => x.SalesCampaign)
            .WithMany(x => x.Contacts)
            .HasForeignKey(nameof(SalesCampaignContact.CompanyId), nameof(SalesCampaignContact.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact)
            .WithMany()
            .HasForeignKey(nameof(SalesCampaignContact.CompanyId), nameof(SalesCampaignContact.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesSequenceExecutionConfiguration : IEntityTypeConfiguration<SalesSequenceExecution>
{
    public void Configure(EntityTypeBuilder<SalesSequenceExecution> builder)
    {
        builder.ToTable("sales_sequence_executions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.SalesCampaignContactId).HasColumnName("sales_campaign_contact_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.StopReason).HasColumnName("stop_reason").HasMaxLength(80);
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.StoppedUtc).HasColumnName("stopped_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.SalesCampaignId, x.ContactId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ContactId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.SalesCampaign)
            .WithMany()
            .HasForeignKey(nameof(SalesSequenceExecution.CompanyId), nameof(SalesSequenceExecution.SalesCampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesCampaignContact)
            .WithMany()
            .HasForeignKey(nameof(SalesSequenceExecution.CompanyId), nameof(SalesSequenceExecution.SalesCampaignContactId))
            .HasPrincipalKey(nameof(SalesCampaignContact.CompanyId), nameof(SalesCampaignContact.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact)
            .WithMany()
            .HasForeignKey(nameof(SalesSequenceExecution.CompanyId), nameof(SalesSequenceExecution.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesSequenceExecutionStepConfiguration : IEntityTypeConfiguration<SalesSequenceExecutionStep>
{
    public void Configure(EntityTypeBuilder<SalesSequenceExecutionStep> builder)
    {
        builder.ToTable("sales_sequence_execution_steps");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SequenceExecutionId).HasColumnName("sequence_execution_id").IsRequired();
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.SalesSequenceStepId).HasColumnName("sales_sequence_step_id").IsRequired();
        builder.Property(x => x.StepOrder).HasColumnName("step_order").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ScheduledSendUtc).HasColumnName("scheduled_send_at").IsRequired();
        builder.Property(x => x.SentUtc).HasColumnName("sent_at");
        builder.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        builder.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BounceStatus).HasColumnName("bounce_status").HasMaxLength(32);
        builder.Property(x => x.BounceReason).HasColumnName("bounce_reason").HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(80);
        builder.Property(x => x.CancellationSourceReference).HasColumnName("cancellation_source_reference").HasMaxLength(256);
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id");
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(256);
        builder.Property(x => x.ProviderThreadId).HasColumnName("provider_thread_id").HasMaxLength(256);
        builder.Property(x => x.InternetMessageId).HasColumnName("internet_message_id").HasMaxLength(512);
        builder.Property(x => x.OriginalGeneratedSubject).HasColumnName("original_generated_subject").HasMaxLength(300);
        builder.Property(x => x.OriginalGeneratedBody).HasColumnName("original_generated_body").HasMaxLength(16000);
        builder.Property(x => x.CurrentDraftSubject).HasColumnName("current_draft_subject").HasMaxLength(300);
        builder.Property(x => x.CurrentDraftBody).HasColumnName("current_draft_body").HasMaxLength(16000);
        builder.Property(x => x.FinalSentSubject).HasColumnName("final_sent_subject").HasMaxLength(300);
        builder.Property(x => x.FinalSentBody).HasColumnName("final_sent_body").HasMaxLength(16000);
        builder.Property(x => x.GeneratedDraftUtc).HasColumnName("generated_draft_at");
        builder.Property(x => x.DraftUpdatedUtc).HasColumnName("draft_updated_at");
        builder.Property(x => x.PolicyDecisionOutcome).HasColumnName("policy_decision_outcome").HasMaxLength(32);
        builder.Property(x => x.PolicyDecisionReasonCode).HasColumnName("policy_decision_reason_code").HasMaxLength(120);
        builder.Property(x => x.PolicyDecisionReason).HasColumnName("policy_decision_reason").HasMaxLength(1000);
        builder.Property(x => x.OutboundMessageReviewId).HasColumnName("outbound_message_review_id");
        builder.Property(x => x.PolicyEvaluatedUtc).HasColumnName("policy_evaluated_at");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ScheduledSendUtc, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderMessageId }).HasFilter("[provider_message_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ProviderThreadId }).HasFilter("[provider_thread_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ContactId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ContactId, x.CancellationReason });
        builder.HasIndex(x => new { x.CompanyId, x.PolicyDecisionOutcome, x.PolicyEvaluatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.OutboundMessageReviewId }).HasFilter("[outbound_message_review_id] IS NOT NULL");
        builder.HasOne(x => x.SequenceExecution)
            .WithMany(x => x.Steps)
            .HasForeignKey(nameof(SalesSequenceExecutionStep.CompanyId), nameof(SalesSequenceExecutionStep.SequenceExecutionId))
            .HasPrincipalKey(nameof(SalesSequenceExecution.CompanyId), nameof(SalesSequenceExecution.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SalesSequenceStep)
            .WithMany()
            .HasForeignKey(nameof(SalesSequenceExecutionStep.CompanyId), nameof(SalesSequenceExecutionStep.SalesSequenceStepId))
            .HasPrincipalKey(nameof(SalesSequenceStep.CompanyId), nameof(SalesSequenceStep.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesAutomationPolicyConfiguration : IEntityTypeConfiguration<SalesAutomationPolicy>
{
    public void Configure(EntityTypeBuilder<SalesAutomationPolicy> builder)
    {
        builder.ToTable("sales_automation_policies");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(80).IsRequired();
        builder.Property(x => x.FinanceDocumentsAlwaysRequireApproval).HasColumnName("finance_documents_always_require_approval").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.OutboundEnabled).HasColumnName("outbound_enabled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.MaxEmailsPerDay).HasColumnName("max_emails_per_day").HasDefaultValue(25).IsRequired();
        builder.Property(x => x.RequireApprovalFirstContact).HasColumnName("require_approval_first_contact").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RequireApprovalPricingDiscussion).HasColumnName("require_approval_pricing_discussion").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RequireApprovalFollowUps).HasColumnName("require_approval_follow_ups").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RequireApprovalReEngagement).HasColumnName("require_approval_re_engagement").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.WebsiteLeadDeduplicationWindowMinutes).HasColumnName("website_lead_deduplication_window_minutes").HasDefaultValue(10080).IsRequired();
        builder.Property(x => x.WebsiteLeadFormKey).HasColumnName("website_lead_form_key").HasMaxLength(80).IsRequired();
        builder.Property(x => x.WebsiteLeadFollowUpSequenceId).HasColumnName("website_lead_follow_up_sequence_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasIndex(x => x.WebsiteLeadFormKey).IsUnique();
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesFinanceHandoffConfiguration : IEntityTypeConfiguration<SalesFinanceHandoff>
{
    public void Configure(EntityTypeBuilder<SalesFinanceHandoff> builder)
    {
        builder.ToTable("sales_finance_handoffs");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ApprovalStatus).HasColumnName("approval_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExecutionStatus).HasColumnName("execution_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32).HasDefaultValue("invoice").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ApprovalId).HasColumnName("approval_id");
        builder.Property(x => x.WriteRequestId).HasColumnName("write_request_id");
        builder.Property(x => x.ExternalSystem).HasColumnName("external_system").HasMaxLength(64).HasDefaultValue("Fortnox").IsRequired();
        builder.Property(x => x.ExternalDocumentId).HasColumnName("external_document_id").HasMaxLength(256);
        builder.Property(x => x.ExternalDocumentNumber).HasColumnName("external_document_number").HasMaxLength(128);
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.ExecutionStartedUtc).HasColumnName("execution_started_at");
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at");
        builder.Property(x => x.FailedUtc).HasColumnName("failed_at");
        builder.Property(x => x.RetriedUtc).HasColumnName("retried_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.DealId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalId }).HasFilter("[approval_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.WriteRequestId }).HasFilter("[write_request_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalStatus });
        builder.HasIndex(x => new { x.CompanyId, x.ExecutionStatus });
        builder.HasIndex(x => new { x.CompanyId, x.ExternalSystem, x.ExternalDocumentId }).IsUnique().HasFilter("[external_document_id] IS NOT NULL");
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Deal)
            .WithMany()
            .HasForeignKey(nameof(SalesFinanceHandoff.CompanyId), nameof(SalesFinanceHandoff.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OutboundMessageReviewConfiguration : IEntityTypeConfiguration<OutboundMessageReview>
{
    public void Configure(EntityTypeBuilder<OutboundMessageReview> builder)
    {
        builder.ToTable("outbound_message_reviews");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SequenceExecutionStepId).HasColumnName("sequence_execution_step_id").IsRequired();
        builder.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OriginalSubject).HasColumnName("original_subject").HasMaxLength(300).IsRequired();
        builder.Property(x => x.OriginalBody).HasColumnName("original_body").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.EditedSubject).HasColumnName("edited_subject").HasMaxLength(300);
        builder.Property(x => x.EditedBody).HasColumnName("edited_body").HasMaxLength(16000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.Property(x => x.DecisionComment).HasColumnName("decision_comment").HasMaxLength(1000);
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.RequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SequenceExecutionStepId }).IsUnique();
        builder.HasOne(x => x.SequenceExecutionStep)
            .WithMany()
            .HasForeignKey(nameof(OutboundMessageReview.CompanyId), nameof(OutboundMessageReview.SequenceExecutionStepId))
            .HasPrincipalKey(nameof(SalesSequenceExecutionStep.CompanyId), nameof(SalesSequenceExecutionStep.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact)
            .WithMany()
            .HasForeignKey(nameof(OutboundMessageReview.CompanyId), nameof(OutboundMessageReview.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WebsiteLeadSubmissionConfiguration : IEntityTypeConfiguration<WebsiteLeadSubmission>
{
    public void Configure(EntityTypeBuilder<WebsiteLeadSubmission> builder)
    {
        builder.ToTable("website_lead_submissions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LeadId).HasColumnName("lead_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id");
        builder.Property(x => x.MergedIntoSubmissionId).HasColumnName("merged_into_submission_id");
        builder.Property(x => x.EnrollmentOutboxMessageId).HasColumnName("enrollment_outbox_message_id");
        builder.Property(x => x.FollowUpSequenceId).HasColumnName("follow_up_sequence_id");
        builder.Property(x => x.SequenceExecutionId).HasColumnName("sequence_execution_id");
        builder.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
        builder.Property(x => x.CompanyName).HasColumnName("company_name").HasMaxLength(200);
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(2000);
        builder.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(512);
        builder.Property(x => x.FormId).HasColumnName("form_id").HasMaxLength(120);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(64);
        builder.Property(x => x.ExternalSubmissionId).HasColumnName("external_submission_id").HasMaxLength(256);
        builder.Property(x => x.SourceMetadataJson).HasColumnName("source_metadata_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.DeduplicationDecision).HasColumnName("deduplication_decision").HasMaxLength(64).HasDefaultValue("new").IsRequired();
        builder.Property(x => x.SequenceEnrollmentStatus).HasColumnName("sequence_enrollment_status").HasMaxLength(64).HasDefaultValue(SalesStatuses.Pending).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReceivedUtc).HasColumnName("received_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedEmail, x.ReceivedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ReceivedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ExternalSubmissionId }).IsUnique().HasFilter("[external_submission_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.FollowUpSequenceId, x.SequenceEnrollmentStatus });
        builder.HasIndex(x => new { x.CompanyId, x.SequenceExecutionId }).HasFilter("[sequence_execution_id] IS NOT NULL");
        builder.HasOne(x => x.Lead)
            .WithMany()
            .HasForeignKey(nameof(WebsiteLeadSubmission.CompanyId), nameof(WebsiteLeadSubmission.LeadId))
            .HasPrincipalKey(nameof(Lead.CompanyId), nameof(Lead.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
