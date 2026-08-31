using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyAccountingClosePolicyConfiguration : IEntityTypeConfiguration<CompanyAccountingClosePolicy>
{
    public void Configure(EntityTypeBuilder<CompanyAccountingClosePolicy> b)
    {
        b.ToTable("company_accounting_close_policies"); b.HasKey(x => x.Id);
        b.Property(x => x.MaterialityThreshold).HasColumnType("decimal(19,4)");
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseReadinessSnapshotConfiguration : IEntityTypeConfiguration<AccountingCloseReadinessSnapshot>
{
    public void Configure(EntityTypeBuilder<AccountingCloseReadinessSnapshot> b)
    {
        b.ToTable("accounting_close_readiness_snapshots"); b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.EvidenceHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.TrialBalanceChecksum).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReviewReason).HasMaxLength(1000); b.Property(x => x.FailureCode).HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasMaxLength(2000); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.SnapshotNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.CloseInstance).WithMany().HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseReadinessCheckConfiguration : IEntityTypeConfiguration<AccountingCloseReadinessCheck>
{
    public void Configure(EntityTypeBuilder<AccountingCloseReadinessCheck> b)
    {
        b.ToTable("accounting_close_readiness_checks"); b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasMaxLength(64).IsRequired(); b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired(); b.Property(x => x.Amount).HasColumnType("decimal(19,4)");
        b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.EvidenceJson).HasMaxLength(16000).IsRequired();
        b.Property(x => x.EvidenceHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.SnapshotId, x.Code });
        b.HasOne(x => x.Snapshot).WithMany(x => x.Checks).HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseWaiverConfiguration : IEntityTypeConfiguration<AccountingCloseWaiver>
{
    public void Configure(EntityTypeBuilder<AccountingCloseWaiver> b)
    {
        b.ToTable("accounting_close_waivers"); b.HasKey(x => x.Id);
        b.Property(x => x.CheckCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.CheckEvidenceHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.Property(x => x.Amount).HasColumnType("decimal(19,4)");
        b.Property(x => x.EvidenceDocumentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.Status, x.ExpiresUtc });
        b.HasIndex(x => new { x.CompanyId, x.SnapshotId, x.CheckCode, x.CheckEvidenceHash });
        b.HasOne<AccountingCloseInstance>().WithMany().HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Snapshot).WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EvidenceDocument).WithMany().HasForeignKey(x => x.EvidenceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseReopenRequestConfiguration : IEntityTypeConfiguration<AccountingCloseReopenRequest>
{
    public void Configure(EntityTypeBuilder<AccountingCloseReopenRequest> b)
    {
        b.ToTable("accounting_close_reopen_requests"); b.HasKey(x => x.Id);
        b.Property(x => x.PriorSnapshotHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.Property(x => x.Scope).HasMaxLength(1000).IsRequired();
        b.Property(x => x.CorrectionPath).HasMaxLength(1000).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.Status, x.RequestedUtc });
        b.HasOne<AccountingCloseInstance>().WithMany().HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PriorSnapshot).WithMany().HasForeignKey(x => x.PriorSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseSignOffConfiguration : IEntityTypeConfiguration<AccountingCloseSignOff>
{
    public void Configure(EntityTypeBuilder<AccountingCloseSignOff> b)
    {
        b.ToTable("accounting_close_sign_offs"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(32).IsRequired();
        b.Property(x => x.EvidenceHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.ActorRole).HasMaxLength(64).IsRequired(); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.OccurredUtc });
        b.HasIndex(x => new { x.CompanyId, x.SnapshotId, x.Action });
        b.HasOne<AccountingCloseInstance>().WithMany().HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseReadinessSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseReopenRequest>().WithMany().HasForeignKey(x => x.ReopenRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
