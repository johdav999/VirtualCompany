using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyOutboxMessageConfiguration : IEntityTypeConfiguration<CompanyOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CompanyOutboxMessage> builder)
    {
        builder.ToTable("company_outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Topic).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MessageType).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.CausationId).HasMaxLength(128);
        builder.Property(x => x.HeadersJson).HasMaxLength(4000);
        builder.Property(x => x.OccurredUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.AvailableUtc).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(status => status.ToStorageValue(), value => CompanyOutboxMessageStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(CompanyOutboxMessageStatusValues.DefaultStatus)
            .HasSentinel((CompanyOutboxMessageStatus)0)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastAttemptUtc);
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.Property(x => x.ClaimToken).HasMaxLength(64).IsConcurrencyToken();
        builder.Property(x => x.ProcessedUtc).IsConcurrencyToken();

        builder.HasIndex(x => new { x.ProcessedUtc, x.AvailableUtc, x.AttemptCount });
        builder.HasIndex(x => new { x.ProcessedUtc, x.ClaimedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.AvailableUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Topic, x.IdempotencyKey }).HasFilter("\"IdempotencyKey\" IS NOT NULL").IsUnique();
        builder.HasIndex(x => new { x.Status, x.AvailableUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProcessedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

