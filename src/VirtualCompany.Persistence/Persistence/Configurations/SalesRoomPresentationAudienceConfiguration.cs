using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesRoomPresentationAudienceConfiguration : IEntityTypeConfiguration<SalesRoomPresentationAudience>
{
    public void Configure(EntityTypeBuilder<SalesRoomPresentationAudience> builder)
    {
        builder.ToTable("sales_room_presentation_audience");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.State).HasMaxLength(24);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.RoomId, x.PresentationVersion, x.ParticipantId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RoomId, x.PresentationVersion });
        builder.HasOne<SalesBrowserRoom>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RoomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<SalesRoomParticipant>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
