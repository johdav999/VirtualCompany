using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMessagePerformanceConfiguration : IEntityTypeConfiguration<SalesMessagePerformance>
{
    public void Configure(EntityTypeBuilder<SalesMessagePerformance> builder)
    {
        builder.ToTable("sales_message_performances");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MessageKey).HasColumnName("message_key").HasMaxLength(512).IsRequired();
        builder.Property(x => x.CampaignId).HasColumnName("campaign_id");
        builder.Property(x => x.SequenceId).HasColumnName("sequence_id");
        builder.Property(x => x.SequenceStepId).HasColumnName("sequence_step_id");
        builder.Property(x => x.SequenceExecutionStepId).HasColumnName("sequence_execution_step_id");
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(256);
        builder.Property(x => x.ProviderThreadId).HasColumnName("provider_thread_id").HasMaxLength(256);
        builder.Property(x => x.InternetMessageId).HasColumnName("internet_message_id").HasMaxLength(512);
        builder.Property(x => x.VariantKey).HasColumnName("variant_key").HasMaxLength(120);
        builder.Property(x => x.StepOrder).HasColumnName("step_order");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(x => x.BouncedAt).HasColumnName("bounced_at");
        builder.Property(x => x.OpenedAt).HasColumnName("opened_at");
        builder.Property(x => x.RepliedAt).HasColumnName("replied_at");
        builder.Property(x => x.DealCreatedAt).HasColumnName("deal_created_at");
        builder.Property(x => x.ConvertedAt).HasColumnName("converted_at");
        builder.Property(x => x.ExpectedRevenueAmount).HasColumnName("expected_revenue_amount").HasPrecision(18, 2);
        builder.Property(x => x.ExpectedRevenueCurrency).HasColumnName("expected_revenue_currency").HasMaxLength(3);
        builder.Property(x => x.ExpectedCloseAt).HasColumnName("expected_close_at");
        builder.Property(x => x.PipelineRiskScore).HasColumnName("pipeline_risk_score").HasPrecision(6, 4);
        builder.Property(x => x.LastRiskCalculatedAt).HasColumnName("last_risk_calculated_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.MessageKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CampaignId });
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SequenceId });
        builder.HasIndex(x => new { x.CompanyId, x.SequenceStepId });
        builder.HasIndex(x => new { x.CompanyId, x.SequenceExecutionStepId }).HasFilter("[sequence_execution_step_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ContactId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CampaignId, x.SequenceId, x.SequenceStepId, x.VariantKey });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderMessageId }).HasFilter("[provider_message_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ProviderThreadId }).HasFilter("[provider_thread_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.InternetMessageId }).HasFilter("[internet_message_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.DealId }).HasFilter("[deal_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ExpectedCloseAt });
        builder.HasIndex(x => new { x.CompanyId, x.PipelineRiskScore });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.CampaignId))
            .HasPrincipalKey(nameof(SalesCampaign.CompanyId), nameof(SalesCampaign.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Sequence)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.SequenceId))
            .HasPrincipalKey(nameof(SalesSequence.CompanyId), nameof(SalesSequence.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SequenceStep)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.SequenceStepId))
            .HasPrincipalKey(nameof(SalesSequenceStep.CompanyId), nameof(SalesSequenceStep.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SequenceExecutionStep)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.SequenceExecutionStepId))
            .HasPrincipalKey(nameof(SalesSequenceExecutionStep.CompanyId), nameof(SalesSequenceExecutionStep.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contact)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Deal)
            .WithMany()
            .HasForeignKey(nameof(SalesMessagePerformance.CompanyId), nameof(SalesMessagePerformance.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_sales_message_performances_step_order_positive", "step_order IS NULL OR step_order > 0");
            t.HasCheckConstraint("CK_sales_message_performances_pipeline_risk_score_range", "pipeline_risk_score IS NULL OR (pipeline_risk_score >= 0 AND pipeline_risk_score <= 1)");
        });
    }
}
