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
internal sealed class FinanceSeedAnomalyConfiguration : IEntityTypeConfiguration<FinanceSeedAnomaly>
{
    public void Configure(EntityTypeBuilder<FinanceSeedAnomaly> builder)
    {
        builder.ToTable("finance_seed_anomalies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AnomalyType).HasColumnName("anomaly_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ScenarioProfile).HasColumnName("scenario_profile").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AffectedRecordIdsJson).HasColumnName("affected_record_ids_json").HasColumnType("text").IsRequired();
        builder.Property(x => x.ExpectedDetectionMetadataJson).HasColumnName("expected_detection_metadata_json").HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.AnomalyType });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

