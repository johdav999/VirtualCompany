using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class DemoScenarioCommandExecutionConfiguration : IEntityTypeConfiguration<DemoScenarioCommandExecution>
{
    public void Configure(EntityTypeBuilder<DemoScenarioCommandExecution> builder)
    {
        builder.ToTable("demo_scenario_command_executions", table =>
        {
            table.HasCheckConstraint("CK_demo_scenario_command_executions_generation", "reset_generation >= 1");
            table.HasCheckConstraint("CK_demo_scenario_command_executions_step", "step_number >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        builder.Property(x => x.ResetGeneration).HasColumnName("reset_generation").IsRequired();
        builder.Property(x => x.StepNumber).HasColumnName("step_number").IsRequired();
        builder.Property(x => x.CommandName).HasColumnName("command_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.Disposition).HasColumnName("disposition").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.ResetGeneration, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.ResetGeneration, x.StepNumber }).IsUnique();
        builder.HasOne(x => x.Run).WithMany()
            .HasForeignKey(nameof(DemoScenarioCommandExecution.CompanyId), nameof(DemoScenarioCommandExecution.RunId))
            .HasPrincipalKey(nameof(DemoScenarioRun.CompanyId), nameof(DemoScenarioRun.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

