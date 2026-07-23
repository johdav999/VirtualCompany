using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyBriefingConfiguration : IEntityTypeConfiguration<CompanyBriefing>
{
    public void Configure(EntityTypeBuilder<CompanyBriefing> builder)
    {
        builder.ToTable("company_briefings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BriefingType)
            .HasColumnName("briefing_type")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at").IsRequired();
        builder.Property(x => x.PeriodEndUtc).HasColumnName("period_end_at").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SummaryBody).HasColumnName("summary_body").IsRequired();
        builder.Property(x => x.StructuredPayload)
            .HasColumnName("structured_payload_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.SourceReferences)
            .HasColumnName("source_refs_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PreferenceSnapshot)
            .HasColumnName("preference_snapshot_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.GeneratedUtc).HasColumnName("generated_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BriefingType, x.GeneratedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.BriefingType, x.PeriodStartUtc, x.PeriodEndUtc }).IsUnique();
        builder.HasIndex(x => x.MessageId).IsUnique().HasFilter("message_id IS NOT NULL");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
    }
}

