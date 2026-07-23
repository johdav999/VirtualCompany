using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CostCenterId).HasColumnName("cost_center_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc });
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.Version });
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.Version, x.PeriodStartUtc });
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.FinanceAccountId, x.Version, x.CostCenterId })
            .HasFilter("cost_center_id IS NOT NULL")
            .IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.FinanceAccountId, x.Version })
            .HasFilter("cost_center_id IS NULL")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForecastConfiguration : IEntityTypeConfiguration<Forecast>
{
    public void Configure(EntityTypeBuilder<Forecast> builder)
    {
        builder.ToTable("forecasts");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CostCenterId).HasColumnName("cost_center_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc });
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.Version });
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.Version, x.PeriodStartUtc });
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.FinanceAccountId, x.Version, x.CostCenterId })
            .HasFilter("cost_center_id IS NOT NULL")
            .IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PeriodStartUtc, x.FinanceAccountId, x.Version })
            .HasFilter("cost_center_id IS NULL")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}