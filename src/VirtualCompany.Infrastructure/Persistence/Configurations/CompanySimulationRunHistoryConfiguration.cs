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
internal sealed class CompanySimulationRunHistoryConfiguration : IEntityTypeConfiguration<CompanySimulationRunHistory>
{
    public void Configure(EntityTypeBuilder<CompanySimulationRunHistory> builder)
    {
        builder.ToTable("company_simulation_run_histories");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => CompanySimulationStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.StartSimulatedUtc).HasColumnName("start_simulated_at").IsRequired();
        builder.Property(x => x.CurrentSimulatedUtc).HasColumnName("current_simulated_at");
        builder.Property(x => x.GenerationEnabled).HasColumnName("generation_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Seed).HasColumnName("seed").IsRequired();
        builder.Property(x => x.DeterministicConfigurationJson).HasColumnName("deterministic_configuration_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.InjectedAnomalies).HasColumnName("injected_anomalies_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.Warnings).HasColumnName("warnings_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.Errors).HasColumnName("errors_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SessionId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.StatusTransitions).WithOne(x => x.RunHistory).HasForeignKey(x => x.RunHistoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.DayLogs).WithOne(x => x.RunHistory).HasForeignKey(x => x.RunHistoryId).OnDelete(DeleteBehavior.Cascade);
    }
}

