using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class SalesRoomOperationConfiguration : IEntityTypeConfiguration<SalesRoomOperation>
{
    public void Configure(EntityTypeBuilder<SalesRoomOperation> b)
    {
        b.ToTable("sales_room_operations"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.State).HasMaxLength(40); b.Property(x => x.RequestHash).HasMaxLength(64); b.Property(x => x.ProblemCode).HasMaxLength(100); b.HasIndex(x => new { x.CompanyId, x.CommandId }).IsUnique(); b.HasIndex(x => new { x.State, x.LeaseUntilUtc });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<SalesBrowserRoom>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RoomId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
