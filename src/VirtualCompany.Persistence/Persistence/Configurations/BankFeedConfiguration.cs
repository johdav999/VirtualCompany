using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BankFeedCheckpointConfiguration : IEntityTypeConfiguration<BankFeedCheckpoint>
{
    public void Configure(EntityTypeBuilder<BankFeedCheckpoint> b)
    {
        b.ToTable("bank_feed_checkpoints"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id");
        b.Property(x => x.DiscoveredAccountId).HasColumnName("discovered_account_id"); b.Property(x => x.AccountMappingId).HasColumnName("account_mapping_id");
        b.Property(x => x.AccountMappingVersion).HasColumnName("account_mapping_version"); b.Property(x => x.CompanyBankAccountId).HasColumnName("company_bank_account_id");
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired(); b.Property(x => x.StableProviderAccountId).HasColumnName("stable_provider_account_id").HasMaxLength(512).IsRequired();
        b.Property(x => x.ProviderAccountAccessReference).HasColumnName("provider_account_access_reference").HasMaxLength(256).IsRequired(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Phase).HasColumnName("phase").HasMaxLength(16).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(96); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.CoverageFrom).HasColumnName("coverage_from").HasColumnType("date"); b.Property(x => x.CoverageThrough).HasColumnName("coverage_through").HasColumnType("date");
        b.Property(x => x.WindowFrom).HasColumnName("window_from").HasColumnType("date"); b.Property(x => x.WindowTo).HasColumnName("window_to").HasColumnType("date");
        b.Property(x => x.RecoveryGapId).HasColumnName("recovery_gap_id"); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.SynchronizationRunId).HasColumnName("synchronization_run_id"); b.Property(x => x.ContinuationTokenEnvelope).HasColumnName("continuation_token_envelope").HasColumnType("nvarchar(max)");
        b.Property(x => x.ContinuationTokenHash).HasColumnName("continuation_token_hash").HasMaxLength(64); b.Property(x => x.PageNumber).HasColumnName("page_number"); b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.ImportedBookedCount).HasColumnName("imported_booked_count"); b.Property(x => x.ObservedPendingCount).HasColumnName("observed_pending_count");
        b.Property(x => x.LastAttemptUtc).HasColumnName("last_attempt_at"); b.Property(x => x.LastSuccessfulSyncUtc).HasColumnName("last_successful_sync_at"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.DiscoveredAccountId }).IsUnique(); b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId }); b.HasIndex(x => new { x.CompanyId, x.CompanyBankAccountId });
        b.HasOne<BankConnection>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankDiscoveredAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DiscoveredAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CompanyBankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankFeedRawSourceObjectConfiguration : IEntityTypeConfiguration<BankFeedRawSourceObject>
{
    public void Configure(EntityTypeBuilder<BankFeedRawSourceObject> b)
    {
        b.ToTable("bank_feed_raw_source_objects"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CheckpointId).HasColumnName("checkpoint_id");
        b.Property(x => x.SynchronizationRunId).HasColumnName("synchronization_run_id"); b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(256).IsRequired();
        b.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32).IsRequired(); b.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        b.Property(x => x.EncryptedPayload).HasColumnName("encrypted_payload").HasColumnType("nvarchar(max)"); b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.RetentionExpiresUtc).HasColumnName("retention_expires_at"); b.Property(x => x.PayloadPurgedUtc).HasColumnName("payload_purged_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.CheckpointId, x.SourceIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.RetentionExpiresUtc });
        b.HasOne<BankFeedCheckpoint>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CheckpointId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankFeedSourceTransactionConfiguration : IEntityTypeConfiguration<BankFeedSourceTransaction>
{
    public void Configure(EntityTypeBuilder<BankFeedSourceTransaction> b)
    {
        b.ToTable("bank_feed_source_transactions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CheckpointId).HasColumnName("checkpoint_id");
        b.Property(x => x.StableIdentity).HasColumnName("stable_identity").HasMaxLength(256).IsRequired(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(x => x.BookingDateUtc).HasColumnName("booking_date"); b.Property(x => x.ValueDateUtc).HasColumnName("value_date"); b.Property(x => x.TransactionDateUtc).HasColumnName("transaction_date");
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)"); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.ReferenceText).HasColumnName("reference_text").HasMaxLength(240).IsRequired(); b.Property(x => x.Counterparty).HasColumnName("counterparty").HasMaxLength(200).IsRequired();
        b.Property(x => x.ProviderTransactionReference).HasColumnName("provider_transaction_reference").HasMaxLength(256); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.RawSourceObjectId).HasColumnName("raw_source_object_id"); b.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.FirstSeenUtc).HasColumnName("first_seen_at"); b.Property(x => x.LastSeenUtc).HasColumnName("last_seen_at");
        b.HasIndex(x => new { x.CompanyId, x.CheckpointId, x.StableIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique().HasFilter("[bank_transaction_id] IS NOT NULL");
        b.HasOne<BankFeedCheckpoint>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CheckpointId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankFeedRawSourceObject>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RawSourceObjectId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankTransaction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankTransactionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankFeedBalanceSnapshotConfiguration : IEntityTypeConfiguration<BankFeedBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<BankFeedBalanceSnapshot> b)
    {
        b.ToTable("bank_feed_balance_snapshots"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CheckpointId).HasColumnName("checkpoint_id"); b.Property(x => x.RawSourceObjectId).HasColumnName("raw_source_object_id");
        b.Property(x => x.BalanceType).HasColumnName("balance_type").HasMaxLength(32).IsRequired(); b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)"); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.ObservedUtc).HasColumnName("observed_at"); b.Property(x => x.ReferenceDate).HasColumnName("reference_date").HasColumnType("date"); b.Property(x => x.LastCommittedTransactionIdentity).HasColumnName("last_committed_transaction_identity").HasMaxLength(256); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.CheckpointId, x.CreatedUtc });
        b.HasOne<BankFeedCheckpoint>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CheckpointId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankFeedRawSourceObject>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RawSourceObjectId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankFeedCursorObservationConfiguration : IEntityTypeConfiguration<BankFeedCursorObservation>
{
    public void Configure(EntityTypeBuilder<BankFeedCursorObservation> b)
    {
        b.ToTable("bank_feed_cursor_observations"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CheckpointId).HasColumnName("checkpoint_id"); b.Property(x => x.SynchronizationRunId).HasColumnName("synchronization_run_id");
        b.Property(x => x.Phase).HasColumnName("phase").HasMaxLength(16).IsRequired(); b.Property(x => x.CursorHash).HasColumnName("cursor_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.PageNumber).HasColumnName("page_number"); b.Property(x => x.ObservedUtc).HasColumnName("observed_at");
        b.HasIndex(x => new { x.CompanyId, x.CheckpointId, x.SynchronizationRunId, x.Phase, x.CursorHash }).IsUnique();
        b.HasOne<BankFeedCheckpoint>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CheckpointId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankFeedGapConfiguration : IEntityTypeConfiguration<BankFeedGap>
{
    public void Configure(EntityTypeBuilder<BankFeedGap> b)
    {
        b.ToTable("bank_feed_gaps"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CheckpointId).HasColumnName("checkpoint_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired(); b.Property(x => x.DateFrom).HasColumnName("date_from").HasColumnType("date"); b.Property(x => x.DateTo).HasColumnName("date_to").HasColumnType("date");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(96).IsRequired(); b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        b.Property(x => x.DetectedUtc).HasColumnName("detected_at"); b.Property(x => x.ResolvedUtc).HasColumnName("resolved_at"); b.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        b.HasIndex(x => new { x.CompanyId, x.CheckpointId, x.Status, x.DateFrom });
        b.HasOne<BankFeedCheckpoint>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CheckpointId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
