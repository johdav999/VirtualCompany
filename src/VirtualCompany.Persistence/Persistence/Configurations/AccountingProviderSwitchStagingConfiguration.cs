using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchStagedRecordConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchStagedRecord>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchStagedRecord> builder)
    {
        builder.ToTable("accounting_provider_switch_staged_records", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_staged_records_disposition",
                "[disposition] IN ('ready','mapped','transformed','opening_balance_representation','duplicate','excluded_with_approval','missing','unsupported','conflicting','awaiting_evidence','blocked')");
            table.HasCheckConstraint("CK_accounting_provider_switch_staged_records_version", "[version] > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.ExtractionBatchId).HasColumnName("extraction_batch_id");
        builder.Property(x => x.SourceEndpointKey).HasColumnName("source_endpoint_key").HasMaxLength(80);
        builder.Property(x => x.Dataset).HasColumnName("dataset").HasMaxLength(64);
        builder.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(256);
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128);
        builder.Property(x => x.SourceRecordKeyHash).HasColumnName("source_record_key_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.StableIdentityHash).HasColumnName("stable_identity_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ProviderModifiedUtc).HasColumnName("provider_modified_at");
        builder.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.NormalizedHash).HasColumnName("normalized_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.NormalizedDataJson).HasColumnName("normalized_data_json").HasMaxLength(16000);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        builder.Property(x => x.FinancialAmount).HasColumnName("financial_amount").HasPrecision(19, 4);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16);
        builder.Property(x => x.Disposition).HasColumnName("disposition").HasMaxLength(48);
        builder.Property(x => x.DispositionReason).HasColumnName("disposition_reason").HasMaxLength(1000);
        builder.Property(x => x.MappingDecisionId).HasColumnName("mapping_decision_id");
        builder.Property(x => x.MappingVersion).HasColumnName("mapping_version");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.ApprovalBindingHash).HasColumnName("approval_binding_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.DuplicateOfStagedRecordId).HasColumnName("duplicate_of_staged_record_id");
        builder.Property(x => x.IsCurrent).HasColumnName("is_current");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.StableIdentityHash }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.SourceRecordKeyHash }).IsUnique()
            .HasFilter("[is_current] = CAST(1 AS bit)");
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.Dataset, x.Disposition, x.IsCurrent });
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IsCurrent, x.MappingDecisionId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Switch).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AccountingProviderSwitchStagedRecord>().WithMany()
            .HasForeignKey(x => x.DuplicateOfStagedRecordId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchMappingSetConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMappingSet>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMappingSet> builder)
    {
        builder.ToTable("accounting_provider_switch_mapping_sets", table =>
            table.HasCheckConstraint("CK_accounting_provider_switch_mapping_sets_version", "[mapping_version] > 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.MappingType).HasColumnName("mapping_type").HasMaxLength(48);
        builder.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(256);
        builder.Property(x => x.MappingVersion).HasColumnName("mapping_version");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.SupersededUtc).HasColumnName("superseded_at");
        builder.Property(x => x.IsCurrent).HasColumnName("is_current");
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.MappingType, x.ScopeKey, x.MappingVersion }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.MappingType, x.ScopeKey, x.IsCurrent }).IsUnique()
            .HasFilter("[is_current] = CAST(1 AS bit)");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Switch).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchMappingDecisionConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMappingDecision>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMappingDecision> builder)
    {
        builder.ToTable("accounting_provider_switch_mapping_decisions", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_mapping_decisions_status",
                "[status] IN ('suggested','awaiting_approval','approved','rejected','stale')");
            table.HasCheckConstraint("CK_accounting_provider_switch_mapping_decisions_version", "[version] > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.MappingSetId).HasColumnName("mapping_set_id");
        builder.Property(x => x.MappingVersion).HasColumnName("mapping_version");
        builder.Property(x => x.MappingType).HasColumnName("mapping_type").HasMaxLength(48);
        builder.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(256);
        builder.Property(x => x.TargetKey).HasColumnName("target_key").HasMaxLength(256);
        builder.Property(x => x.SuggestionMethod).HasColumnName("suggestion_method").HasMaxLength(64);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        builder.Property(x => x.IsMaterial).HasColumnName("is_material");
        builder.Property(x => x.AffectedRecordCount).HasColumnName("affected_record_count");
        builder.Property(x => x.AffectedFinancialTotal).HasColumnName("affected_financial_total").HasPrecision(19, 4);
        builder.Property(x => x.BindingHash).HasColumnName("binding_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.MappingSetId, x.SourceKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.Status, x.MappingType });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Switch).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.MappingSet).WithMany(x => x.Decisions)
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.MappingSetId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchMappingRecordConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMappingRecord>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMappingRecord> builder)
    {
        builder.ToTable("accounting_provider_switch_mapping_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.MappingDecisionId).HasColumnName("mapping_decision_id");
        builder.Property(x => x.StagedRecordId).HasColumnName("staged_record_id");
        builder.Property(x => x.StagedSourceHash).HasColumnName("staged_source_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.StagedNormalizedHash).HasColumnName("staged_normalized_hash").HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.CompanyId, x.MappingDecisionId, x.StagedRecordId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.StagedRecordId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.MappingDecision).WithMany(x => x.AffectedRecords)
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.MappingDecisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.StagedRecord).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.StagedRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
