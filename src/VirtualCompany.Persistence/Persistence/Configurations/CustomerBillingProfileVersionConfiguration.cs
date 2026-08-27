using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CustomerBillingProfileVersionConfiguration : IEntityTypeConfiguration<CustomerBillingProfileVersion>
{
    public void Configure(EntityTypeBuilder<CustomerBillingProfileVersion> b)
    {
        b.ToTable("customer_billing_profile_versions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.ProfileId).HasColumnName("profile_id"); b.Property(x => x.CounterpartyId).HasColumnName("counterparty_id");
        b.Property(x => x.ProfileVersion).HasColumnName("profile_version"); b.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32);
        b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200);
        b.Property(x => x.ChangedFields).HasColumnName("changed_fields").HasMaxLength(2000);
        b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(16000);
        b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64);
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ProfileId, x.ProfileVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.CounterpartyId, x.CreatedUtc });
        b.HasOne<CustomerBillingProfile>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ProfileId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
