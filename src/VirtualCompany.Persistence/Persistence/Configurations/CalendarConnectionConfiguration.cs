using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CalendarConnectionConfiguration : IEntityTypeConfiguration<CalendarConnection>
{
    public void Configure(EntityTypeBuilder<CalendarConnection> builder)
    {
        builder.ToTable("calendar_connections", table =>
        {
            table.HasCheckConstraint("CK_calendar_connections_provider", ExternalAccountProviderValues.BuildCheckConstraintSql("provider"));
            table.HasCheckConstraint("CK_calendar_connections_status", ExternalConnectionStatusValues.BuildCheckConstraintSql("status"));
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ExternalAccountConnectionId).HasColumnName("external_account_connection_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider")
            .HasConversion(x => x.ToStorageValue(), x => ExternalAccountProviderValues.Parse(x))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccountEmail).HasColumnName("account_email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        builder.Property(x => x.CalendarId).HasColumnName("calendar_id").HasMaxLength(256).IsRequired();
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100);
        builder.Property(x => x.Capabilities).HasColumnName("capability_flags").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => ExternalConnectionStatusValues.Parse(x))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastHealthCheckUtc).HasColumnName("last_health_check_at");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120);
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ExternalAccountConnectionId, x.CalendarId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExternalAccountConnection).WithMany(x => x.CalendarConnections)
            .HasForeignKey(x => new { x.CompanyId, x.ExternalAccountConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
