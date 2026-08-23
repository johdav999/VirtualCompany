using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchTargetTransferBatchConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchTargetTransferBatch>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchTargetTransferBatch> b)
    {
        b.ToTable("accounting_provider_switch_target_transfer_batches"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanVersion).HasColumnName("plan_version"); b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.TargetProviderKey).HasColumnName("target_provider_key").HasMaxLength(64);
        b.Property(x => x.PackageHash).HasColumnName("package_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.TotalItemCount).HasColumnName("total_item_count"); b.Property(x => x.PreviewItemCount).HasColumnName("preview_item_count");
        b.Property(x => x.PreparatoryItemCount).HasColumnName("preparatory_item_count"); b.Property(x => x.FinalItemCount).HasColumnName("final_item_count");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.RequestedUtc).HasColumnName("requested_at"); b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.PlanId, x.PackageHash }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Switch).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PlanId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchTargetTransferItemConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchTargetTransferItem>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchTargetTransferItem> b)
    {
        b.ToTable("accounting_provider_switch_target_transfer_items"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.StagedRecordId).HasColumnName("staged_record_id");
        b.Property(x => x.Dataset).HasColumnName("dataset").HasMaxLength(64); b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(256);
        b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.NormalizedHash).HasColumnName("normalized_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.MappingVersion).HasColumnName("mapping_version");
        b.Property(x => x.OperationMode).HasColumnName("operation_mode").HasMaxLength(32); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(80);
        b.Property(x => x.StableIdentity).HasColumnName("stable_identity").HasMaxLength(64).IsFixedLength(); b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.SafePayloadSummary).HasColumnName("safe_payload_summary").HasMaxLength(1000); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.CommandType).HasColumnName("command_type").HasMaxLength(80); b.Property(x => x.HttpMethod).HasColumnName("http_method").HasMaxLength(16);
        b.Property(x => x.Path).HasColumnName("path").HasMaxLength(512); b.Property(x => x.SanitizedPayloadJson).HasColumnName("sanitized_payload_json").HasMaxLength(64000);
        b.Property(x => x.ProviderPayloadType).HasColumnName("provider_payload_type").HasMaxLength(128);
        b.Property(x => x.WriteRequestId).HasColumnName("write_request_id"); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.ProviderExternalId).HasColumnName("provider_external_id").HasMaxLength(256); b.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(100);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.ReconciliationNeeded).HasColumnName("reconciliation_needed");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.BatchId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.StableIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.WriteRequestId }).IsUnique().HasFilter("[write_request_id] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.Status });
        b.HasOne<AccountingProviderSwitchTargetTransferBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingProviderSwitchStagedRecord>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, Id = x.StagedRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceIntegrationWriteCommandRecord>().WithMany().HasForeignKey(x => x.WriteRequestId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchTargetTransferAttemptConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchTargetTransferAttempt>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchTargetTransferAttempt> b)
    {
        b.ToTable("accounting_provider_switch_target_transfer_attempts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.ItemId).HasColumnName("item_id");
        b.Property(x => x.AttemptNumber).HasColumnName("attempt_number"); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32); b.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(100);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.ProviderAcceptedRequest).HasColumnName("provider_accepted_request"); b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.AttemptNumber }).IsUnique();
        b.HasOne<AccountingProviderSwitchTargetTransferItem>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.BatchId, x.ItemId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.BatchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchTargetAcknowledgementConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchTargetAcknowledgement>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchTargetAcknowledgement> b)
    {
        b.ToTable("accounting_provider_switch_target_acknowledgements"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.ItemId).HasColumnName("item_id");
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64); b.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(256); b.Property(x => x.AcknowledgementHash).HasColumnName("acknowledgement_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.ReceivedUtc).HasColumnName("received_at");
        b.HasIndex(x => new { x.CompanyId, x.ItemId }).IsUnique();
        b.HasOne<AccountingProviderSwitchTargetTransferItem>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.BatchId, x.ItemId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.BatchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
