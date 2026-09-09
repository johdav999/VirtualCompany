using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesRoomInvitationGrantConfiguration : IEntityTypeConfiguration<SalesRoomInvitationGrant>
{
    public void Configure(EntityTypeBuilder<SalesRoomInvitationGrant> b)
    {
        b.ToTable("sales_room_invitation_grants"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.SecretHash).HasMaxLength(64); b.HasIndex(x => x.SecretHash).IsUnique().HasFilter("[SecretHash] IS NOT NULL");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
