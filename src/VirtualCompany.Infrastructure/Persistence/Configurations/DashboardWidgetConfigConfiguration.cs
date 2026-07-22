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
internal sealed class DashboardWidgetConfigConfiguration : IEntityTypeConfiguration<DashboardWidgetConfig>
{
    public void Configure(EntityTypeBuilder<DashboardWidgetConfig> builder)
    {
        builder.ToTable("dashboard_widget_configs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DepartmentConfigId).HasColumnName("department_config_id").IsRequired();
        builder.Property(x => x.WidgetKey).HasColumnName("widget_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.WidgetType).HasColumnName("widget_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.SummaryBinding).HasColumnName("summary_binding").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Navigation).HasColumnName("navigation_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.Visibility).HasColumnName("visibility_roles_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.EmptyState).HasColumnName("empty_state_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.DepartmentConfigId, x.DisplayOrder, x.WidgetKey });
        builder.HasIndex(x => new { x.CompanyId, x.DepartmentConfigId, x.WidgetKey }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

