using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class MailboxConnectionEntityConfiguration : IEntityTypeConfiguration<MailboxConnection>
{
    public void Configure(EntityTypeBuilder<MailboxConnection> builder)
    {
        builder.ToTable("mailbox_connections");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_mailbox_connections_provider", MailboxProviderValues.BuildCheckConstraintSql("provider"));
            t.HasCheckConstraint("CK_mailbox_connections_status", MailboxConnectionStatusValues.BuildCheckConstraintSql("status"));
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion(provider => provider.ToStorageValue(), value => MailboxProviderValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(status => status.ToStorageValue(), value => MailboxConnectionStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.EmailAddress).HasColumnName("email_address").HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        builder.Property(x => x.MailboxExternalId).HasColumnName("mailbox_external_id").HasMaxLength(256);
        builder.Property(x => x.EncryptedAccessToken).HasColumnName("encrypted_access_token");
        builder.Property(x => x.EncryptedRefreshToken).HasColumnName("encrypted_refresh_token");
        builder.Property(x => x.EncryptedCredentialEnvelope).HasColumnName("encrypted_credential_envelope");
        builder.Property(x => x.AccessTokenExpiresUtc).HasColumnName("access_token_expires_at");
        builder.Property(x => x.GrantedScopes)
            .HasColumnName("granted_scopes_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.ConfiguredFolders)
            .HasColumnName("configured_folders_json")
            .HasJsonConversion<List<MailboxFolderSelection>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata)
            .HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.LastSuccessfulScanUtc).HasColumnName("last_successful_scan_at");
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.UserId });
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.EmailAddress });
        builder.HasIndex(x => new { x.CompanyId, x.Status });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static PropertyBuilder<JsonObject> HasJsonObjectConversion(PropertyBuilder<JsonObject> propertyBuilder)
    {
        var converter = new ValueConverter<JsonObject, string>(
            value => SerializeJsonObject(value),
            value => DeserializeJsonObject(value));

        var comparer = new ValueComparer<JsonObject>(
            (left, right) => SerializeJsonObject(left) == SerializeJsonObject(right),
            value => StringComparer.Ordinal.GetHashCode(SerializeJsonObject(value)),
            value => DeserializeJsonObject(SerializeJsonObject(value)));

        propertyBuilder.HasColumnType("nvarchar(max)");
        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        return propertyBuilder;
    }

    private static string SerializeJsonObject(JsonObject? value) =>
        (value ?? new JsonObject()).ToJsonString(null);

    private static JsonObject DeserializeJsonObject(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new JsonObject()
            : JsonNode.Parse(value, null, default(JsonDocumentOptions)) as JsonObject ?? new JsonObject();
}

internal sealed class EmailIngestionRunEntityConfiguration : IEntityTypeConfiguration<EmailIngestionRun>
{
    public void Configure(EntityTypeBuilder<EmailIngestionRun> builder)
    {
        builder.ToTable("email_ingestion_runs");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_email_ingestion_runs_provider", MailboxProviderValues.BuildCheckConstraintSql("provider"));
            t.HasCheckConstraint("CK_email_ingestion_runs_scanned_message_count_nonnegative", "scanned_message_count >= 0");
            t.HasCheckConstraint("CK_email_ingestion_runs_detected_candidate_count_nonnegative", "detected_candidate_count >= 0");
            t.HasCheckConstraint("CK_email_ingestion_runs_non_candidate_message_count_nonnegative", "non_candidate_message_count >= 0");
            t.HasCheckConstraint("CK_email_ingestion_runs_candidate_attachment_snapshot_count_nonnegative", "candidate_attachment_snapshot_count >= 0");
            t.HasCheckConstraint("CK_email_ingestion_runs_deduplicated_attachment_count_nonnegative", "deduplicated_attachment_count >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id").IsRequired();
        builder.Property(x => x.TriggeredByUserId).HasColumnName("triggered_by_user_id").IsRequired();
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion(provider => provider.ToStorageValue(), value => MailboxProviderValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.ScanFromUtc).HasColumnName("scan_from_at");
        builder.Property(x => x.ScanToUtc).HasColumnName("scan_to_at");
        builder.Property(x => x.ScannedMessageCount).HasColumnName("scanned_message_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.DetectedCandidateCount).HasColumnName("detected_candidate_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.NonCandidateMessageCount).HasColumnName("non_candidate_message_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CandidateAttachmentSnapshotCount).HasColumnName("candidate_attachment_snapshot_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.DeduplicatedAttachmentCount).HasColumnName("deduplicated_attachment_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.FailureDetails).HasColumnName("failure_details").HasMaxLength(4000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.MailboxConnectionId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Provider, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggeredByUserId, x.StartedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.MailboxConnection).WithMany(x => x.IngestionRuns).HasForeignKey(x => x.MailboxConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.TriggeredByUser).WithMany().HasForeignKey(x => x.TriggeredByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmailMessageSnapshotEntityConfiguration : IEntityTypeConfiguration<EmailMessageSnapshot>
{
    public void Configure(EntityTypeBuilder<EmailMessageSnapshot> builder)
    {
        builder.ToTable("email_message_snapshots");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_email_message_snapshots_candidate_decision", EmailCandidateDecisionValues.BuildCheckConstraintSql("candidate_decision"));
            t.HasCheckConstraint("CK_email_message_snapshots_source_type", BillSourceTypeValues.BuildCheckConstraintSql("source_type"));
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id").IsRequired();
        builder.Property(x => x.EmailIngestionRunId).HasColumnName("email_ingestion_run_id").IsRequired();
        builder.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(512).IsRequired();
        builder.Property(x => x.FromAddress).HasColumnName("from_address").HasMaxLength(320);
        builder.Property(x => x.FromDisplayName).HasColumnName("from_display_name").HasMaxLength(256);
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500);
        builder.Property(x => x.SenderDomain).HasColumnName("sender_domain").HasMaxLength(256);
        builder.Property(x => x.ReceivedUtc).HasColumnName("received_at");
        builder.Property(x => x.FolderId).HasColumnName("folder_id").HasMaxLength(256);
        builder.Property(x => x.FolderDisplayName).HasColumnName("folder_display_name").HasMaxLength(256);
        builder.Property(x => x.BodyReference).HasColumnName("body_reference").HasMaxLength(512);
        builder.Property(x => x.UntrustedBodyText).HasColumnName("untrusted_body_text");
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(sourceType => sourceType.ToStorageValue(), value => BillSourceTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.CandidateDecision)
            .HasColumnName("candidate_decision")
            .HasConversion(decision => decision.ToStorageValue(), value => EmailCandidateDecisionValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(EmailCandidateDecision.Candidate)
            .IsRequired();
        builder.Property(x => x.MatchedRules)
            .HasColumnName("matched_rules_json")
            .HasJsonConversion<List<BillDetectionRuleMatch>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.ReasonSummary).HasColumnName("reason_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.MailboxConnectionId, x.ExternalMessageId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.EmailIngestionRunId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SenderDomain });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.MailboxConnection)
            .WithMany(x => x.MessageSnapshots)
            .HasForeignKey(x => x.MailboxConnectionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.EmailIngestionRun)
            .WithMany()
            .HasForeignKey(x => x.EmailIngestionRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailAttachmentSnapshotEntityConfiguration : IEntityTypeConfiguration<EmailAttachmentSnapshot>
{
    public void Configure(EntityTypeBuilder<EmailAttachmentSnapshot> builder)
    {
        builder.ToTable("email_attachment_snapshots");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_email_attachment_snapshots_source_type", BillSourceTypeValues.BuildCheckConstraintSql("source_type"));
            t.HasCheckConstraint("CK_email_attachment_snapshots_size_nonnegative", "size_bytes IS NULL OR size_bytes >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.EmailMessageSnapshotId).HasColumnName("email_message_snapshot_id").IsRequired();
        builder.Property(x => x.ExternalAttachmentId).HasColumnName("external_attachment_id").HasMaxLength(512).IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(512);
        builder.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(256);
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.StorageReference).HasColumnName("storage_reference").HasMaxLength(512);
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(sourceType => sourceType.ToStorageValue(), value => BillSourceTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.UntrustedExtractedText).HasColumnName("untrusted_extracted_text");
        builder.Property(x => x.IsDuplicateByHash).HasColumnName("is_duplicate_by_hash").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CanonicalAttachmentSnapshotId).HasColumnName("canonical_attachment_snapshot_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.EmailMessageSnapshotId });
        builder.HasIndex(x => new { x.CompanyId, x.EmailMessageSnapshotId, x.ExternalAttachmentId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ContentHash });
        builder.HasIndex(x => new { x.CompanyId, x.ContentHash, x.IsDuplicateByHash });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.EmailMessageSnapshot)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.EmailMessageSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
