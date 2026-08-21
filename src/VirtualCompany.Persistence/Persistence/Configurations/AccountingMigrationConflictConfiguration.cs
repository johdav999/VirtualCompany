using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingMigrationConflictConfiguration : IEntityTypeConfiguration<AccountingMigrationConflict>
{
    public void Configure(EntityTypeBuilder<AccountingMigrationConflict> builder)
    {
        builder.ToTable("accounting_migration_conflicts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MigrationRunId).HasColumnName("migration_run_id").IsRequired();
        builder.Property(x => x.TargetVersion).HasColumnName("target_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.OperatorAction).HasColumnName("operator_action").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResolutionSummary).HasColumnName("resolution_summary").HasMaxLength(1000);
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_utc");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasIndex(x => new { x.CompanyId, x.MigrationRunId, x.EntityType, x.EntityId, x.ReasonCode }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MigrationRun).WithMany(x => x.Conflicts)
            .HasForeignKey(x => new { x.CompanyId, x.MigrationRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.FiscalPeriod).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
