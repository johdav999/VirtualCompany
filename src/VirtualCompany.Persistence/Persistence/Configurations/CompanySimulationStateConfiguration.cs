using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanySimulationStateConfiguration : IEntityTypeConfiguration<CompanySimulationState>
{
    public void Configure(EntityTypeBuilder<CompanySimulationState> builder)
    {
        builder.ToTable("company_simulation_states");
        builder.Ignore(x => x.RunHistories);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(status => status.ToStorageValue(), value => CompanySimulationStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.CurrentSimulatedUtc).HasColumnName("current_simulated_at").IsRequired();
        builder.Property(x => x.LastProgressedUtc).HasColumnName("last_progressed_at");
        builder.Property(x => x.GenerationEnabled).HasColumnName("generation_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Seed).HasColumnName("seed").IsRequired();
        builder.Property(x => x.ActiveSessionId).HasColumnName("active_session_id");
        builder.Property(x => x.StartSimulatedUtc).HasColumnName("start_simulated_at").IsRequired();
        builder.Property(x => x.DeterministicConfigurationJson).HasColumnName("deterministic_configuration_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.PausedUtc).HasColumnName("paused_at");
        builder.Property(x => x.StoppedUtc).HasColumnName("stopped_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_company_simulation_states_status", CompanySimulationStatusValues.BuildCheckConstraintSql("status"));
            t.HasCheckConstraint("CK_company_simulation_states_active_session", "(status = 'stopped' AND active_session_id IS NULL) OR (status IN ('running', 'paused') AND active_session_id IS NOT NULL)");
        });
        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastProgressedUtc, x.CompanyId });
        builder.HasIndex(x => new { x.CompanyId, x.ActiveSessionId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

