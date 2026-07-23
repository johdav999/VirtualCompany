using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FortnoxOAuthStateEntityConfiguration : IEntityTypeConfiguration<FortnoxOAuthState>
{
    public void Configure(EntityTypeBuilder<FortnoxOAuthState> builder)
    {
        builder.ToTable("fortnox_oauth_states");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.StateHash).HasColumnName("state_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.ConsumedUtc).HasColumnName("consumed_at");
        builder.Property(x => x.CallbackReceivedUtc).HasColumnName("callback_received_at");
        builder.Property(x => x.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2048);
        builder.Property(x => x.CodeVerifierCiphertext).HasColumnName("code_verifier_ciphertext").HasColumnType("nvarchar(max)");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);

        builder.HasIndex(x => x.StateHash).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.UserId });
        builder.HasIndex(x => x.ExpiresUtc);
        builder.HasIndex(x => x.ConsumedUtc);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Connection)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FortnoxSyncHistoryEntityConfiguration : IEntityTypeConfiguration<FortnoxSyncHistory>
{
    public void Configure(EntityTypeBuilder<FortnoxSyncHistory> builder)
    {
        builder.ToTable("fortnox_sync_histories");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_fortnox_sync_histories_direction", FortnoxSyncDirections.BuildCheckConstraintSql("direction"));
            t.HasCheckConstraint("CK_fortnox_sync_histories_status", FortnoxSyncStatuses.BuildCheckConstraintSql("status"));
            t.HasCheckConstraint("CK_fortnox_sync_histories_records_processed_nonnegative", "records_processed >= 0");
            t.HasCheckConstraint("CK_fortnox_sync_histories_records_succeeded_nonnegative", "records_succeeded >= 0");
            t.HasCheckConstraint("CK_fortnox_sync_histories_records_failed_nonnegative", "records_failed >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FortnoxConnectionId).HasColumnName("fortnox_connection_id").IsRequired();
        builder.Property(x => x.SyncType).HasColumnName("sync_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.TriggeredByUserId).HasColumnName("triggered_by_user_id");
        builder.Property(x => x.RecordsProcessed).HasColumnName("records_processed").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.RecordsSucceeded).HasColumnName("records_succeeded").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.RecordsFailed).HasColumnName("records_failed").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.ErrorSummary).HasColumnName("error_summary").HasMaxLength(1000);
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.FortnoxConnectionId, x.StartedUtc });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FortnoxConnection)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FortnoxConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.TriggeredByUser).WithMany().HasForeignKey(x => x.TriggeredByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class FortnoxExternalReferenceEntityConfiguration : IEntityTypeConfiguration<FortnoxExternalReference>
{
    public void Configure(EntityTypeBuilder<FortnoxExternalReference> builder)
    {
        builder.ToTable("fortnox_external_references");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FortnoxConnectionId).HasColumnName("fortnox_connection_id");
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InternalEntityId).HasColumnName("internal_entity_id").IsRequired();
        builder.Property(x => x.ExternalEntityType).HasColumnName("external_entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExternalDisplayReference).HasColumnName("external_display_reference").HasMaxLength(128);
        builder.Property(x => x.LastSyncedUtc).HasColumnName("last_synced_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.EntityType, x.InternalEntityId, x.ExternalEntityType }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ExternalEntityType, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FortnoxConnectionId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FortnoxConnection)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FortnoxConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
