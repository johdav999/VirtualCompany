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
internal sealed class WorkflowExceptionConfiguration : IEntityTypeConfiguration<WorkflowException>
{
    public void Configure(EntityTypeBuilder<WorkflowException> builder)
    {
        builder.ToTable("workflow_exceptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(x => x.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExceptionType)
            .HasColumnName("exception_type")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowExceptionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowExceptionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowExceptionStatusValues.DefaultStatus)
            .HasSentinel((WorkflowExceptionStatus)0)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Details).HasColumnName("details").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(x => x.TechnicalDetailsJson)
            .HasColumnName("technical_details_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ReviewedUtc).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(2000);

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId, x.StepKey, x.ExceptionType, x.Status })
            .HasFilter("\"status\" = 'open'")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowInstance)
            .WithMany(x => x.Exceptions)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Definition)
            .WithMany(x => x.Exceptions)
            .HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

