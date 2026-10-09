using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

// Applied explicitly by each owning entity configuration, never discovered as a second owner.
internal static class FinanceSourceTrackingMapping
{
    internal static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder, string? constraintName = null)
        where TEntity : class
    {
        builder.Property<string>("SourceType")
            .HasColumnName("source_type")
            .HasMaxLength(32)
            .HasDefaultValue(FinanceRecordSourceTypes.Manual)
            .IsRequired();
        builder.Property<string?>("ProviderKey")
            .HasColumnName("provider_key")
            .HasMaxLength(64);
        builder.Property<string?>("ProviderExternalId")
            .HasColumnName("provider_external_id")
            .HasMaxLength(256);
        builder.Property<Guid?>("FinanceExternalReferenceId")
            .HasColumnName("finance_external_reference_id");

        builder.ToTable(t => t.HasCheckConstraint(constraintName ?? $"CK_{builder.Metadata.GetTableName()}_source_type", FinanceRecordSourceTypes.BuildCheckConstraintSql("source_type")));
        builder.HasIndex("CompanyId", "SourceType");
        builder.HasIndex("CompanyId", "ProviderKey", "ProviderExternalId")
            .HasFilter("provider_key IS NOT NULL AND provider_external_id IS NOT NULL");
        builder.HasOne(typeof(FinanceExternalReference))
            .WithMany()
            .HasForeignKey("FinanceExternalReferenceId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
