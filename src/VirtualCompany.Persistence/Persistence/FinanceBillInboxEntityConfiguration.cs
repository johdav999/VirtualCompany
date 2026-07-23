using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceBillReviewStateEntityConfiguration : IEntityTypeConfiguration<FinanceBillReviewState>
{
    public void Configure(EntityTypeBuilder<FinanceBillReviewState> builder)
    {
        builder.ToTable("finance_bill_review_states");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProposalSummary).HasColumnName("proposal_summary").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.Navigation(x => x.Actions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_finance_bill_review_states_status",
            "status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')"));

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceBillReviewActionEntityConfiguration : IEntityTypeConfiguration<FinanceBillReviewAction>
{
    public void Configure(EntityTypeBuilder<FinanceBillReviewAction> builder)
    {
        builder.ToTable("finance_bill_review_actions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ReviewStateId).HasColumnName("review_state_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.ActorDisplayName).HasColumnName("actor_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PriorStatus).HasColumnName("prior_status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.NewStatus).HasColumnName("new_status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_bill_review_actions_prior_status", "prior_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
            t.HasCheckConstraint("CK_finance_bill_review_actions_new_status", "new_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId, x.OccurredUtc });
        builder.HasOne(x => x.ReviewState).WithMany(x => x.Actions).HasForeignKey(x => x.ReviewStateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BillApprovalProposalEntityConfiguration : IEntityTypeConfiguration<BillApprovalProposal>
{
    public void Configure(EntityTypeBuilder<BillApprovalProposal> builder)
    {
        builder.ToTable("bill_approval_proposals");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.ReviewStateId).HasColumnName("review_state_id").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at").IsRequired();
        builder.Property(x => x.PaymentExecutionRequested)
            .HasColumnName("payment_execution_requested")
            .HasDefaultValue(false)
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_bill_approval_proposals_no_payment_execution",
            "payment_execution_requested = 0"));

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId }).IsUnique();
        builder.HasOne(x => x.DetectedBill).WithMany().HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ReviewState).WithMany().HasForeignKey(x => x.ReviewStateId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupplierInvoicePaymentProposalEntityConfiguration : IEntityTypeConfiguration<SupplierInvoicePaymentProposal>
{
    public void Configure(EntityTypeBuilder<SupplierInvoicePaymentProposal> builder)
    {
        builder.ToTable("supplier_invoice_payment_proposals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at").IsRequired();
        builder.Property(x => x.PaymentReference).HasColumnName("payment_reference").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.Property(x => x.ExportMode)
            .HasColumnName("export_mode")
            .HasMaxLength(64)
            .HasDefaultValue(SupplierInvoicePaymentExportModes.RegisterPayment)
            .IsRequired();
        builder.Property(x => x.ExportStatus)
            .HasColumnName("export_status")
            .HasMaxLength(64)
            .HasDefaultValue(SupplierInvoicePaymentExportStatuses.NotExported)
            .IsRequired();
        builder.Property(x => x.ExportProviderKey).HasColumnName("export_provider_key").HasMaxLength(64);
        builder.Property(x => x.ExportConnectionId).HasColumnName("export_connection_id");
        builder.Property(x => x.ExportRequestedByUserId).HasColumnName("export_requested_by_user_id");
        builder.Property(x => x.ExportRequestedUtc).HasColumnName("export_requested_at");
        builder.Property(x => x.ExportedUtc).HasColumnName("exported_at");
        builder.Property(x => x.ExportResponseSummary).HasColumnName("export_response_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.ExportProviderMetadata)
            .HasColumnName("export_provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.AuditTrail)
            .HasColumnName("audit_trail_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_supplier_invoice_payment_proposals_status",
                "status IN ('draft', 'awaiting_approval', 'ready_for_payment', 'rejected', 'cancelled', 'exported')");
            t.HasCheckConstraint(
                "CK_supplier_invoice_payment_proposals_export_status",
                "export_status IN ('not_exported', 'export_requested', 'exported', 'failed', 'cancelled')");
            t.HasCheckConstraint(
                "CK_supplier_invoice_payment_proposals_export_mode",
                "export_mode IN ('register_payment', 'prepare_payment_file', 'manual_export')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.BillId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ExportMode, x.ExportStatus, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ExportStatus, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SupplierId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ApprovalRequest)
            .WithMany()
            .HasForeignKey(x => x.ApprovalRequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SupplierInvoiceSourceDocumentAttachmentEntityConfiguration : IEntityTypeConfiguration<SupplierInvoiceSourceDocumentAttachment>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceSourceDocumentAttachment> builder)
    {
        builder.ToTable("supplier_invoice_source_document_attachments");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(64)
            .HasDefaultValue(SupplierInvoiceSourceDocumentAttachmentStatuses.NotAttached)
            .IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        builder.Property(x => x.AttachedUtc).HasColumnName("attached_at");
        builder.Property(x => x.ResponseSummary).HasColumnName("response_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata)
            .HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AuditTrail)
            .HasColumnName("audit_trail_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_supplier_invoice_source_document_attachments_status",
            "status IN ('not_attached', 'attachment_requested', 'attached', 'failed', 'not_available')"));

        builder.HasIndex(x => new { x.CompanyId, x.BillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SupplierInvoiceDraftActionEntityConfiguration : IEntityTypeConfiguration<SupplierInvoiceDraftAction>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceDraftAction> builder)
    {
        builder.ToTable("supplier_invoice_draft_actions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(64)
            .HasDefaultValue(SupplierInvoiceDraftActionStatuses.Draft)
            .IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        builder.Property(x => x.UpdatedInProviderUtc).HasColumnName("updated_in_provider_at");
        builder.Property(x => x.BookedUtc).HasColumnName("booked_at");
        builder.Property(x => x.ResponseSummary).HasColumnName("response_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata)
            .HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AuditTrail)
            .HasColumnName("audit_trail_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_supplier_invoice_draft_actions_status",
            "status IN ('draft', 'update_pending', 'updated', 'bookkeeping_requested', 'booked', 'failed')"));

        builder.HasIndex(x => new { x.CompanyId, x.BillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SupplierInvoiceCorrectionActionEntityConfiguration : IEntityTypeConfiguration<SupplierInvoiceCorrectionAction>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceCorrectionAction> builder)
    {
        builder.ToTable("supplier_invoice_correction_actions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreditNoteBillId).HasColumnName("credit_note_bill_id");
        builder.Property(x => x.ProviderCreditNoteNumber).HasColumnName("provider_credit_note_number").HasMaxLength(128);
        builder.Property(x => x.ResponseSummary).HasColumnName("response_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata)
            .HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AuditTrail)
            .HasColumnName("audit_trail_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_supplier_invoice_correction_actions_action_type",
                "action_type IN ('cancellation', 'credit_note')");
            t.HasCheckConstraint(
                "CK_supplier_invoice_correction_actions_status",
                "status IN ('cancellation_requested', 'cancelled', 'cancellation_failed', 'credit_note_requested', 'credit_note_created', 'credit_note_failed')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.BillId, x.ActionType }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.CreditNoteBill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CreditNoteBillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ApprovalRequest)
            .WithMany()
            .HasForeignKey(x => x.ApprovalRequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SupplierInvoiceEnrichmentActionEntityConfiguration : IEntityTypeConfiguration<SupplierInvoiceEnrichmentAction>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceEnrichmentAction> builder)
    {
        builder.ToTable("supplier_invoice_enrichment_actions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(64)
            .HasDefaultValue(SupplierInvoiceEnrichmentActionStatuses.NotSuggested)
            .IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.SyncedUtc).HasColumnName("synced_at");
        builder.Property(x => x.ResponseSummary).HasColumnName("response_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.SuggestionPayload)
            .HasColumnName("suggestion_payload_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ReconciliationWarnings)
            .HasColumnName("reconciliation_warnings_json")
            .HasJsonArrayConversion()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata)
            .HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AuditTrail)
            .HasColumnName("audit_trail_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_supplier_invoice_enrichment_actions_status",
            "status IN ('not_suggested', 'awaiting_approval', 'approved', 'sync_requested', 'synced', 'failed', 'reconciliation_warning')"));

        builder.HasIndex(x => new { x.CompanyId, x.BillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ApprovalRequest)
            .WithMany()
            .HasForeignKey(x => x.ApprovalRequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
