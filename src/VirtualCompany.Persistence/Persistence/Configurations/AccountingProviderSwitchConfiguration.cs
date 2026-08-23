using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchConfiguration : IEntityTypeConfiguration<AccountingProviderSwitch>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitch> builder)
    {
        builder.ToTable("accounting_provider_switches", table =>
        {
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_source_endpoint",
                "([source_kind] = 'internal' AND [source_provider_key] IS NULL) OR ([source_kind] = 'external' AND [source_provider_key] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_target_endpoint",
                "([target_kind] = 'internal' AND [target_provider_key] IS NULL) OR ([target_kind] = 'external' AND [target_provider_key] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_distinct_endpoints",
                "NOT ([source_kind] = [target_kind] AND COALESCE([source_provider_key], '') = COALESCE([target_provider_key], ''))");
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_strategy",
                "[migration_strategy] IN ('opening_balances_and_open_items', 'current_fiscal_year', 'full_history')");
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_status",
                "[status] IN ('draft', 'assessing', 'ready_for_planning', 'plan_awaiting_approval', 'preparing_target', 'rehearsal_passed', 'scheduled', 'source_frozen', 'reconciling', 'activation_awaiting_approval', 'target_authoritative', 'monitoring', 'completed', 'blocked', 'cancelled', 'recovery')");
            table.HasCheckConstraint("CK_accounting_provider_switches_version", "[version] > 0");
            table.HasCheckConstraint(
                "CK_accounting_provider_switches_cancellation",
                "([status] = 'cancelled' AND [cancelled_at] IS NOT NULL AND [cancelled_by_user_id] IS NOT NULL AND [cancellation_reason] IS NOT NULL) OR ([status] <> 'cancelled' AND [cancelled_at] IS NULL AND [cancelled_by_user_id] IS NULL AND [cancellation_reason] IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(16).IsRequired();
        builder.Property(x => x.SourceProviderKey).HasColumnName("source_provider_key").HasMaxLength(64);
        builder.Property(x => x.TargetKind).HasColumnName("target_kind").HasMaxLength(16).IsRequired();
        builder.Property(x => x.TargetProviderKey).HasColumnName("target_provider_key").HasMaxLength(64);
        builder.Property(x => x.EffectiveFiscalPeriodId).HasColumnName("effective_fiscal_period_id").IsRequired();
        builder.Property(x => x.MigrationStrategy).HasColumnName("migration_strategy").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ResponsibleUserId).HasColumnName("responsible_user_id").IsRequired();
        builder.Property(x => x.ResponsibleAgentId).HasColumnName("responsible_agent_id");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.BlockedFromStatus).HasColumnName("blocked_from_status").HasMaxLength(64);
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.StatusChangedUtc).HasColumnName("status_changed_at").IsRequired();
        builder.Property(x => x.BlockedUtc).HasColumnName("blocked_at");
        builder.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("[status] <> 'completed' AND [status] <> 'cancelled'")
            .HasDatabaseName("UX_accounting_provider_switches_company_active");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.EffectiveFiscalPeriodId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.EffectiveFiscalPeriod).WithMany().HasForeignKey(x => x.EffectiveFiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResponsibleAgent).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ResponsibleAgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.Source);
        builder.Ignore(x => x.Target);
        builder.Ignore(x => x.IsTerminal);
        builder.Ignore(x => x.CanUpdatePlan);
        builder.Ignore(x => x.CanCancel);
    }
}
