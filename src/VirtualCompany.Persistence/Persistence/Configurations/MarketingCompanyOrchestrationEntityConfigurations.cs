using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class MarketingWorkEvidenceConfiguration : IEntityTypeConfiguration<MarketingWorkEvidence>
{
    public void Configure(EntityTypeBuilder<MarketingWorkEvidence> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_work_evidence");
        b.Property(x => x.MarketingOperatingRunId).HasColumnName("marketing_operating_run_id");
        b.Property(x => x.OperatingInitiativeId).HasColumnName("operating_initiative_id");
        b.Property(x => x.WorkTaskId).HasColumnName("work_task_id");
        b.Property(x => x.RecordType).HasColumnName("record_type").HasMaxLength(24);
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x => x.EvidenceVersion).HasColumnName("evidence_version").HasMaxLength(100);
        b.Property(x => x.CompletedArtifactsJson).HasColumnName("completed_artifacts_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.ExpectedResultsJson).HasColumnName("expected_results_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.ActualResultsJson).HasColumnName("actual_results_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        b.Property(x => x.DataGapsJson).HasColumnName("data_gaps_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.BlockersJson).HasColumnName("blockers_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.DependenciesJson).HasColumnName("dependencies_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.ChangedForecastJson).HasColumnName("changed_forecast_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.Lessons).HasColumnName("lessons").HasMaxLength(4000);
        b.Property(x => x.RequestedNextAction).HasColumnName("requested_next_action").HasMaxLength(2000);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.MarketingOperatingRunId, x.RecordType, x.Version }).IsUnique();
        b.HasOne<MarketingOperatingRun>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingOperatingRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MarketingCompanySignalConfiguration : IEntityTypeConfiguration<MarketingCompanySignal>
{
    public void Configure(EntityTypeBuilder<MarketingCompanySignal> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_company_signals");
        b.Property(x => x.MarketingOperatingRunId).HasColumnName("marketing_operating_run_id");
        b.Property(x => x.SignalType).HasColumnName("signal_type").HasMaxLength(64);
        b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24);
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24);
        b.Property(x => x.CycleEvaluationRequested).HasColumnName("cycle_evaluation_requested");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.Severity, x.CreatedUtc });
        b.HasOne<MarketingOperatingRun>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MarketingOperatingRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
