using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAgentDelegationAuthorityConfiguration : IEntityTypeConfiguration<FinanceAgentDelegationAuthority>
{
    public void Configure(EntityTypeBuilder<FinanceAgentDelegationAuthority> builder)
    {
        builder.ToTable("finance_agent_delegation_authorities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.DelegatedActorUserId).HasColumnName("delegated_actor_user_id").IsRequired();
        builder.Property(x => x.IssuedByUserId).HasColumnName("issued_by_user_id").IsRequired();
        builder.Property(x => x.OriginatingWorkflowInstanceId).HasColumnName("originating_workflow_instance_id").IsRequired();
        builder.Property(x => x.Capability).HasColumnName("capability").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AllowedActionClasses).HasColumnName("allowed_action_classes_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.AllowedScopes).HasColumnName("allowed_scopes_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.IssuedUtc).HasColumnName("issued_utc").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_utc").IsRequired();
        builder.Property(x => x.RevokedUtc).HasColumnName("revoked_utc");
        builder.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.ExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.OriginatingWorkflowInstanceId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
