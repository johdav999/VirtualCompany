using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchAssessmentConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchAssessment>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchAssessment> builder)
    {
        builder.ToTable("accounting_provider_switch_assessments", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_assessments_status", "[status] IN ('queued', 'running', 'completed', 'failed')");
            table.HasCheckConstraint("CK_accounting_provider_switch_assessments_progress", "[work_index] >= 0 AND [work_index] <= [total_work_items] AND [total_work_items] > 0");
            table.HasCheckConstraint("CK_accounting_provider_switch_assessments_version", "[version] > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
        builder.Property(x => x.WorkIndex).HasColumnName("work_index");
        builder.Property(x => x.TotalWorkItems).HasColumnName("total_work_items");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
        builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.RequestedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Switch).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchCapabilityConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchCapability>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchCapability> builder)
    {
        builder.ToTable("accounting_provider_switch_capabilities", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_capabilities_level", "[level] IN ('supported', 'partial', 'unsupported', 'unknown')");
            table.HasCheckConstraint("CK_accounting_provider_switch_capabilities_role", "[endpoint_role] IN ('source', 'target')");
        });
        ConfigureCommon(builder);
        builder.Property(x => x.EndpointRole).HasColumnName("endpoint_role").HasMaxLength(16);
        builder.Property(x => x.CapabilityKey).HasColumnName("capability_key").HasMaxLength(64);
        builder.Property(x => x.Level).HasColumnName("level").HasMaxLength(16);
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        builder.Property(x => x.RequiredScope).HasColumnName("required_scope").HasMaxLength(128);
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at");
        builder.HasIndex(x => new { x.CompanyId, x.AssessmentId, x.EndpointRole, x.CapabilityKey }).IsUnique();
    }

    private static void ConfigureCommon(EntityTypeBuilder<AccountingProviderSwitchCapability> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.AssessmentId).HasColumnName("assessment_id");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Assessment).WithMany(x => x.Capabilities)
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.AssessmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchDatasetConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchDataset>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchDataset> builder)
    {
        builder.ToTable("accounting_provider_switch_datasets", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_datasets_availability", "[availability] IN ('available', 'confirmed_absent', 'not_returned', 'not_authorized', 'unsupported', 'unknown')");
            table.HasCheckConstraint("CK_accounting_provider_switch_datasets_capability", "[capability_level] IN ('supported', 'partial', 'unsupported', 'unknown')");
            table.HasCheckConstraint("CK_accounting_provider_switch_datasets_count", "[record_count] >= 0");
            table.HasCheckConstraint("CK_accounting_provider_switch_datasets_role", "[endpoint_role] IN ('source', 'target')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.AssessmentId).HasColumnName("assessment_id");
        builder.Property(x => x.EndpointRole).HasColumnName("endpoint_role").HasMaxLength(16);
        builder.Property(x => x.DatasetKey).HasColumnName("dataset_key").HasMaxLength(64);
        builder.Property(x => x.Availability).HasColumnName("availability").HasMaxLength(32);
        builder.Property(x => x.CapabilityLevel).HasColumnName("capability_level").HasMaxLength(16);
        builder.Property(x => x.RecordCount).HasColumnName("record_count");
        builder.Property(x => x.FinancialTotal).HasColumnName("financial_total").HasPrecision(19, 4);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16);
        builder.Property(x => x.SourceCursor).HasColumnName("source_cursor").HasMaxLength(256);
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128);
        builder.Property(x => x.IntegrityHash).HasColumnName("integrity_hash").HasMaxLength(64);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.ExtractedUtc).HasColumnName("extracted_at");
        builder.HasIndex(x => new { x.CompanyId, x.AssessmentId, x.EndpointRole, x.DatasetKey }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Assessment).WithMany(x => x.Datasets)
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.AssessmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchGapConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchGap>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchGap> builder)
    {
        builder.ToTable("accounting_provider_switch_gaps", table =>
        {
            table.HasCheckConstraint("CK_accounting_provider_switch_gaps_severity", "[severity] IN ('information', 'warning', 'blocking')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SwitchId).HasColumnName("switch_id");
        builder.Property(x => x.AssessmentId).HasColumnName("assessment_id");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64);
        builder.Property(x => x.DatasetKey).HasColumnName("dataset_key").HasMaxLength(64);
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(16);
        builder.Property(x => x.IsBlocking).HasColumnName("is_blocking");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        builder.Property(x => x.OperatorAction).HasColumnName("operator_action").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.AssessmentId, x.ReasonCode, x.DatasetKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IsBlocking });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Assessment).WithMany(x => x.Gaps)
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.AssessmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
