using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> b)
    {
        b.ToTable("report_definitions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ReportKind).HasColumnName("report_kind").HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceTemplateKey).HasColumnName("source_template_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.ReportKind });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionVersionConfiguration : IEntityTypeConfiguration<ReportDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionVersion> b)
    {
        b.ToTable("report_definition_versions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.DefinitionId).HasColumnName("definition_id"); b.Property(x => x.VersionNumber).HasColumnName("version_number");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ReportKind).HasColumnName("report_kind").HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        b.Property(x => x.DefinitionHash).HasColumnName("definition_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.SubmittedUtc).HasColumnName("submitted_utc");
        b.Property(x => x.ApprovedUtc).HasColumnName("approved_utc"); b.Property(x => x.ActivatedUtc).HasColumnName("activated_utc");
        b.Property(x => x.RetiredUtc).HasColumnName("retired_utc");
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.ReportKind, x.Status, x.EffectiveFrom, x.EffectiveTo });
        b.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(x => new { x.CompanyId, x.DefinitionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionSectionConfiguration : IEntityTypeConfiguration<ReportDefinitionSection>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionSection> b)
    {
        b.ToTable("report_definition_sections"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(80).IsRequired(); b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplayOrder).HasColumnName("display_order"); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.VersionId, x.Code }).IsUnique();
        b.HasOne(x => x.Version).WithMany(x => x.Sections).HasForeignKey(x => new { x.CompanyId, x.VersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionLineConfiguration : IEntityTypeConfiguration<ReportDefinitionLine>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionLine> b)
    {
        b.ToTable("report_definition_lines"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.SectionId).HasColumnName("section_id"); b.Property(x => x.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200).IsRequired(); b.Property(x => x.LineType).HasColumnName("line_type").HasMaxLength(32).IsRequired();
        b.Property(x => x.DisplayOrder).HasColumnName("display_order"); b.Property(x => x.Formula).HasColumnName("formula").HasMaxLength(2000);
        b.Property(x => x.SignRule).HasColumnName("sign_rule").HasMaxLength(32).IsRequired(); b.Property(x => x.Scale).HasColumnName("scale");
        b.Property(x => x.Decimals).HasColumnName("decimals"); b.Property(x => x.SuppressZero).HasColumnName("suppress_zero");
        b.Property(x => x.CurrencyMode).HasColumnName("currency_mode").HasMaxLength(32).IsRequired();
        b.Property(x => x.DimensionTypeId).HasColumnName("dimension_type_id"); b.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id");
        b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.HasIndex(x => new { x.CompanyId, x.VersionId, x.Code }).IsUnique();
        b.HasOne(x => x.Version).WithMany().HasForeignKey(x => new { x.CompanyId, x.VersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Section).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.SectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionAccountGroupConfiguration : IEntityTypeConfiguration<ReportDefinitionAccountGroup>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionAccountGroup> b)
    {
        b.ToTable("report_definition_account_groups"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.LineId).HasColumnName("line_id"); b.Property(x => x.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired(); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.VersionId, x.Code }).IsUnique();
        b.HasOne(x => x.Version).WithMany(x => x.AccountGroups).HasForeignKey(x => new { x.CompanyId, x.VersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Line).WithMany(x => x.AccountGroups).HasForeignKey(x => new { x.CompanyId, x.LineId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionAccountGroupMemberConfiguration : IEntityTypeConfiguration<ReportDefinitionAccountGroupMember>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionAccountGroupMember> b)
    {
        b.ToTable("report_definition_account_group_members"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.GroupId).HasColumnName("group_id");
        b.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id"); b.HasIndex(x => new { x.CompanyId, x.GroupId, x.FinanceAccountId }).IsUnique();
        b.HasOne(x => x.Group).WithMany(x => x.Members).HasForeignKey(x => new { x.CompanyId, x.GroupId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReportDefinitionComparisonConfiguration : IEntityTypeConfiguration<ReportDefinitionComparison>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionComparison> b)
    {
        b.ToTable("report_definition_comparisons"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(32).IsRequired(); b.Property(x => x.PeriodCount).HasColumnName("period_count");
        b.Property(x => x.ShowVariance).HasColumnName("show_variance"); b.Property(x => x.ShowVariancePercent).HasColumnName("show_variance_percent");
        b.HasIndex(x => new { x.CompanyId, x.VersionId }).IsUnique();
        b.HasOne(x => x.Version).WithOne(x => x.Comparison).HasForeignKey<ReportDefinitionComparison>(x => new { x.CompanyId, x.VersionId })
            .HasPrincipalKey<ReportDefinitionVersion>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionValidationResultConfiguration : IEntityTypeConfiguration<ReportDefinitionValidationResult>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionValidationResult> b)
    {
        b.ToTable("report_definition_validation_results"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.IsValid).HasColumnName("is_valid"); b.Property(x => x.DefinitionHash).HasColumnName("definition_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.ValidatedByUserId).HasColumnName("validated_by_user_id"); b.Property(x => x.ValidatedUtc).HasColumnName("validated_utc");
        b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.HasIndex(x => new { x.CompanyId, x.VersionId, x.ValidatedUtc });
        b.HasOne(x => x.Version).WithMany(x => x.ValidationResults).HasForeignKey(x => new { x.CompanyId, x.VersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionValidationIssueConfiguration : IEntityTypeConfiguration<ReportDefinitionValidationIssue>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionValidationIssue> b)
    {
        b.ToTable("report_definition_validation_issues"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ValidationResultId).HasColumnName("validation_result_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired(); b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired(); b.Property(x => x.LineId).HasColumnName("line_id"); b.Property(x => x.AccountId).HasColumnName("account_id");
        b.HasIndex(x => new { x.CompanyId, x.ValidationResultId, x.Code });
        b.HasOne(x => x.ValidationResult).WithMany(x => x.Issues).HasForeignKey(x => new { x.CompanyId, x.ValidationResultId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionApprovalConfiguration : IEntityTypeConfiguration<ReportDefinitionApproval>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionApproval> b)
    {
        b.ToTable("report_definition_approvals"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        b.Property(x => x.SubmittedUtc).HasColumnName("submitted_utc"); b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        b.Property(x => x.DecidedUtc).HasColumnName("decided_utc"); b.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(1000);
        b.HasIndex(x => new { x.CompanyId, x.VersionId, x.SubmittedUtc });
        b.HasOne(x => x.Version).WithMany(x => x.Approvals).HasForeignKey(x => new { x.CompanyId, x.VersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReportDefinitionCommandReceiptConfiguration : IEntityTypeConfiguration<ReportDefinitionCommandReceipt>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionCommandReceipt> b)
    {
        b.ToTable("report_definition_command_receipts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(64).IsRequired(); b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasOne(x => x.Version).WithMany().HasForeignKey(x => new { x.CompanyId, x.VersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
