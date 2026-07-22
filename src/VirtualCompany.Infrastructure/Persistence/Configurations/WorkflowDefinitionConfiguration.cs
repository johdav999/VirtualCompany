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
internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    private static readonly ValueConverter<WorkflowTriggerType, string> TriggerTypeConverter =
        new(
            value => value.ToStorageValue(),
            value => ParseWorkflowTriggerType(value));

    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(100);
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.TriggerType)
            .HasColumnName("trigger_type")
            .HasConversion(TriggerTypeConverter)
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowTriggerTypeValues.DefaultType)
            .HasSentinel((WorkflowTriggerType)0)
            .IsRequired();
        builder.Property(x => x.DefinitionJson)
            .HasColumnName("definition_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Code, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Code });
        builder.HasIndex(x => new { x.CompanyId, x.Active });
        builder.HasIndex(x => new { x.CompanyId, x.Active, x.Department });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static WorkflowTriggerType ParseWorkflowTriggerType(string value) =>
        WorkflowTriggerTypeValues.TryParse(value, out var triggerType)
            ? triggerType
            : WorkflowTriggerTypeValues.DefaultType;
}

