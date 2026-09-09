using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesRoomConsentConfiguration : IEntityTypeConfiguration<SalesRoomConsent>
{
    public void Configure(EntityTypeBuilder<SalesRoomConsent> b)
    {
        b.ToTable("sales_room_consents"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Purpose).HasMaxLength(40); b.Property(x => x.NoticeVersion).HasMaxLength(80); b.HasIndex(x => new { x.CompanyId, x.ParticipantId, x.Version }).IsUnique(); b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParticipantId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
