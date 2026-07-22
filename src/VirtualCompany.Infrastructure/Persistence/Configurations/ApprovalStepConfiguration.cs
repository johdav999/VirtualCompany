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
internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("approval_steps");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApprovalId).IsRequired();
        builder.Property(x => x.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(x => x.ApproverType)
            .HasColumnName("approver_type")
            .HasConversion(value => value.ToStorageValue(), value => ApprovalStepApproverTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ApproverRef)
            .HasColumnName("approver_ref")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => ApprovalStepStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(2000);

        builder.HasIndex(x => new { x.ApprovalId, x.SequenceNo }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}

