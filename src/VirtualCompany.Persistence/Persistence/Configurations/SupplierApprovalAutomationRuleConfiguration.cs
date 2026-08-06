using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal sealed class SupplierApprovalAutomationRuleConfiguration : IEntityTypeConfiguration<SupplierApprovalAutomationRule>
{
    public void Configure(EntityTypeBuilder<SupplierApprovalAutomationRule> builder)
    {
        builder.ToTable("supplier_approval_automation_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupplierKey).HasColumnName("supplier_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.SupplierOrgNumber).HasColumnName("supplier_org_number").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.AgentDisplayName).HasColumnName("agent_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
        builder.Property(x => x.GrantedByDisplayName).HasColumnName("granted_by_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.RevokedUtc).HasColumnName("revoked_at");

        builder.HasIndex(x => new { x.CompanyId, x.SupplierKey, x.Stage }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.IsActive });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
