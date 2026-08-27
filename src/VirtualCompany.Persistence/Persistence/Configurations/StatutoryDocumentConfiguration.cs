using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class StatutoryDocumentSeriesConfiguration : IEntityTypeConfiguration<StatutoryDocumentSeries>
{
    public void Configure(EntityTypeBuilder<StatutoryDocumentSeries> b)
    {
        b.ToTable("statutory_document_series"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(32); b.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32);
        b.Property(x => x.FiscalYearStart).HasColumnName("fiscal_year_start"); b.Property(x => x.FiscalYearEnd).HasColumnName("fiscal_year_end");
        b.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(32); b.Property(x => x.NumberWidth).HasColumnName("number_width");
        b.Property(x => x.NextNumber).HasColumnName("next_number"); b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Ignore(x => x.FiscalYearKey);
        b.HasIndex(x => new { x.CompanyId, x.Code, x.FiscalYearStart, x.FiscalYearEnd }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DocumentType, x.FiscalYearStart, x.FiscalYearEnd });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StatutoryDocumentNumberAllocationConfiguration : IEntityTypeConfiguration<StatutoryDocumentNumberAllocation>
{
    public void Configure(EntityTypeBuilder<StatutoryDocumentNumberAllocation> b)
    {
        b.ToTable("statutory_document_number_allocations"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SeriesId).HasColumnName("series_id");
        b.Property(x => x.FiscalYearKey).HasColumnName("fiscal_year_key").HasMaxLength(24); b.Property(x => x.Number).HasColumnName("number");
        b.Property(x => x.FormattedNumber).HasColumnName("formatted_number").HasMaxLength(64); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
        b.Property(x => x.GapReason).HasColumnName("gap_reason").HasMaxLength(512); b.Property(x => x.BusinessKey).HasColumnName("business_key").HasMaxLength(128);
        b.Property(x => x.SourceVersion).HasColumnName("source_version"); b.Property(x => x.IssuedDocumentId).HasColumnName("issued_document_id");
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.AllocatedUtc).HasColumnName("allocated_at");
        b.HasIndex(x => new { x.CompanyId, x.SeriesId, x.FiscalYearKey, x.Number }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.BusinessKey, x.SourceVersion }).IsUnique();
        b.HasOne(x => x.Series).WithMany().HasForeignKey(x => new { x.CompanyId, x.SeriesId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IssuedStatutoryDocumentConfiguration : IEntityTypeConfiguration<IssuedStatutoryDocument>
{
    public void Configure(EntityTypeBuilder<IssuedStatutoryDocument> b)
    {
        b.ToTable("issued_statutory_documents"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32); b.Property(x => x.Authority).HasColumnName("authority").HasMaxLength(16);
        b.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(64); b.Property(x => x.SourceRecordId).HasColumnName("source_record_id");
        b.Property(x => x.SourceVersion).HasColumnName("source_version"); b.Property(x => x.SeriesId).HasColumnName("series_id");
        b.Property(x => x.FiscalYearKey).HasColumnName("fiscal_year_key").HasMaxLength(24); b.Property(x => x.SequenceNumber).HasColumnName("sequence_number");
        b.Property(x => x.StatutoryProfileId).HasColumnName("statutory_profile_id"); b.Property(x => x.StatutoryProfileVersion).HasColumnName("statutory_profile_version");
        b.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(96); b.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32);
        b.Property(x => x.PolicyPackDefinitionHash).HasColumnName("policy_pack_definition_hash").HasMaxLength(64);
        b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(32768); b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64);
        b.Property(x => x.TaxFactsJson).HasColumnName("tax_facts_json").HasMaxLength(16384); b.Property(x => x.ApprovalIdsJson).HasColumnName("approval_ids_json").HasMaxLength(4096);
        b.Property(x => x.BusinessKey).HasColumnName("business_key").HasMaxLength(128); b.Property(x => x.OriginalIssuedDocumentId).HasColumnName("original_issued_document_id");
        b.Property(x => x.IssuedByUserId).HasColumnName("issued_by_user_id"); b.Property(x => x.IssuedUtc).HasColumnName("issued_at");
        b.Property(x => x.RenderedEvidenceReference).HasColumnName("rendered_evidence_reference").HasMaxLength(512);
        b.Property(x => x.DeliveryEvidenceReference).HasColumnName("delivery_evidence_reference").HasMaxLength(512);
        b.Property(x => x.EvidenceVersion).HasColumnName("evidence_version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SourceRecordId, x.SourceVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.BusinessKey, x.SourceVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DocumentNumber, x.Authority });
        b.HasIndex(x => new { x.CompanyId, x.SeriesId, x.FiscalYearKey, x.SequenceNumber }).IsUnique().HasFilter("[series_id] IS NOT NULL");
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
