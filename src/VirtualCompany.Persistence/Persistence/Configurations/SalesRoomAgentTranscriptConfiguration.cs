using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesRoomAgentTranscriptConfiguration : IEntityTypeConfiguration<SalesRoomAgentTranscript>
{
    public void Configure(EntityTypeBuilder<SalesRoomAgentTranscript> b)
    {
        b.ToTable("sales_room_agent_transcripts"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.TranscriptSegmentId }).IsUnique().HasFilter("[TranscriptSegmentId] IS NOT NULL");
        b.HasOne<SalesMeetingTranscriptSegment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TranscriptSegmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.TrackIdHash).HasMaxLength(128); b.Property(x => x.Text).HasMaxLength(8000);
        b.HasIndex(x => new { x.CompanyId, x.RoomId, x.StartedUtc });
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesRoomParticipant>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParticipantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
