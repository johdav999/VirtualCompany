using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class BriefingCadenceDeliveryConfiguration : IEntityTypeConfiguration<BriefingCadenceDelivery>
{
    public void Configure(EntityTypeBuilder<BriefingCadenceDelivery> b)
    {
        b.ToTable("briefing_cadence_deliveries"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.SlotKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.ScheduledUtc });
        b.Property(x => x.Cadence).HasMaxLength(200); b.Property(x => x.SlotKey).HasMaxLength(240);
        b.Property(x => x.Status).HasMaxLength(24); b.Property(x => x.Routing).HasMaxLength(48);
        b.Property(x => x.SettingsHash).HasMaxLength(64); b.Property(x => x.ContentHash).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.CreatedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Property(x => x.UpdatedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Property(x => x.ScheduledUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
