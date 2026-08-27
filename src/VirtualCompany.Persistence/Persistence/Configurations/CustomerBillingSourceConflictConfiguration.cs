using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CustomerBillingSourceConflictConfiguration : IEntityTypeConfiguration<CustomerBillingSourceConflict>
{
    public void Configure(EntityTypeBuilder<CustomerBillingSourceConflict> b)
    {
        b.ToTable("customer_billing_source_conflicts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.ProfileId).HasColumnName("profile_id"); b.Property(x => x.CounterpartyId).HasColumnName("counterparty_id");
        b.Property(x => x.BaseVersion).HasColumnName("base_version"); b.Property(x => x.ExistingSourceKind).HasColumnName("existing_source_kind").HasMaxLength(32);
        b.Property(x => x.IncomingSourceKind).HasColumnName("incoming_source_kind").HasMaxLength(32);
        b.Property(x => x.IncomingSourceReference).HasColumnName("incoming_source_reference").HasMaxLength(200);
        b.Property(x => x.ChangedFields).HasColumnName("changed_fields").HasMaxLength(2000);
        b.Property(x => x.IncomingSnapshotJson).HasColumnName("incoming_snapshot_json").HasMaxLength(16000);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.UsedIncomingValues).HasColumnName("used_incoming_values");
        b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
        b.Property(x => x.DetectedByUserId).HasColumnName("detected_by_user_id"); b.Property(x => x.DetectedUtc).HasColumnName("detected_at");
        b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id"); b.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.ProfileId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.CounterpartyId, x.DetectedUtc });
        b.HasOne<CustomerBillingProfile>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ProfileId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
