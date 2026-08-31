using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class ComplianceObligationDefinitionConfiguration : IEntityTypeConfiguration<ComplianceObligationDefinition>
{
    public void Configure(EntityTypeBuilder<ComplianceObligationDefinition> b){b.ToTable("compliance_obligation_definitions");b.HasKey(x=>x.Id);b.Property(x=>x.Key).HasMaxLength(100).IsRequired();b.Property(x=>x.Title).HasMaxLength(240).IsRequired();b.Property(x=>x.Jurisdiction).HasMaxLength(64).IsRequired();b.Property(x=>x.PolicyPackKey).HasMaxLength(100).IsRequired();b.Property(x=>x.PolicyPackVersion).HasMaxLength(64).IsRequired();b.Property(x=>x.PolicyPackDefinitionHash).HasMaxLength(128).IsRequired();b.Property(x=>x.DueDateRule).HasMaxLength(160).IsRequired();b.Property(x=>x.RequiredReport).HasMaxLength(240).IsRequired();b.Property(x=>x.RequiredEvidence).HasMaxLength(500).IsRequired();b.Property(x=>x.SubmissionMode).HasMaxLength(64).IsRequired();b.HasIndex(x=>new{x.CompanyId,x.Key,x.PolicyPackKey,x.PolicyPackVersion}).IsUnique();}
}

internal sealed class ComplianceObligationInstanceConfiguration : IEntityTypeConfiguration<ComplianceObligationInstance>
{
    public void Configure(EntityTypeBuilder<ComplianceObligationInstance> b)
    {
        b.ToTable("compliance_obligation_instances"); b.HasKey(x => x.Id);
        b.Property(x => x.DefinitionKey).HasMaxLength(100).IsRequired(); b.Property(x => x.Title).HasMaxLength(240).IsRequired();
        b.Property(x => x.Jurisdiction).HasMaxLength(64).IsRequired(); b.Property(x => x.PolicyPackKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.PolicyPackVersion).HasMaxLength(64).IsRequired(); b.Property(x => x.PolicyPackDefinitionHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.DueDateRule).HasMaxLength(160).IsRequired(); b.Property(x => x.Status).HasMaxLength(40).IsRequired();
        b.Property(x => x.SubmissionMode).HasMaxLength(64).IsRequired(); b.Property(x => x.SourceHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ExportReference).HasMaxLength(500); b.Property(x => x.ExportChecksum).HasMaxLength(128);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DefinitionKey, x.VatFilingPeriodId }).HasFilter("[CorrectionOfInstanceId] IS NULL").IsUnique().HasDatabaseName("IX_compliance_obligation_instances_origin");
        b.HasIndex(x => new { x.CompanyId, x.CorrectionOfInstanceId }).HasFilter("[CorrectionOfInstanceId] IS NOT NULL").IsUnique().HasDatabaseName("IX_compliance_obligation_instances_correction");
        b.HasIndex(x => new { x.CompanyId, x.DueDate, x.Status });
        b.HasOne<VatFilingPeriod>().WithMany().HasForeignKey(x => x.VatFilingPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VatReturn>().WithMany().HasForeignKey(x => x.VatReturnId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseTask>().WithMany().HasForeignKey(x => x.AccountingCloseTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ComplianceObligationHistoryConfiguration : IEntityTypeConfiguration<ComplianceObligationHistory>
{
    public void Configure(EntityTypeBuilder<ComplianceObligationHistory> b) { b.ToTable("compliance_obligation_history"); b.HasKey(x=>x.Id); b.Property(x=>x.Action).HasMaxLength(64).IsRequired(); b.Property(x=>x.FromStatus).HasMaxLength(40).IsRequired(); b.Property(x=>x.ToStatus).HasMaxLength(40).IsRequired(); b.Property(x=>x.SourceHash).HasMaxLength(128).IsRequired(); b.Property(x=>x.Reason).HasMaxLength(1000); b.HasIndex(x=>new{x.CompanyId,x.InstanceId,x.OccurredUtc}); b.HasOne(x=>x.Instance).WithMany(x=>x.History).HasForeignKey(x=>x.InstanceId).OnDelete(DeleteBehavior.Cascade); }
}
internal sealed class ComplianceSubmissionEvidenceConfiguration : IEntityTypeConfiguration<ComplianceSubmissionEvidence>
{
    public void Configure(EntityTypeBuilder<ComplianceSubmissionEvidence> b) { b.ToTable("compliance_submission_evidence"); b.HasKey(x=>x.Id); b.Property(x=>x.Reference).HasMaxLength(500).IsRequired(); b.Property(x=>x.ContentHash).HasMaxLength(128).IsRequired(); b.Property(x=>x.ReviewStatus).HasMaxLength(24).IsRequired(); b.HasIndex(x=>new{x.CompanyId,x.InstanceId,x.ContentHash}).IsUnique(); b.HasOne(x=>x.Instance).WithMany(x=>x.SubmissionEvidence).HasForeignKey(x=>x.InstanceId).OnDelete(DeleteBehavior.Cascade); }
}
internal sealed class ComplianceAuthorityAcknowledgementConfiguration : IEntityTypeConfiguration<ComplianceAuthorityAcknowledgement>
{
    public void Configure(EntityTypeBuilder<ComplianceAuthorityAcknowledgement> b) { b.ToTable("compliance_authority_acknowledgements"); b.HasKey(x=>x.Id); b.Property(x=>x.Kind).HasMaxLength(24).IsRequired(); b.Property(x=>x.Reference).HasMaxLength(500).IsRequired(); b.Property(x=>x.ContentHash).HasMaxLength(128).IsRequired(); b.HasIndex(x=>new{x.CompanyId,x.InstanceId,x.Kind,x.ContentHash}).IsUnique(); b.HasOne(x=>x.Instance).WithMany(x=>x.Acknowledgements).HasForeignKey(x=>x.InstanceId).OnDelete(DeleteBehavior.Cascade); }
}
internal sealed class ComplianceReminderConfiguration : IEntityTypeConfiguration<ComplianceReminder>
{
    public void Configure(EntityTypeBuilder<ComplianceReminder> b) { b.ToTable("compliance_reminders"); b.HasKey(x=>x.Id); b.Property(x=>x.Kind).HasMaxLength(32).IsRequired(); b.Property(x=>x.Status).HasMaxLength(24).IsRequired(); b.HasIndex(x=>new{x.CompanyId,x.InstanceId,x.Kind,x.EscalationLevel}).IsUnique(); b.HasOne(x=>x.Instance).WithMany(x=>x.Reminders).HasForeignKey(x=>x.InstanceId).OnDelete(DeleteBehavior.Cascade); }
}
internal sealed class ComplianceCommandReceiptConfiguration : IEntityTypeConfiguration<ComplianceCommandReceipt>
{
    public void Configure(EntityTypeBuilder<ComplianceCommandReceipt> b) { b.ToTable("compliance_command_receipts"); b.HasKey(x=>x.Id); b.Property(x=>x.IdempotencyKey).HasMaxLength(200).IsRequired(); b.Property(x=>x.PayloadHash).HasMaxLength(64).IsRequired(); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); }
}
