using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AdvancedReconciliationRuleConfiguration : IEntityTypeConfiguration<AdvancedReconciliationRule>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationRule> b)
    {
        b.ToTable("finance_advanced_reconciliation_rules");
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsRequired(); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.ReferenceNormalizationPattern).HasColumnName("reference_normalization_pattern").HasMaxLength(500).IsRequired();
        b.Property(x => x.CounterpartyNormalizationPattern).HasColumnName("counterparty_normalization_pattern").HasMaxLength(500).IsRequired();
        b.Property(x => x.ProviderPattern).HasColumnName("provider_pattern").HasMaxLength(500).IsRequired();
        b.Property(x => x.AmountTolerance).HasColumnName("amount_tolerance").HasPrecision(19, 4).IsRequired();
        b.Property(x => x.TimingWindowDays).HasColumnName("timing_window_days").IsRequired();
        b.Property(x => x.RecommendationThreshold).HasColumnName("recommendation_threshold").HasPrecision(9, 4).IsRequired();
        b.Property(x => x.LowConfidenceThreshold).HasColumnName("low_confidence_threshold").HasPrecision(9, 4).IsRequired();
        b.Property(x => x.MaterialityThreshold).HasColumnName("materiality_threshold").HasPrecision(19, 4).IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.Property(x => x.SupersededUtc).HasColumnName("superseded_at");
        b.HasIndex(x => new { x.CompanyId, x.Version }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.SupersededUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdvancedReconciliationGroupConfiguration : IEntityTypeConfiguration<AdvancedReconciliationGroup>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationGroup> b)
    {
        b.ToTable("finance_advanced_reconciliation_groups", t => t.HasCheckConstraint("CK_finance_advanced_reconciliation_groups_status", AdvancedReconciliationGroupStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired(); b.Property(x => x.RuleVersion).HasColumnName("rule_version").IsRequired(); b.Property(x => x.CorrectionOfGroupId).HasColumnName("correction_of_group_id");
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200).IsRequired(); b.Property(x => x.Counterparty).HasColumnName("counterparty").HasMaxLength(200).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsUnicode(false).IsRequired();
        b.Property(x => x.ExpectedBankTotal).HasColumnName("expected_bank_total").HasPrecision(19, 4).IsRequired(); b.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasPrecision(9, 4).IsRequired(); b.Property(x => x.RequiresApproval).HasColumnName("requires_approval").IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.ProposalHash).HasColumnName("proposal_hash").HasMaxLength(64);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        b.Property(x => x.AcceptedUtc).HasColumnName("accepted_at"); b.Property(x => x.RejectedUtc).HasColumnName("rejected_at"); b.Property(x => x.ReversedUtc).HasColumnName("reversed_at"); b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc }); b.HasIndex(x => new { x.CompanyId, x.RuleVersion }); b.HasIndex(x => new { x.CompanyId, x.Reference });
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Rule).WithMany().HasForeignKey(x => new { x.CompanyId, x.RuleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdvancedReconciliationGroup>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CorrectionOfGroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AdvancedReconciliationNodeConfiguration : IEntityTypeConfiguration<AdvancedReconciliationNode>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationNode> b)
    {
        b.ToTable("finance_advanced_reconciliation_nodes", t => t.HasCheckConstraint("CK_finance_advanced_reconciliation_nodes_type", AdvancedReconciliationNodeTypes.BuildCheckConstraintSql("node_type")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.GroupId).HasColumnName("group_id").IsRequired();
        b.Property(x => x.NodeType).HasColumnName("node_type").HasMaxLength(32).IsRequired(); b.Property(x => x.RecordId).HasColumnName("record_id");
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200).IsRequired(); b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(300).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsUnicode(false).IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(16); b.Property(x => x.AdjustmentKind).HasColumnName("adjustment_kind").HasMaxLength(64);
        b.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.ExpectedRecordVersion).HasColumnName("expected_record_version").HasMaxLength(200); b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.GroupId, x.Sequence }); b.HasIndex(x => new { x.CompanyId, x.NodeType, x.RecordId });
        b.HasOne(x => x.Group).WithMany(x => x.Nodes).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdvancedReconciliationEdgeConfiguration : IEntityTypeConfiguration<AdvancedReconciliationEdge>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationEdge> b)
    {
        b.ToTable("finance_advanced_reconciliation_edges", t => t.HasCheckConstraint("CK_finance_advanced_reconciliation_edges_type", AdvancedReconciliationEdgeTypes.BuildCheckConstraintSql("edge_type")));
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(); b.Property(x => x.SourceNodeId).HasColumnName("source_node_id").IsRequired(); b.Property(x => x.TargetNodeId).HasColumnName("target_node_id").IsRequired(); b.Property(x => x.EdgeType).HasColumnName("edge_type").HasMaxLength(32).IsRequired(); b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.GroupId, x.EdgeType }); b.HasIndex(x => new { x.CompanyId, x.SourceNodeId, x.TargetNodeId, x.EdgeType }).IsUnique();
        b.HasOne(x => x.Group).WithMany(x => x.Edges).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SourceNode).WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceNodeId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.TargetNode).WithMany().HasForeignKey(x => new { x.CompanyId, x.TargetNodeId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AdvancedReconciliationReasonContributionConfiguration : IEntityTypeConfiguration<AdvancedReconciliationReasonContribution>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationReasonContribution> b)
    {
        b.ToTable("finance_advanced_reconciliation_reason_contributions"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(); b.Property(x => x.FeatureKey).HasColumnName("feature_key").HasMaxLength(80).IsRequired(); b.Property(x => x.Contribution).HasColumnName("contribution").HasPrecision(9, 4).IsRequired(); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(500).IsRequired(); b.Property(x => x.Evidence).HasColumnName("evidence").HasMaxLength(1000).IsRequired(); b.HasIndex(x => new { x.CompanyId, x.GroupId, x.FeatureKey }).IsUnique(); b.HasOne(x => x.Group).WithMany(x => x.ReasonContributions).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdvancedReconciliationResultConfiguration : IEntityTypeConfiguration<AdvancedReconciliationResult>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationResult> b)
    {
        b.ToTable("finance_advanced_reconciliation_results"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(); b.Property(x => x.ParentResultId).HasColumnName("parent_result_id"); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired(); b.Property(x => x.GroupVersion).HasColumnName("group_version").IsRequired(); b.Property(x => x.RuleVersion).HasColumnName("rule_version").IsRequired(); b.Property(x => x.ExpectedBankTotal).HasColumnName("expected_bank_total").HasPrecision(19, 4).IsRequired(); b.Property(x => x.AllocatedAmount).HasColumnName("allocated_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.FeeAmount).HasColumnName("fee_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.RoundingAmount).HasColumnName("rounding_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.ResidualAmount).HasColumnName("residual_amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.GroupId, x.CreatedUtc }); b.HasOne(x => x.Group).WithMany(x => x.Results).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne<AdvancedReconciliationResult>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentResultId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AdvancedReconciliationEventConfiguration : IEntityTypeConfiguration<AdvancedReconciliationEvent>
{
    public void Configure(EntityTypeBuilder<AdvancedReconciliationEvent> b)
    {
        b.ToTable("finance_advanced_reconciliation_events"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.GroupId).HasColumnName("group_id").IsRequired(); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(80).IsRequired(); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired(); b.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.GroupId, x.CreatedUtc }); b.HasOne(x => x.Group).WithMany(x => x.Events).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
