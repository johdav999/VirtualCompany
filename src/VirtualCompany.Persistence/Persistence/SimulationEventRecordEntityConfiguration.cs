using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SimulationEventRecordEntityConfiguration : IEntityTypeConfiguration<SimulationEventRecord>
{
    public void Configure(EntityTypeBuilder<SimulationEventRecord> builder)
    {
        builder.ToTable("simulation_event_records");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_simulation_event_records_cash_snapshot", "((cash_before IS NULL AND cash_delta IS NULL AND cash_after IS NULL) OR (cash_before IS NOT NULL AND cash_delta IS NOT NULL AND cash_after IS NOT NULL AND cash_after = cash_before + cash_delta))");
            t.HasCheckConstraint("CK_simulation_event_records_sequence_number_positive", "sequence_number > 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SimulationSessionId).HasColumnName("simulation_session_id");
        builder.Property(x => x.Seed).HasColumnName("seed").IsRequired();
        builder.Property(x => x.StartSimulatedUtc).HasColumnName("start_simulated_at").IsRequired();
        builder.Property(x => x.SimulationDateUtc).HasColumnName("simulation_date_at").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128);
        builder.Property(x => x.ParentEventId).HasColumnName("parent_event_id");
        builder.Property(x => x.SequenceNumber).HasColumnName("sequence_number").IsRequired();
        builder.Property(x => x.DeterministicKey).HasColumnName("deterministic_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CashBefore).HasColumnName("cash_before").HasColumnType("decimal(18,2)");
        builder.Property(x => x.CashDelta).HasColumnName("cash_delta").HasColumnType("decimal(18,2)");
        builder.Property(x => x.CashAfter).HasColumnName("cash_after").HasColumnType("decimal(18,2)");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SimulationDateUtc });
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.SimulationDateUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.SimulationSessionId });
        builder.HasIndex(x => new { x.CompanyId, x.DeterministicKey }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ParentEvent).WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentEventId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
