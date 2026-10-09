using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class TaskTypePolicyConfiguration : IEntityTypeConfiguration<TaskTypePolicy>
{
    public void Configure(EntityTypeBuilder<TaskTypePolicy> b)
    {
        b.ToTable("task_type_policies");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        b.Property(x=>x.TaskType).HasMaxLength(100).IsRequired();
        b.Property(x=>x.Version).IsConcurrencyToken();b.Property(x=>x.UsageVersion).IsConcurrencyToken();
        b.HasIndex(x=>new{x.CompanyId,x.AgentId,x.TaskType}).IsUnique();
        b.HasOne<Agent>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.AgentId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
    }
}
internal sealed class TaskTypePolicyRevisionConfiguration : IEntityTypeConfiguration<TaskTypePolicyRevision>
{
    public void Configure(EntityTypeBuilder<TaskTypePolicyRevision> b)
    {
        b.ToTable("task_type_policy_revisions");b.HasKey(x=>x.Id);
        b.Property(x=>x.Mode).HasMaxLength(32).IsRequired();b.Property(x=>x.Rationale).HasMaxLength(2000).IsRequired();
        b.Property(x=>x.PreviewHash).HasMaxLength(64).IsRequired();b.Property(x=>x.PreviewInputs).HasMaxLength(12000).IsRequired();
        b.HasIndex(x=>new{x.CompanyId,x.PolicyId,x.Version}).IsUnique();
        b.HasOne<TaskTypePolicy>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.PolicyId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
    }
}
