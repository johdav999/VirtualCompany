using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AuditPackageConfiguration : IEntityTypeConfiguration<AuditPackage>
{
    public void Configure(EntityTypeBuilder<AuditPackage> builder)
    {
        builder.ToTable("audit_packages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ScopeVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ScopeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SnapshotVersionsJson).HasMaxLength(16000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ManifestJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ManifestChecksum).HasMaxLength(64);
        builder.Property(x => x.PackageChecksum).HasMaxLength(64);
        builder.Property(x => x.StorageKey).HasMaxLength(1024);
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.MediaType).HasMaxLength(100);
        builder.Property(x => x.RequestedByRole).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.SafeFailureSummary).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.ScopeKey, x.ScopeVersion, x.ScopeHash }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RetainUntilUtc });
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditPackageApprovalConfiguration : IEntityTypeConfiguration<AuditPackageApproval>
{
    public void Configure(EntityTypeBuilder<AuditPackageApproval> builder)
    {
        builder.ToTable("audit_package_approvals"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Decision).HasMaxLength(32).IsRequired(); builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.DecidedUtc });
        builder.HasOne(x => x.Package).WithMany(x => x.Approvals).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditPackageArtifactConfiguration : IEntityTypeConfiguration<AuditPackageArtifact>
{
    public void Configure(EntityTypeBuilder<AuditPackageArtifact> builder)
    {
        builder.ToTable("audit_package_artifacts"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ArtifactType).HasMaxLength(100).IsRequired(); builder.Property(x => x.Path).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired(); builder.Property(x => x.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(500).IsRequired(); builder.Property(x => x.SourceVersion).HasMaxLength(128);
        builder.Property(x => x.DefinitionVersion).HasMaxLength(128); builder.Property(x => x.Checksum).HasMaxLength(64);
        builder.Property(x => x.SafeDetail).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.Status });
        builder.HasOne(x => x.Package).WithMany(x => x.Artifacts).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditPackageGenerationAttemptConfiguration : IEntityTypeConfiguration<AuditPackageGenerationAttempt>
{
    public void Configure(EntityTypeBuilder<AuditPackageGenerationAttempt> builder)
    {
        builder.ToTable("audit_package_generation_attempts"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired(); builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.SafeSummary).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.AttemptNumber }).IsUnique();
        builder.HasOne(x => x.Package).WithMany(x => x.GenerationAttempts).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditPackageDownloadAuthorizationConfiguration : IEntityTypeConfiguration<AuditPackageDownloadAuthorization>
{
    public void Configure(EntityTypeBuilder<AuditPackageDownloadAuthorization> builder)
    {
        builder.ToTable("audit_package_download_authorizations"); builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.TokenHash }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.UserId, x.ExpiresUtc });
        builder.HasOne(x => x.Package).WithMany(x => x.DownloadAuthorizations).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditPackageVerificationResultConfiguration : IEntityTypeConfiguration<AuditPackageVerificationResult>
{
    public void Configure(EntityTypeBuilder<AuditPackageVerificationResult> builder)
    {
        builder.ToTable("audit_package_verification_results"); builder.HasKey(x => x.Id);
        builder.Property(x => x.PackageChecksum).HasMaxLength(64).IsRequired(); builder.Property(x => x.ManifestChecksum).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultCode).HasMaxLength(100).IsRequired(); builder.Property(x => x.SafeSummary).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.PackageId, x.VerifiedUtc });
        builder.HasOne(x => x.Package).WithMany(x => x.VerificationResults).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
    }
}
