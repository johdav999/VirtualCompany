using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class OperatingInitiativeCollaboratorConfiguration : IEntityTypeConfiguration<OperatingInitiativeCollaborator>
{
    public void Configure(EntityTypeBuilder<OperatingInitiativeCollaborator> b)
    {
        b.ToTable("operating_initiative_collaborators");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.InitiativeId).HasColumnName("initiative_id");
        b.Property(x => x.AgentId).HasColumnName("agent_id");
        b.Property(x => x.Role).HasColumnName("role").HasMaxLength(32)
            .HasConversion(x => x.ToStorageValue(), x => OperatingCollaborationRoleValues.Parse(x));
        b.Property(x => x.Pattern).HasColumnName("pattern").HasMaxLength(32)
            .HasConversion(x => x.ToStorageValue(), x => OperatingCollaborationPatternValues.Parse(x));
        b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.Objective).HasColumnName("objective").HasMaxLength(2000);
        b.Property(x => x.ExpectedArtifact).HasColumnName("expected_artifact").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.InitiativeId, x.AgentId, x.Role }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.AgentId });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Initiative).WithMany().HasForeignKey(x => x.InitiativeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.NoAction);
    }
}
