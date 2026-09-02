using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyResponsibilityAssignmentConfiguration : IEntityTypeConfiguration<CompanyResponsibilityAssignment>
{
    public void Configure(EntityTypeBuilder<CompanyResponsibilityAssignment> builder)
    {
        builder.ToTable("company_responsibility_assignments");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.ResponsibilityArea).HasColumnName("responsibility_area")
            .HasConversion(x => x.ToStorageValue(), x => ResponsibilityAreaValues.Parse(x)).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AssignmentKind).HasColumnName("assignment_kind")
            .HasConversion(x => x.ToStorageValue(), x => ResponsibilityAssignmentKindValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AssignedMembershipId).HasColumnName("assigned_membership_id");
        builder.Property(x => x.PrimaryAgentId).HasColumnName("primary_agent_id");
        builder.Property(x => x.AuthorityLevel).HasColumnName("authority_level")
            .HasConversion(x => x.ToStorageValue(), x => AgentAutonomyLevelValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ApprovalPolicyId).HasColumnName("approval_policy_id");
        builder.Property(x => x.EscalationMembershipId).HasColumnName("escalation_membership_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ResponsibilityArea, x.AssignmentKind });
        builder.HasIndex(x => new { x.CompanyId, x.ResponsibilityArea }).IsUnique()
            .HasFilter("[assignment_kind] = N'primary'").HasDatabaseName("UX_company_responsibility_primary");
        builder.HasIndex(x => new { x.CompanyId, x.AssignedMembershipId });
        builder.HasOne(x => x.Company).WithMany(x => x.ResponsibilityAssignments).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AssignedMembership).WithMany().HasForeignKey(x => new { x.CompanyId, x.AssignedMembershipId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.EscalationMembership).WithMany().HasForeignKey(x => new { x.CompanyId, x.EscalationMembershipId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PrimaryAgent).WithMany().HasForeignKey(x => new { x.CompanyId, x.PrimaryAgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
