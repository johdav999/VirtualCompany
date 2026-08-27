using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CustomerInvoiceRenderedArtifactConfiguration : IEntityTypeConfiguration<CustomerInvoiceRenderedArtifact>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceRenderedArtifact> b)
    {
        b.ToTable("customer_invoice_rendered_artifacts"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.IssuedDocumentId).HasColumnName("issued_document_id");
        b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64); b.Property(x => x.TemplateVersion).HasColumnName("template_version").HasMaxLength(64); b.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(16); b.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(100); b.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64); b.Property(x => x.ContentLength).HasColumnName("content_length"); b.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024); b.Property(x => x.GenerationAttempts).HasColumnName("generation_attempts"); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.RenderedUtc).HasColumnName("rendered_at");
        b.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.SnapshotHash, x.TemplateVersion, x.Locale }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<FinanceInvoice>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<IssuedStatutoryDocument>().WithMany().HasForeignKey(x => new { x.CompanyId, x.IssuedDocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerInvoiceEmailDeliveryConfiguration : IEntityTypeConfiguration<CustomerInvoiceEmailDelivery>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceEmailDelivery> b)
    {
        b.ToTable("customer_invoice_email_deliveries"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.ArtifactId).HasColumnName("artifact_id"); b.Property(x => x.ArtifactHash).HasColumnName("artifact_hash").HasMaxLength(64); b.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320); b.Property(x => x.RecipientSnapshotHash).HasColumnName("recipient_snapshot_hash").HasMaxLength(64); b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(300); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.RequestSource).HasColumnName("request_source").HasMaxLength(32); b.Property(x => x.FallbackReasonCode).HasColumnName("fallback_reason_code").HasMaxLength(100); b.Property(x => x.FallbackProviderKey).HasColumnName("fallback_provider_key").HasMaxLength(64); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.Attempts).HasColumnName("attempts"); b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.AcceptedUtc).HasColumnName("accepted_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.CreatedUtc }); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<FinanceInvoice>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerInvoiceRenderedArtifact>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ArtifactId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerInvoiceElectronicDeliveryConfiguration : IEntityTypeConfiguration<CustomerInvoiceElectronicDelivery>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceElectronicDelivery> b)
    {
        b.ToTable("customer_invoice_electronic_deliveries");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.IssuedDocumentId).HasColumnName("issued_document_id");
        b.Property(x => x.ArtifactId).HasColumnName("artifact_id"); b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64);
        b.Property(x => x.ArtifactHash).HasColumnName("artifact_hash").HasMaxLength(64); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        b.Property(x => x.Profile).HasColumnName("profile").HasMaxLength(128); b.Property(x => x.ProfileVersion).HasColumnName("profile_version").HasMaxLength(64);
        b.Property(x => x.ParticipantScheme).HasColumnName("participant_scheme").HasMaxLength(16); b.Property(x => x.ParticipantIdentifier).HasColumnName("participant_identifier").HasMaxLength(128);
        b.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32); b.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(64);
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(64); b.Property(x => x.SubmissionAttempts).HasColumnName("submission_attempts");
        b.Property(x => x.ReconciliationAttempts).HasColumnName("reconciliation_attempts"); b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256);
        b.Property(x => x.ProviderState).HasColumnName("provider_state").HasMaxLength(64); b.Property(x => x.DocumentHash).HasColumnName("document_hash").HasMaxLength(64);
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.AllowEmailFallback).HasColumnName("allow_email_fallback"); b.Property(x => x.FallbackRecipientEmail).HasColumnName("fallback_recipient_email").HasMaxLength(320);
        b.Property(x => x.FallbackEmailDeliveryId).HasColumnName("fallback_email_delivery_id"); b.Property(x => x.RequestReason).HasColumnName("request_reason").HasMaxLength(500);
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.SubmittedUtc).HasColumnName("submitted_at");
        b.Property(x => x.DeliveredUtc).HasColumnName("delivered_at"); b.Property(x => x.NextReconcileUtc).HasColumnName("next_reconcile_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.CreatedUtc });
        b.HasIndex(x => new { x.ProviderKey, x.ProviderReference }).IsUnique().HasFilter("[provider_reference] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.NextReconcileUtc });
        b.HasOne<FinanceInvoice>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<IssuedStatutoryDocument>().WithMany().HasForeignKey(x => new { x.CompanyId, x.IssuedDocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerInvoiceRenderedArtifact>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ArtifactId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerInvoiceEmailDelivery>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FallbackEmailDeliveryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerInvoiceElectronicDeliveryEventConfiguration : IEntityTypeConfiguration<CustomerInvoiceElectronicDeliveryEvent>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceElectronicDeliveryEvent> b)
    {
        b.ToTable("customer_invoice_electronic_delivery_events"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.DeliveryId).HasColumnName("delivery_id"); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        b.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(256); b.Property(x => x.Source).HasColumnName("source").HasMaxLength(32);
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(64); b.Property(x => x.ProviderState).HasColumnName("provider_state").HasMaxLength(64);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64);
        b.Property(x => x.OccurredUtc).HasColumnName("occurred_at");
        b.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.EventKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.DeliveryId, x.OccurredUtc });
        b.HasOne<CustomerInvoiceElectronicDelivery>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DeliveryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
