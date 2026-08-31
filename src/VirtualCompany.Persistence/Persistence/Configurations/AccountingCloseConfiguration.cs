using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingCloseTemplateConfiguration : IEntityTypeConfiguration<AccountingCloseTemplate>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTemplate> b)
    {
        b.ToTable("accounting_close_templates"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(64).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ActiveVersion).WithMany().HasForeignKey(x => x.ActiveVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTemplateVersionConfiguration : IEntityTypeConfiguration<AccountingCloseTemplateVersion>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTemplateVersion> b)
    {
        b.ToTable("accounting_close_template_versions"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.MaterialityAmount).HasColumnType("decimal(19,4)");
        b.Property(x => x.MaterialityPercentage).HasColumnType("decimal(9,4)"); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.TemplateId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.TemplateId, x.Status });
        b.HasOne(x => x.Template).WithMany(x => x.Versions).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseTemplateSectionConfiguration : IEntityTypeConfiguration<AccountingCloseTemplateSection>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTemplateSection> b)
    {
        b.ToTable("accounting_close_template_sections"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(64).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.Sequence });
        b.HasOne(x => x.TemplateVersion).WithMany(x => x.Sections).HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseTaskDefinitionConfiguration : IEntityTypeConfiguration<AccountingCloseTaskDefinition>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskDefinition> b)
    {
        b.ToTable("accounting_close_task_definitions"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(64).IsRequired(); b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.DefaultOwnerRole).HasMaxLength(64);
        b.Property(x => x.SignOffRole).HasMaxLength(64); b.Property(x => x.MaterialityAmount).HasColumnType("decimal(19,4)");
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.Sequence });
        b.HasOne(x => x.TemplateVersion).WithMany(x => x.TaskDefinitions).HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTaskDefinitionDependencyConfiguration : IEntityTypeConfiguration<AccountingCloseTaskDefinitionDependency>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskDefinitionDependency> b)
    {
        b.ToTable("accounting_close_task_definition_dependencies"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.TemplateVersionId, x.PredecessorTaskDefinitionId, x.DependentTaskDefinitionId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DependentTaskDefinitionId });
        b.HasOne(x => x.TemplateVersion).WithMany(x => x.Dependencies).HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingCloseTaskDefinition>().WithMany().HasForeignKey(x => x.PredecessorTaskDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseTaskDefinition>().WithMany().HasForeignKey(x => x.DependentTaskDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseEvidenceRequirementConfiguration : IEntityTypeConfiguration<AccountingCloseEvidenceRequirement>
{
    public void Configure(EntityTypeBuilder<AccountingCloseEvidenceRequirement> b)
    {
        b.ToTable("accounting_close_evidence_requirements"); b.HasKey(x => x.Id);
        b.Property(x => x.EvidenceType).HasMaxLength(64).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.TaskDefinitionId, x.EvidenceType }).IsUnique();
        b.HasOne(x => x.TaskDefinition).WithMany(x => x.EvidenceRequirements).HasForeignKey(x => x.TaskDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseTemplateHistoryConfiguration : IEntityTypeConfiguration<AccountingCloseTemplateHistory>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTemplateHistory> b)
    {
        b.ToTable("accounting_close_template_history"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(32).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.CompanyId, x.TemplateId, x.OccurredUtc });
        b.HasOne(x => x.Template).WithMany(x => x.History).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingCloseTemplateVersion>().WithMany().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseInstanceConfiguration : IEntityTypeConfiguration<AccountingCloseInstance>
{
    public void Configure(EntityTypeBuilder<AccountingCloseInstance> b)
    {
        b.ToTable("accounting_close_instances"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.StartIdempotencyKey).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.StartIdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.TemplateVersionId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TemplateVersion).WithMany().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTaskConfiguration : IEntityTypeConfiguration<AccountingCloseTask>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTask> b)
    {
        b.ToTable("accounting_close_tasks"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(64).IsRequired(); b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.OwnerRole).HasMaxLength(64); b.Property(x => x.SignOffRole).HasMaxLength(64);
        b.Property(x => x.MaterialityAmount).HasColumnType("decimal(19,4)"); b.Property(x => x.ReportedAmount).HasColumnType("decimal(19,4)");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Status, x.DueUtc });
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.WorkTaskId }).IsUnique();
        b.HasOne(x => x.CloseInstance).WithMany(x => x.Tasks).HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingCloseTaskDefinition>().WithMany().HasForeignKey(x => x.TaskDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseTemplateSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WorkTask).WithMany().HasForeignKey(x => x.WorkTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTaskDependencyConfiguration : IEntityTypeConfiguration<AccountingCloseTaskDependency>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskDependency> b)
    {
        b.ToTable("accounting_close_task_dependencies"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.PredecessorTaskId, x.DependentTaskId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DependentTaskId });
        b.HasOne(x => x.CloseInstance).WithMany(x => x.Dependencies).HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingCloseTask>().WithMany().HasForeignKey(x => x.PredecessorTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseTask>().WithMany().HasForeignKey(x => x.DependentTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTaskEvidenceConfiguration : IEntityTypeConfiguration<AccountingCloseTaskEvidence>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskEvidence> b)
    {
        b.ToTable("accounting_close_task_evidence"); b.HasKey(x => x.Id);
        b.Property(x => x.EvidenceType).HasMaxLength(64).IsRequired(); b.Property(x => x.DocumentTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(128);
        b.HasIndex(x => new { x.CompanyId, x.CloseTaskId, x.DocumentId, x.EvidenceType }).IsUnique();
        b.HasOne(x => x.CloseTask).WithMany(x => x.Evidence).HasForeignKey(x => x.CloseTaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseTaskNoteConfiguration : IEntityTypeConfiguration<AccountingCloseTaskNote>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskNote> b)
    {
        b.ToTable("accounting_close_task_notes"); b.HasKey(x => x.Id); b.Property(x => x.Note).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.CloseTaskId, x.CreatedUtc });
        b.HasOne(x => x.CloseTask).WithMany(x => x.Notes).HasForeignKey(x => x.CloseTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseTaskBlockerConfiguration : IEntityTypeConfiguration<AccountingCloseTaskBlocker>
{
    public void Configure(EntityTypeBuilder<AccountingCloseTaskBlocker> b)
    {
        b.ToTable("accounting_close_task_blockers"); b.HasKey(x => x.Id);
        b.Property(x => x.ReasonCode).HasMaxLength(100).IsRequired(); b.Property(x => x.Explanation).HasMaxLength(1000).IsRequired();
        b.Property(x => x.SafeNextAction).HasMaxLength(1000).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.CloseTaskId, x.Status });
        b.HasOne(x => x.CloseTask).WithMany(x => x.Blockers).HasForeignKey(x => x.CloseTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountingCloseStatusHistoryConfiguration : IEntityTypeConfiguration<AccountingCloseStatusHistory>
{
    public void Configure(EntityTypeBuilder<AccountingCloseStatusHistory> b)
    {
        b.ToTable("accounting_close_status_history"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(32).IsRequired(); b.Property(x => x.FromStatus).HasMaxLength(32);
        b.Property(x => x.ToStatus).HasMaxLength(32).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.CompanyId, x.CloseInstanceId, x.OccurredUtc });
        b.HasOne(x => x.CloseInstance).WithMany(x => x.History).HasForeignKey(x => x.CloseInstanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingCloseTask>().WithMany().HasForeignKey(x => x.CloseTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountingCloseOperationConfiguration : IEntityTypeConfiguration<AccountingCloseOperation>
{
    public void Configure(EntityTypeBuilder<AccountingCloseOperation> b)
    {
        b.ToTable("accounting_close_operations"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(32).IsRequired(); b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.TargetId, x.CreatedUtc });
    }
}
