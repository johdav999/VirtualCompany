using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceBillReviewStateEntityConfiguration : IEntityTypeConfiguration<FinanceBillReviewState>
{
    public void Configure(EntityTypeBuilder<FinanceBillReviewState> builder)
    {
        builder.ToTable("finance_bill_review_states");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProposalSummary).HasColumnName("proposal_summary").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.Navigation(x => x.Actions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_finance_bill_review_states_status",
            "status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')"));

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceBillReviewActionEntityConfiguration : IEntityTypeConfiguration<FinanceBillReviewAction>
{
    public void Configure(EntityTypeBuilder<FinanceBillReviewAction> builder)
    {
        builder.ToTable("finance_bill_review_actions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ReviewStateId).HasColumnName("review_state_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.ActorDisplayName).HasColumnName("actor_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PriorStatus).HasColumnName("prior_status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.NewStatus).HasColumnName("new_status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_bill_review_actions_prior_status", "prior_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
            t.HasCheckConstraint("CK_finance_bill_review_actions_new_status", "new_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId, x.OccurredUtc });
        builder.HasOne(x => x.ReviewState).WithMany(x => x.Actions).HasForeignKey(x => x.ReviewStateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BillApprovalProposalEntityConfiguration : IEntityTypeConfiguration<BillApprovalProposal>
{
    public void Configure(EntityTypeBuilder<BillApprovalProposal> builder)
    {
        builder.ToTable("bill_approval_proposals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.ReviewStateId).HasColumnName("review_state_id").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at").IsRequired();
        builder.Property(x => x.PaymentExecutionRequested)
            .HasColumnName("payment_execution_requested")
            .HasDefaultValue(false)
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_bill_approval_proposals_no_payment_execution",
            "payment_execution_requested = 0"));

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId }).IsUnique();
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ReviewState).WithMany().HasForeignKey(x => x.ReviewStateId).OnDelete(DeleteBehavior.Cascade);
    }
}