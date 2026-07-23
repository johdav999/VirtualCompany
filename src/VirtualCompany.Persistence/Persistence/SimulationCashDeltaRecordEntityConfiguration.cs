using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SimulationCashDeltaRecordEntityConfiguration : IEntityTypeConfiguration<SimulationCashDeltaRecord>
{
    public void Configure(EntityTypeBuilder<SimulationCashDeltaRecord> builder)
    {
        builder.ToTable("simulation_cash_delta_records");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_simulation_cash_delta_records_cash_snapshot", "cash_after = cash_before + cash_delta");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SimulationEventRecordId).HasColumnName("simulation_event_record_id").IsRequired();
        builder.Property(x => x.SimulationDateUtc).HasColumnName("simulation_date_at").IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id");
        builder.Property(x => x.CashBefore).HasColumnName("cash_before").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CashDelta).HasColumnName("cash_delta").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CashAfter).HasColumnName("cash_after").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SimulationDateUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SimulationEventRecordId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
