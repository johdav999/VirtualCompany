using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class ApprovalTaskEntityConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.ToTable("approval_tasks");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_approval_tasks_target_type", ApprovalTargetTypeValues.BuildCheckConstraintSql("target_type"));
            t.HasCheckConstraint("CK_approval_tasks_status", ApprovalTaskStatusValues.BuildCheckConstraintSql("status"));
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TargetType)
            .HasColumnName("target_type")
            .HasConversion(value => value.ToStorageValue(), value => ApprovalTargetTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        builder.Property(x => x.AssigneeId).HasColumnName("assignee_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => ApprovalTaskStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.AssigneeId);
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => new { x.CompanyId, x.AssigneeId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.DueDate });
        // Current workflow keeps one tenant-scoped approval task per target so repeated backfills stay idempotent.
        builder.HasIndex(x => new { x.CompanyId, x.TargetType, x.TargetId }).IsUnique();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Assignee)
            .WithMany()
            .HasForeignKey(x => x.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
