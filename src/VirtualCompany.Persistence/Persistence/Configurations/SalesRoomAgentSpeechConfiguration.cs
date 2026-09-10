using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesRoomAgentSpeechConfiguration : IEntityTypeConfiguration<SalesRoomAgentSpeech>
{
    public void Configure(EntityTypeBuilder<SalesRoomAgentSpeech> b)
    {
        b.ToTable("sales_room_agent_speech"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Kind).HasMaxLength(24); b.Property(x => x.Status).HasMaxLength(24);
        b.Property(x => x.ResponseGeneration).HasDefaultValue(1L);
        b.Property(x => x.ReleasedText).HasMaxLength(8000); b.Property(x => x.EvidenceJson).HasMaxLength(16000);
        b.Property(x => x.ProviderResponseId).HasMaxLength(200); b.Property(x => x.FailureCode).HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasMaxLength(1000); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.RoomId, x.CommandId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.RoomId, x.Status, x.CreatedUtc });
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesMeetingSession>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SessionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesNarrationRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.NarrationRevisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesNarrationSegment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.NarrationSegmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesMeetingQuestion>().WithMany().HasForeignKey(x => new { x.CompanyId, x.QuestionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
