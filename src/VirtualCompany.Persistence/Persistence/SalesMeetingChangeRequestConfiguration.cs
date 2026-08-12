using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingChangeRequestConfiguration : IEntityTypeConfiguration<SalesMeetingChangeRequest>
{
    public void Configure(EntityTypeBuilder<SalesMeetingChangeRequest> builder)
    {
        builder.ToTable("sales_meeting_change_requests");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.InvitationId).HasColumnName("invitation_id").IsRequired();
        builder.Property(x => x.Operation).HasColumnName("operation").HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeOperationValues.Parse(x)).HasMaxLength(24).IsRequired();
        builder.Property(x => x.StartsUtc).HasColumnName("starts_at");
        builder.Property(x => x.EndsUtc).HasColumnName("ends_at");
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.CreateOnlineMeeting).HasColumnName("create_online_meeting");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeRequestStatusValues.Parse(x)).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(180).IsRequired();
        builder.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120);
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.CompanyId, x.InvitationId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).HasFilter("[approval_request_id] IS NOT NULL");
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_sales_meeting_change_requests_payload",
            "(operation = 'cancel') OR (starts_at IS NOT NULL AND ends_at IS NOT NULL AND ends_at > starts_at AND time_zone_id IS NOT NULL AND title IS NOT NULL AND description IS NOT NULL)"));
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Invitation).WithMany().HasForeignKey(nameof(SalesMeetingChangeRequest.CompanyId), nameof(SalesMeetingChangeRequest.InvitationId)).HasPrincipalKey(nameof(SalesMeetingInvitation.CompanyId), nameof(SalesMeetingInvitation.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
