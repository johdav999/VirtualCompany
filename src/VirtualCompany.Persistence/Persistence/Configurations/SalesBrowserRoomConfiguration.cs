using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesBrowserRoomConfiguration : IEntityTypeConfiguration<SalesBrowserRoom>
{
    public void Configure(EntityTypeBuilder<SalesBrowserRoom> b)
    {
        b.ToTable("sales_browser_rooms"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.State).HasMaxLength(40); b.Property(x => x.AgentHealth).HasMaxLength(40);
        b.Property(x => x.AgentVoiceHealth).HasMaxLength(40).HasDefaultValue("not_connected");
        b.Property(x => x.AgentTurnGeneration).HasDefaultValue(1L); b.Property(x => x.AgentLastErrorCode).HasMaxLength(100);
        b.Property(x => x.AgentLastErrorSummary).HasMaxLength(1000); b.Property(x => x.ProviderReference).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.MeetingSessionId }).IsUnique(); b.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.State, x.ExpiresUtc });
        b.HasOne<SalesMeetingSession>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MeetingSessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AgentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x=>new{x.CompanyId,x.InvitationId}).IsUnique().HasFilter("[InvitationId] IS NOT NULL");
        b.HasOne<SalesMeetingInvitation>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.InvitationId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
