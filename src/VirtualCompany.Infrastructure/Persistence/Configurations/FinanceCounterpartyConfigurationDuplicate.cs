using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinanceCounterpartyConfigurationDuplicate : IEntityTypeConfiguration<FinanceCounterparty>
{
    public void Configure(EntityTypeBuilder<FinanceCounterparty> builder)
    {
        builder.ToTable("finance_counterparties");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CounterpartyType).HasColumnName("counterparty_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Name });
        builder.HasIndex(x => new { x.CompanyId, x.CounterpartyType });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

