using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CollaborationExecutionLeaseConfiguration : IEntityTypeConfiguration<CollaborationExecutionLease>
{
    public void Configure(EntityTypeBuilder<CollaborationExecutionLease> b)
    {
        b.ToTable("collaboration_execution_leases"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(128).IsRequired(); b.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
