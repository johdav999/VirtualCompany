using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ContextRetrievalConfiguration : IEntityTypeConfiguration<ContextRetrieval>
{
    public void Configure(EntityTypeBuilder<ContextRetrieval> builder)
    {
        builder.ToTable("context_retrievals");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.TaskId);
        builder.Property(x => x.QueryText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.QueryHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.RetrievalPurpose).HasMaxLength(256);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId, x.CreatedUtc });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

