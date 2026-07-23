using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanySimulationRunTransitionConfiguration : IEntityTypeConfiguration<CompanySimulationRunTransition>
{
    public void Configure(EntityTypeBuilder<CompanySimulationRunTransition> builder)
    {
        builder.ToTable("company_simulation_run_transitions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RunHistoryId).HasColumnName("run_history_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(value => value.ToStorageValue(), value => CompanySimulationStatusValues.Parse(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TransitionedUtc).HasColumnName("transitioned_at").IsRequired();
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(4000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.TransitionedUtc });
    }
}

