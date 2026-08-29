using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class TreasuryTransferConfiguration : IEntityTypeConfiguration<TreasuryTransfer>
{
    public void Configure(EntityTypeBuilder<TreasuryTransfer> b)
    {
        b.ToTable("finance_treasury_transfers", t => t.HasCheckConstraint("CK_finance_treasury_transfers_status", TreasuryMovementStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(200).IsRequired();
        b.Property(x => x.FromBankAccountId).HasColumnName("from_bank_account_id").IsRequired(); b.Property(x => x.ToBankAccountId).HasColumnName("to_bank_account_id").IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.FeeAmount).HasColumnName("fee_amount").HasPrecision(19, 4).IsRequired();
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsUnicode(false).IsRequired(); b.Property(x => x.FeeFinanceAccountId).HasColumnName("fee_finance_account_id");
        b.Property(x => x.MaterialityThreshold).HasColumnName("materiality_threshold").HasPrecision(19, 4).IsRequired(); b.Property(x => x.RequiresApproval).HasColumnName("requires_approval").IsRequired();
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.OutboundBankTransactionId).HasColumnName("outbound_bank_transaction_id"); b.Property(x => x.InboundBankTransactionId).HasColumnName("inbound_bank_transaction_id"); b.Property(x => x.CorrectionOfTransferId).HasColumnName("correction_of_transfer_id");
        Common(b);
        b.HasIndex(x => new { x.CompanyId, x.SourceIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc }); b.HasIndex(x => new { x.CompanyId, x.OutboundBankTransactionId }); b.HasIndex(x => new { x.CompanyId, x.InboundBankTransactionId });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FromBankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ToBankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FeeFinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BankTransaction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OutboundBankTransactionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BankTransaction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InboundBankTransactionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<TreasuryTransfer>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CorrectionOfTransferId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
    private static void Common(EntityTypeBuilder<TreasuryTransfer> b)
    {
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired(); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired(); b.Property(x => x.PostedUtc).HasColumnName("posted_at"); b.Property(x => x.ReversedUtc).HasColumnName("reversed_at");
    }
}

internal sealed class BankAdjustmentConfiguration : IEntityTypeConfiguration<BankAdjustment>
{
    public void Configure(EntityTypeBuilder<BankAdjustment> b)
    {
        b.ToTable("finance_bank_adjustments", t => { t.HasCheckConstraint("CK_finance_bank_adjustments_status", TreasuryMovementStatuses.BuildCheckConstraintSql("status")); t.HasCheckConstraint("CK_finance_bank_adjustments_kind", BankAdjustmentKinds.BuildCheckConstraintSql("adjustment_kind")); });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(200).IsRequired(); b.Property(x => x.AdjustmentKind).HasColumnName("adjustment_kind").HasMaxLength(32).IsRequired();
        b.Property(x => x.BankAccountId).HasColumnName("bank_account_id").IsRequired(); b.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired(); b.Property(x => x.CounterpartFinanceAccountId).HasColumnName("counterpart_finance_account_id").IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsUnicode(false).IsRequired(); b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        b.Property(x => x.MaterialityThreshold).HasColumnName("materiality_threshold").HasPrecision(19, 4).IsRequired(); b.Property(x => x.RequiresApproval).HasColumnName("requires_approval").IsRequired(); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.CorrectionOfAdjustmentId).HasColumnName("correction_of_adjustment_id"); Common(b);
        b.HasIndex(x => new { x.CompanyId, x.SourceIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BankTransaction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankTransactionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CounterpartFinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<BankAdjustment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CorrectionOfAdjustmentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
    private static void Common(EntityTypeBuilder<BankAdjustment> b) { b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired(); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired(); b.Property(x => x.PostedUtc).HasColumnName("posted_at"); b.Property(x => x.ReversedUtc).HasColumnName("reversed_at"); }
}

internal sealed class CardSettlementConfiguration : IEntityTypeConfiguration<CardSettlement>
{
    public void Configure(EntityTypeBuilder<CardSettlement> b) => SettlementConfiguration.Configure(b,
        "finance_card_settlements", "CK_finance_card_settlements_status", nameof(CardSettlement.ReceivableFinanceAccountId));
}

internal sealed class PayoutSettlementConfiguration : IEntityTypeConfiguration<PayoutSettlement>
{
    public void Configure(EntityTypeBuilder<PayoutSettlement> b) => SettlementConfiguration.Configure(b,
        "finance_payout_settlements", "CK_finance_payout_settlements_status", nameof(PayoutSettlement.PayoutClearingFinanceAccountId));
}

internal static class SettlementConfiguration
{
    public static void Configure<T>(EntityTypeBuilder<T> b, string table, string statusConstraint,
        string controlAccountProperty) where T : class, ICompanyOwnedEntity
    {
        b.ToTable(table, t => t.HasCheckConstraint(statusConstraint, TreasuryMovementStatuses.BuildCheckConstraintSql("status")));
        b.HasKey("Id"); b.HasAlternateKey("CompanyId", "Id");
        b.Property<Guid>("Id").HasColumnName("id"); b.Property<Guid>("CompanyId").HasColumnName("company_id").IsRequired(); b.Property<string>("SourceIdentity").HasColumnName("source_identity").HasMaxLength(200).IsRequired(); b.Property<string>("ProviderBatchReference").HasColumnName("provider_batch_reference").HasMaxLength(200).IsRequired();
        b.Property<Guid>("BankAccountId").HasColumnName("bank_account_id").IsRequired(); b.Property<Guid?>("BankTransactionId").HasColumnName("bank_transaction_id"); b.Property<Guid>(controlAccountProperty).HasColumnName("control_finance_account_id").IsRequired();
        b.Property<decimal>("GrossAmount").HasColumnName("gross_amount").HasPrecision(19, 4).IsRequired(); b.Property<decimal>("FeeAmount").HasColumnName("fee_amount").HasPrecision(19, 4).IsRequired(); b.Property<decimal>("NetAmount").HasColumnName("net_amount").HasPrecision(19, 4).IsRequired(); b.Property<string>("Currency").HasColumnName("currency").HasMaxLength(3).IsUnicode(false).IsRequired();
        b.Property<decimal>("MaterialityThreshold").HasColumnName("materiality_threshold").HasPrecision(19, 4).IsRequired(); b.Property<bool>("RequiresApproval").HasColumnName("requires_approval").IsRequired(); b.Property<Guid?>("ApprovalRequestId").HasColumnName("approval_request_id"); b.Property<Guid?>("CorrectionOfSettlementId").HasColumnName("correction_of_settlement_id");
        b.Property<string>("Status").HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property<string?>("ReasonCode").HasColumnName("reason_code").HasMaxLength(100); b.Property<long>("Version").HasColumnName("version").IsConcurrencyToken().IsRequired(); b.Property<Guid>("CreatedByUserId").HasColumnName("created_by_user_id").IsRequired(); b.Property<Guid>("UpdatedByUserId").HasColumnName("updated_by_user_id").IsRequired(); b.Property<DateTime>("CreatedUtc").HasColumnName("created_at").IsRequired(); b.Property<DateTime>("UpdatedUtc").HasColumnName("updated_at").IsRequired(); b.Property<DateTime?>("PostedUtc").HasColumnName("posted_at"); b.Property<DateTime?>("ReversedUtc").HasColumnName("reversed_at");
        b.HasIndex("CompanyId", "SourceIdentity").IsUnique(); b.HasIndex("CompanyId", "Status", "UpdatedUtc"); b.HasIndex("CompanyId", "BankTransactionId").IsUnique();
        b.HasOne<Company>("Company").WithMany().HasForeignKey("CompanyId").OnDelete(DeleteBehavior.Cascade); b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey("CompanyId", "BankAccountId").HasPrincipalKey(nameof(CompanyBankAccount.CompanyId), nameof(CompanyBankAccount.Id)).OnDelete(DeleteBehavior.NoAction); b.HasOne<BankTransaction>().WithMany().HasForeignKey("CompanyId", "BankTransactionId").HasPrincipalKey(nameof(BankTransaction.CompanyId), nameof(BankTransaction.Id)).OnDelete(DeleteBehavior.NoAction); b.HasOne<FinanceAccount>().WithMany().HasForeignKey("CompanyId", controlAccountProperty).HasPrincipalKey(nameof(FinanceAccount.CompanyId), nameof(FinanceAccount.Id)).OnDelete(DeleteBehavior.NoAction); b.HasOne<T>().WithMany().HasForeignKey("CompanyId", "CorrectionOfSettlementId").HasPrincipalKey("CompanyId", "Id").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TreasurySourceEvidenceConfiguration : IEntityTypeConfiguration<TreasurySourceEvidence>
{
    public void Configure(EntityTypeBuilder<TreasurySourceEvidence> b)
    { b.ToTable("finance_treasury_source_evidence", t => t.HasCheckConstraint("CK_finance_treasury_source_evidence_type", TreasurySourceTypes.BuildCheckConstraintSql("source_type"))); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired(); b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired(); b.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasMaxLength(64).IsRequired(); b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(300).IsRequired(); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsUnicode(false).IsRequired(); b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.EvidenceType }); b.HasIndex(x => new { x.CompanyId, x.ContentHash }); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); }
}

internal sealed class TreasurySourceEventConfiguration : IEntityTypeConfiguration<TreasurySourceEvent>
{
    public void Configure(EntityTypeBuilder<TreasurySourceEvent> b)
    { b.ToTable("finance_treasury_source_events", t => t.HasCheckConstraint("CK_finance_treasury_source_events_type", TreasurySourceTypes.BuildCheckConstraintSql("source_type"))); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired(); b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired(); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(80).IsRequired(); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.CreatedUtc }); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); }
}

internal sealed class TreasurySourceLedgerLinkConfiguration : IEntityTypeConfiguration<TreasurySourceLedgerLink>
{
    public void Configure(EntityTypeBuilder<TreasurySourceLedgerLink> b)
    { b.ToTable("finance_treasury_source_ledger_links", t => t.HasCheckConstraint("CK_finance_treasury_source_ledger_links_type", TreasurySourceTypes.BuildCheckConstraintSql("source_type"))); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired(); b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired(); b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired(); b.Property(x => x.LinkRole).HasColumnName("link_role").HasMaxLength(16).IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); b.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.LinkRole }).IsUnique(); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction); }
}
