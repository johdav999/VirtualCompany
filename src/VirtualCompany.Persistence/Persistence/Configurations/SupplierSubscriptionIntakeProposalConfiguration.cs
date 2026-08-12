using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class SupplierSubscriptionIntakeProposalConfiguration : IEntityTypeConfiguration<SupplierSubscriptionIntakeProposal>
{
    public void Configure(EntityTypeBuilder<SupplierSubscriptionIntakeProposal> builder)
    {
        builder.ToTable("SupplierSubscriptionIntakeProposals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceFingerprint).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Classification).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.EvidenceSummary).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SupplierName).HasMaxLength(200);
        builder.Property(x => x.SupplierOrgNumber).HasMaxLength(64);
        builder.Property(x => x.AgreementName).HasMaxLength(200);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Cadence).HasMaxLength(32);
        builder.Property(x => x.StartDateUtc).HasColumnType("date");
        builder.Property(x => x.EndDateUtc).HasColumnType("date");
        builder.Property(x => x.NextExpectedBillDateUtc).HasColumnType("date");
        builder.Property(x => x.AmountTolerance).HasPrecision(18, 2);
        builder.Property(x => x.ContractReference).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.SafeFailureSummary).HasMaxLength(1000);
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasPrecision(3);
        builder.Property(x => x.UpdatedUtc).HasPrecision(3);
        builder.Property(x => x.DecidedUtc).HasPrecision(3);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceFingerprint }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceEmailMessageSnapshotId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEmailAttachmentSnapshotId });
        builder.HasIndex(x => new { x.CompanyId, x.AcceptedSubscriptionId });
        builder.HasOne(x => x.SourceEmailMessageSnapshot).WithMany().HasForeignKey(x => x.SourceEmailMessageSnapshotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceEmailAttachmentSnapshot).WithMany().HasForeignKey(x => x.SourceEmailAttachmentSnapshotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.MatchedCounterparty).WithMany().HasForeignKey(x => x.MatchedCounterpartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcceptedSubscription).WithMany().HasForeignKey(x => x.AcceptedSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
