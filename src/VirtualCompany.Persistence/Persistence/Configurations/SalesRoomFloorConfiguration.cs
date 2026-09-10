using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesRoomFloorConfiguration : IEntityTypeConfiguration<SalesRoomFloor>
{
    public void Configure(EntityTypeBuilder<SalesRoomFloor> b)
    {
        b.ToTable("sales_room_floors"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.State).HasMaxLength(32); b.Property(x => x.PendingTurnState).HasMaxLength(40);
        b.Property(x => x.ControlMode).HasMaxLength(24); b.Property(x => x.ResumeMarker).HasMaxLength(1000);
        b.Property(x => x.PlaybackStopState).HasMaxLength(24); b.Property(x => x.TurnGeneration).HasDefaultValue(1L);
        b.Property(x => x.ResponseGeneration).HasDefaultValue(1L); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.RoomId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.LastPlaybackStopId });
        b.HasOne<SalesBrowserRoom>().WithOne().HasForeignKey<SalesRoomFloor>(x => new { x.CompanyId, x.RoomId })
            .HasPrincipalKey<SalesBrowserRoom>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.HostParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FloorOwnerParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PendingParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PreauthorizedCoHostParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesMeetingQuestion>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PendingQuestionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SalesRoomPlaybackStopAcknowledgementConfiguration : IEntityTypeConfiguration<SalesRoomPlaybackStopAcknowledgement>
{
    public void Configure(EntityTypeBuilder<SalesRoomPlaybackStopAcknowledgement> b)
    {
        b.ToTable("sales_room_playback_stop_acknowledgements"); b.HasKey(x => x.Id);
        b.Property(x => x.ConnectionIdHash).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.RoomId, x.StopId, x.ParticipantId, x.ParticipantGeneration }).IsUnique();
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
