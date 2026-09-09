using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesRoomProviderEventConfiguration : IEntityTypeConfiguration<SalesRoomProviderEvent>
{
    public void Configure(EntityTypeBuilder<SalesRoomProviderEvent> b)
    {
        b.ToTable("sales_room_provider_events"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.ProviderEventId).HasMaxLength(100); b.Property(x => x.EventType).HasMaxLength(40); b.Property(x => x.ParticipantIdentity).HasMaxLength(100); b.HasIndex(x => x.ProviderEventId).IsUnique();
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
