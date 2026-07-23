using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class ReconciliationSuggestionRecordEntityConfiguration : IEntityTypeConfiguration<ReconciliationSuggestionRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationSuggestionRecord> builder)
    {
        builder.ToTable("finance_reconciliation_suggestions");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_reconciliation_suggestions_source_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("source_record_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_suggestions_target_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("target_record_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_suggestions_match_type", ReconciliationMatchTypes.BuildCheckConstraintSql("match_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_suggestions_status", ReconciliationSuggestionStatuses.BuildCheckConstraintSql("status"));
            t.HasCheckConstraint("CK_finance_reconciliation_suggestions_confidence_score", "confidence_score >= 0 AND confidence_score <= 1");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SourceRecordType).HasColumnName("source_record_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceRecordId).HasColumnName("source_record_id").IsRequired();
        builder.Property(x => x.TargetRecordType).HasColumnName("target_record_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetRecordId).HasColumnName("target_record_id").IsRequired();
        builder.Property(x => x.MatchType).HasColumnName("match_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.RuleBreakdown)
            .HasColumnName("rule_breakdown_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(ReconciliationSuggestionStatuses.Open).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.AcceptedUtc).HasColumnName("accepted_at");
        builder.Property(x => x.RejectedUtc).HasColumnName("rejected_at");
        builder.Property(x => x.SupersededUtc).HasColumnName("superseded_at");

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceRecordType, x.SourceRecordId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.TargetRecordType, x.TargetRecordId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.SourceRecordType, x.SourceRecordId, x.TargetRecordType, x.TargetRecordId });
        builder.HasIndex(x => x.CreatedByUserId);
        builder.HasIndex(x => x.UpdatedByUserId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.AcceptedResults).WithOne(x => x.AcceptedSuggestion).HasForeignKey(x => x.AcceptedSuggestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReconciliationResultRecordEntityConfiguration : IEntityTypeConfiguration<ReconciliationResultRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationResultRecord> builder)
    {
        builder.ToTable("finance_reconciliation_results");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_reconciliation_results_source_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("source_record_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_results_target_record_type", ReconciliationRecordTypes.BuildCheckConstraintSql("target_record_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_results_match_type", ReconciliationMatchTypes.BuildCheckConstraintSql("match_type"));
            t.HasCheckConstraint("CK_finance_reconciliation_results_confidence_score", "confidence_score >= 0 AND confidence_score <= 1");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AcceptedSuggestionId).HasColumnName("accepted_suggestion_id").IsRequired();
        builder.Property(x => x.SourceRecordType).HasColumnName("source_record_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceRecordId).HasColumnName("source_record_id").IsRequired();
        builder.Property(x => x.TargetRecordType).HasColumnName("target_record_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetRecordId).HasColumnName("target_record_id").IsRequired();
        builder.Property(x => x.MatchType).HasColumnName("match_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.RuleBreakdown)
            .HasColumnName("rule_breakdown_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.AcceptedSuggestionId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceRecordType, x.SourceRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetRecordType, x.TargetRecordId });
        builder.HasIndex(x => x.CreatedByUserId);
        builder.HasIndex(x => x.UpdatedByUserId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcceptedSuggestion).WithMany(x => x.AcceptedResults).HasForeignKey(x => x.AcceptedSuggestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
