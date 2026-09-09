using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesRoomParticipantConfiguration : IEntityTypeConfiguration<SalesRoomParticipant>
{
    public void Configure(EntityTypeBuilder<SalesRoomParticipant> b)
    {
        b.ToTable("sales_room_participants"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.DisplayName).HasMaxLength(80); b.Property(x => x.State).HasMaxLength(40); b.Property(x => x.SessionHash).HasMaxLength(64); b.HasIndex(x => x.SessionHash).IsUnique().HasFilter("[SessionHash] IS NOT NULL"); b.HasIndex(x => new { x.CompanyId, x.RoomId, x.MemberUserId }).IsUnique().HasFilter("[MemberUserId] IS NOT NULL");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
