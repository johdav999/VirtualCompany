using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderExportConfiguration : IEntityTypeConfiguration<AccountingProviderExport>
{
    public void Configure(EntityTypeBuilder<AccountingProviderExport> builder)
    {
        builder.ToTable("accounting_provider_exports", table =>
        {
            table.HasCheckConstraint(
                "CK_accounting_provider_exports_status",
                "[status] IN ('awaiting_approval', 'approved', 'executing', 'exported', 'failed', 'reconciliation_required', 'cancelled')");
            table.HasCheckConstraint("CK_accounting_provider_exports_attempt_count", "[attempt_count] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AuthorityPeriodId).HasColumnName("authority_period_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StableIdentity).HasColumnName("stable_identity").HasMaxLength(256).IsRequired();
        builder.Property(x => x.WriteRequestId).HasColumnName("write_request_id").IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        builder.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        builder.Property(x => x.ProviderExternalId).HasColumnName("provider_external_id").HasMaxLength(256);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.ReconciledByUserId).HasColumnName("reconciled_by_user_id");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        builder.Property(x => x.ReconciledUtc).HasColumnName("reconciled_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.StableIdentity }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.WriteRequestId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId, x.ProviderKey, x.Action }).IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AuthorityPeriod)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AuthorityPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LedgerEntry)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
