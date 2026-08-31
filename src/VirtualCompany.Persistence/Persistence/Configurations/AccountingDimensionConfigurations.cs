using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingDimensionTypeConfiguration : IEntityTypeConfiguration<AccountingDimensionType>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionType> b)
    {
        b.ToTable("accounting_dimension_types"); Identity(b);
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(x => x.AllowsHierarchy).HasColumnName("allows_hierarchy").IsRequired();
        Lifecycle(b); Audit(b);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.EffectiveFrom });
        Company(b);
    }

    internal static void Identity<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, ICompanyOwnedEntity
    {
        b.HasKey("Id"); b.HasAlternateKey("CompanyId", "Id");
        b.Property("Id").HasColumnName("id"); b.Property("CompanyId").HasColumnName("company_id").IsRequired();
    }
    internal static void Lifecycle<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class
    {
        b.Property("Status").HasColumnName("status").HasMaxLength(24).IsRequired();
        b.Property("EffectiveFrom").HasColumnName("effective_from").HasColumnType("date").IsRequired();
        b.Property("EffectiveTo").HasColumnName("effective_to").HasColumnType("date");
    }
    internal static void Audit<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class
    {
        b.Property("CreatedByUserId").HasColumnName("created_by_user_id").IsRequired();
        b.Property("CreatedUtc").HasColumnName("created_at").IsRequired();
        b.Property("UpdatedUtc").HasColumnName("updated_at").IsRequired();
        b.Property("Version").HasColumnName("version").IsConcurrencyToken().IsRequired();
    }
    internal static void Company<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, ICompanyOwnedEntity =>
        b.HasOne<Company>("Company").WithMany().HasForeignKey("CompanyId").OnDelete(DeleteBehavior.Cascade);
}

internal sealed class AccountingDimensionMemberConfiguration : IEntityTypeConfiguration<AccountingDimensionMember>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionMember> b)
    {
        b.ToTable("accounting_dimension_members"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.DimensionTypeId).HasColumnName("dimension_type_id").IsRequired();
        b.Property(x => x.ParentMemberId).HasColumnName("parent_member_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        AccountingDimensionTypeConfiguration.Lifecycle(b); AccountingDimensionTypeConfiguration.Audit(b);
        b.HasIndex(x => new { x.CompanyId, x.DimensionTypeId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DimensionTypeId, x.ParentMemberId, x.Status });
        AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.DimensionType).WithMany(x => x.Members)
            .HasForeignKey(x => new { x.CompanyId, x.DimensionTypeId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ParentMember).WithMany(x => x.Children)
            .HasForeignKey(x => new { x.CompanyId, x.ParentMemberId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingDimensionAccountPolicyConfiguration : IEntityTypeConfiguration<AccountingDimensionAccountPolicy>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionAccountPolicy> b)
    {
        b.ToTable("accounting_dimension_account_policies"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        b.Property(x => x.DimensionTypeId).HasColumnName("dimension_type_id").IsRequired();
        b.Property(x => x.Requirement).HasColumnName("requirement").HasMaxLength(24).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired();
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        AccountingDimensionTypeConfiguration.Audit(b);
        b.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.DimensionTypeId, x.EffectiveFrom }).IsUnique();
        AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionType).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionTypeId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingDimensionCombinationRuleConfiguration : IEntityTypeConfiguration<AccountingDimensionCombinationRule>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionCombinationRule> b)
    {
        b.ToTable("accounting_dimension_combination_rules"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.LeftMemberId).HasColumnName("left_member_id").IsRequired();
        b.Property(x => x.RightMemberId).HasColumnName("right_member_id").IsRequired();
        b.Property(x => x.IsAllowed).HasColumnName("is_allowed").IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired();
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        AccountingDimensionTypeConfiguration.Audit(b);
        b.HasIndex(x => new { x.CompanyId, x.LeftMemberId, x.RightMemberId, x.EffectiveFrom }).IsUnique();
        AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.LeftMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.LeftMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RightMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.RightMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingDimensionExternalMappingConfiguration : IEntityTypeConfiguration<AccountingDimensionExternalMapping>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionExternalMapping> b)
    {
        b.ToTable("accounting_dimension_external_mappings"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.ExternalDimensionType).HasColumnName("external_dimension_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.ExternalValue).HasColumnName("external_value").HasMaxLength(160).IsRequired();
        b.Property(x => x.DimensionTypeId).HasColumnName("dimension_type_id").IsRequired();
        b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id").IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired();
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        AccountingDimensionTypeConfiguration.Audit(b);
        b.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.ExternalDimensionType, x.ExternalValue, x.EffectiveFrom }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DimensionMemberId }); AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.DimensionType).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionTypeId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingDimensionMappingConflictConfiguration : IEntityTypeConfiguration<AccountingDimensionMappingConflict>
{
    public void Configure(EntityTypeBuilder<AccountingDimensionMappingConflict> b)
    {
        b.ToTable("accounting_dimension_mapping_conflicts"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.ExternalDimensionType).HasColumnName("external_dimension_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.ExternalValue).HasColumnName("external_value").HasMaxLength(160).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();
        b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        b.Property(x => x.ResolvedDimensionMemberId).HasColumnName("resolved_dimension_member_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.ExternalDimensionType, x.ExternalValue, x.Status });
        AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.ResolvedDimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.ResolvedDimensionMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingAllocationTemplateConfiguration : IEntityTypeConfiguration<AccountingAllocationTemplate>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationTemplate> b)
    {
        b.ToTable("accounting_allocation_templates"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired(); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired(); b.Property(x => x.ApprovalThreshold).HasColumnName("approval_threshold").HasPrecision(19, 6);
        AccountingDimensionTypeConfiguration.Audit(b); b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); AccountingDimensionTypeConfiguration.Company(b);
    }
}

internal sealed class AccountingAllocationTemplateVersionConfiguration : IEntityTypeConfiguration<AccountingAllocationTemplateVersion>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationTemplateVersion> b)
    {
        b.ToTable("accounting_allocation_template_versions"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired(); b.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired(); b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        b.Property(x => x.RoundingPrecision).HasColumnName("rounding_precision").IsRequired(); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.TemplateId, x.VersionNumber }).IsUnique();
        AccountingDimensionTypeConfiguration.Company(b); b.HasOne(x => x.Template).WithMany(x => x.Versions)
            .HasForeignKey(x => new { x.CompanyId, x.TemplateId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingAllocationTemplateLineConfiguration : IEntityTypeConfiguration<AccountingAllocationTemplateLine>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationTemplateLine> b)
    {
        b.ToTable("accounting_allocation_template_lines"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.TemplateVersionId).HasColumnName("template_version_id").IsRequired(); b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id").IsRequired(); b.Property(x => x.AllocationKind).HasColumnName("allocation_kind").HasMaxLength(24).IsRequired();
        b.Property(x => x.Value).HasColumnName("value").HasPrecision(19, 8).IsRequired(); b.Property(x => x.Basis).HasColumnName("basis").HasMaxLength(160);
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.Sequence }).IsUnique(); AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.TemplateVersion).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.TemplateVersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingAllocationApplicationConfiguration : IEntityTypeConfiguration<AccountingAllocationApplication>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationApplication> b)
    {
        b.ToTable("accounting_allocation_applications"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired(); b.Property(x => x.TemplateVersionId).HasColumnName("template_version_id").IsRequired();
        b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired(); b.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(160).IsRequired();
        b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128).IsRequired(); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.SourceAmount).HasColumnName("source_amount").HasPrecision(19, 6).IsRequired();
        b.Property(x => x.AllocatedAmount).HasColumnName("allocated_amount").HasPrecision(19, 6).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired(); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.SourceVersion });
        AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.Template).WithMany().HasForeignKey(x => new { x.CompanyId, x.TemplateId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.TemplateVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.TemplateVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountingAllocationApplicationLineConfiguration : IEntityTypeConfiguration<AccountingAllocationApplicationLine>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationApplicationLine> b)
    {
        b.ToTable("accounting_allocation_application_lines"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired(); b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id").IsRequired(); b.Property(x => x.AllocationKind).HasColumnName("allocation_kind").HasMaxLength(24).IsRequired();
        b.Property(x => x.DriverValue).HasColumnName("driver_value").HasPrecision(19, 8).IsRequired(); b.Property(x => x.RawAmount).HasColumnName("raw_amount").HasPrecision(38, 18).IsRequired();
        b.Property(x => x.RoundedAmount).HasColumnName("rounded_amount").HasPrecision(19, 6).IsRequired(); b.Property(x => x.RoundingResidual).HasColumnName("rounding_residual").HasPrecision(38, 18).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.ApplicationId, x.Sequence }).IsUnique(); AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.Application).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.ApplicationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingAllocationEvidenceLinkConfiguration : IEntityTypeConfiguration<AccountingAllocationEvidenceLink>
{
    public void Configure(EntityTypeBuilder<AccountingAllocationEvidenceLink> b)
    {
        b.ToTable("accounting_allocation_evidence_links"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired(); b.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsRequired(); b.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.ApplicationId, x.DocumentId }).IsUnique();
        AccountingDimensionTypeConfiguration.Company(b); b.HasOne(x => x.Application).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => new { x.CompanyId, x.ApplicationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerEntryLineDimensionConfiguration : IEntityTypeConfiguration<LedgerEntryLineDimension>
{
    public void Configure(EntityTypeBuilder<LedgerEntryLineDimension> b)
    {
        b.ToTable("ledger_entry_line_dimensions"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.LedgerEntryLineId).HasColumnName("ledger_entry_line_id").IsRequired(); b.Property(x => x.DimensionTypeId).HasColumnName("dimension_type_id").IsRequired();
        b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id").IsRequired(); b.Property(x => x.DimensionTypeCodeSnapshot).HasColumnName("dimension_type_code_snapshot").HasMaxLength(64).IsRequired();
        b.Property(x => x.DimensionTypeNameSnapshot).HasColumnName("dimension_type_name_snapshot").HasMaxLength(120).IsRequired(); b.Property(x => x.MemberCodeSnapshot).HasColumnName("member_code_snapshot").HasMaxLength(64).IsRequired();
        b.Property(x => x.MemberNameSnapshot).HasColumnName("member_name_snapshot").HasMaxLength(160).IsRequired(); b.Property(x => x.HierarchyPathSnapshot).HasColumnName("hierarchy_path_snapshot").HasMaxLength(1000).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.LedgerEntryLineId, x.DimensionTypeId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DimensionMemberId, x.LedgerEntryLineId }); AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.LedgerEntryLine).WithMany(x => x.DimensionAssignments).HasForeignKey(x => new { x.CompanyId, x.LedgerEntryLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionType).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionTypeId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ManualJournalDraftLineDimensionConfiguration : IEntityTypeConfiguration<ManualJournalDraftLineDimension>
{
    public void Configure(EntityTypeBuilder<ManualJournalDraftLineDimension> b)
    {
        b.ToTable("manual_journal_draft_line_dimensions"); AccountingDimensionTypeConfiguration.Identity(b);
        b.Property(x => x.ManualJournalDraftLineId).HasColumnName("manual_journal_draft_line_id").IsRequired(); b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id").IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.ManualJournalDraftLineId, x.DimensionMemberId }).IsUnique(); AccountingDimensionTypeConfiguration.Company(b);
        b.HasOne(x => x.ManualJournalDraftLine).WithMany(x => x.DimensionAssignments)
            .HasForeignKey(x => new { x.CompanyId, x.ManualJournalDraftLineId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
