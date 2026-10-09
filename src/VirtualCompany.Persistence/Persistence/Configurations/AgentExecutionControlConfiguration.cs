using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AgentExecutionControlConfiguration : IEntityTypeConfiguration<AgentExecutionControl>
{
    public void Configure(EntityTypeBuilder<AgentExecutionControl> b)
    { b.ToTable("agent_execution_controls"); b.HasKey(x=>x.Id); b.HasIndex(x=>new{x.CompanyId,x.ScopeId}).IsUnique(); b.Property(x=>x.Version).IsConcurrencyToken(); b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AgentExecutionControlCommandConfiguration : IEntityTypeConfiguration<AgentExecutionControlCommand>
{
    public void Configure(EntityTypeBuilder<AgentExecutionControlCommand> b)
    { b.ToTable("agent_execution_control_commands"); b.HasKey(x=>x.Id); b.HasIndex(x=>new{x.CompanyId,x.ScopeId,x.Version}).IsUnique(); b.Property(x=>x.Reason).HasMaxLength(500); b.Property(x=>x.RequestHash).HasMaxLength(64); b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AgentExecutionAdmissionConfiguration : IEntityTypeConfiguration<AgentExecutionAdmission>
{
    public void Configure(EntityTypeBuilder<AgentExecutionAdmission> b)
    { b.ToTable("agent_execution_admissions"); b.HasKey(x=>x.Id); b.HasIndex(x=>new{x.CompanyId,x.Boundary,x.BusinessKey}).IsUnique(); b.Property(x=>x.Boundary).HasMaxLength(64); b.Property(x=>x.BusinessKey).HasMaxLength(200); b.Property(x=>x.Version).IsConcurrencyToken(); b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict); }
}
