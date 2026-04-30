using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MailboxConnectionPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_mailbox_connection_and_ingestion_run_tables()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var mailboxColumns = await ReadColumnsAsync(connection, "mailbox_connections");
        Assert.Contains("company_id", mailboxColumns);
        Assert.Contains("user_id", mailboxColumns);
        Assert.Contains("provider", mailboxColumns);
        Assert.Contains("status", mailboxColumns);
        Assert.Contains("encrypted_access_token", mailboxColumns);
        Assert.Contains("encrypted_refresh_token", mailboxColumns);
        Assert.Contains("configured_folders_json", mailboxColumns);
        Assert.Contains("granted_scopes_json", mailboxColumns);

        var runColumns = await ReadColumnsAsync(connection, "email_ingestion_runs");
        Assert.Contains("company_id", runColumns);
        Assert.Contains("mailbox_connection_id", runColumns);
        Assert.Contains("triggered_by_user_id", runColumns);
        Assert.Contains("started_at", runColumns);
        Assert.Contains("completed_at", runColumns);
        Assert.Contains("scanned_message_count", runColumns);
        Assert.Contains("detected_candidate_count", runColumns);
        Assert.Contains("non_candidate_message_count", runColumns);
        Assert.Contains("candidate_attachment_snapshot_count", runColumns);
        Assert.Contains("deduplicated_attachment_count", runColumns);
        Assert.Contains("failure_details", runColumns);

        var messageSnapshotColumns = await ReadColumnsAsync(connection, "email_message_snapshots");
        Assert.Contains("external_message_id", messageSnapshotColumns);
        Assert.Contains("source_type", messageSnapshotColumns);
        Assert.Contains("untrusted_body_text", messageSnapshotColumns);
        Assert.Contains("matched_rules_json", messageSnapshotColumns);
        Assert.Contains("candidate_decision", messageSnapshotColumns);
        Assert.Contains("sender_domain", messageSnapshotColumns);

        var attachmentSnapshotColumns = await ReadColumnsAsync(connection, "email_attachment_snapshots");
        Assert.Contains("external_attachment_id", attachmentSnapshotColumns);
        Assert.Contains("content_hash", attachmentSnapshotColumns);
        Assert.Contains("mime_type", attachmentSnapshotColumns);
        Assert.Contains("untrusted_extracted_text", attachmentSnapshotColumns);
        Assert.Contains("is_duplicate_by_hash", attachmentSnapshotColumns);
        Assert.Contains("canonical_attachment_snapshot_id", attachmentSnapshotColumns);

        var mailboxIndexes = await ReadIndexesAsync(connection, "mailbox_connections");
        Assert.Contains("IX_mailbox_connections_company_id_user_id", mailboxIndexes);
        Assert.Contains("IX_mailbox_connections_company_id_provider_email_address", mailboxIndexes);

        var runIndexes = await ReadIndexesAsync(connection, "email_ingestion_runs");
        Assert.Contains("IX_email_ingestion_runs_company_id_mailbox_connection_id_started_at", runIndexes);
        Assert.Contains("IX_email_ingestion_runs_company_id_provider_started_at", runIndexes);

        var messageSnapshotIndexes = await ReadIndexesAsync(connection, "email_message_snapshots");
        Assert.Contains("IX_email_message_snapshots_company_id_mailbox_connection_id_external_message_id", messageSnapshotIndexes);

        var attachmentSnapshotIndexes = await ReadIndexesAsync(connection, "email_attachment_snapshots");
        Assert.Contains("IX_email_attachment_snapshots_company_id_email_message_snapshot_id_external_attachment_id", attachmentSnapshotIndexes);
        Assert.Contains("IX_email_attachment_snapshots_company_id_content_hash_is_duplicate_by_hash", attachmentSnapshotIndexes);
    }

    [Fact]
    public async Task Mailbox_connection_round_trips_encrypted_tokens_and_folder_configuration()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var encryption = CreateEncryptionService();
        var accessToken = "access-token-value";
        var refreshToken = "refresh-token-value";

        await using (var dbContext = CreateContext(connection))
        {
            await dbContext.Database.EnsureCreatedAsync();
            SeedTenantAndUser(dbContext, companyId, userId);

            var mailboxConnection = new MailboxConnection(
                Guid.NewGuid(),
                companyId,
                userId,
                MailboxProvider.Gmail,
                "Bills@Example.com",
                "Bills Inbox",
                new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc));
            mailboxConnection.StoreEncryptedCredentials(
                encryption.Encrypt(companyId, "mailbox:gmail:access_token", accessToken),
                encryption.Encrypt(companyId, "mailbox:gmail:refresh_token", refreshToken),
                new DateTime(2026, 4, 26, 9, 0, 0, DateTimeKind.Utc),
                ["gmail.readonly", "gmail.attachments"]);
            mailboxConnection.ConfigureFolders(
            [
                new MailboxFolderSelection("Label_123", "Invoices"),
                new MailboxFolderSelection("Label_456", "Receipts")
            ]);

            dbContext.MailboxConnections.Add(mailboxConnection);
            await dbContext.SaveChangesAsync();
        }

        var storedAccessToken = await ReadScalarAsync(connection, "SELECT encrypted_access_token FROM mailbox_connections LIMIT 1;");
        var storedRefreshToken = await ReadScalarAsync(connection, "SELECT encrypted_refresh_token FROM mailbox_connections LIMIT 1;");
        Assert.NotEqual(accessToken, storedAccessToken);
        Assert.NotEqual(refreshToken, storedRefreshToken);
        Assert.Equal(accessToken, encryption.Decrypt(companyId, "mailbox:gmail:access_token", storedAccessToken));
        Assert.Equal(refreshToken, encryption.Decrypt(companyId, "mailbox:gmail:refresh_token", storedRefreshToken));

        await using var readContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var loaded = await readContext.MailboxConnections.SingleAsync();
        Assert.Equal("bills@example.com", loaded.EmailAddress);
        Assert.Equal(MailboxProvider.Gmail, loaded.Provider);
        Assert.Equal(MailboxConnectionStatus.Pending, loaded.Status);
        Assert.Equal(2, loaded.GrantedScopes.Count);
        Assert.Equal(2, loaded.ConfiguredFolders.Count);
        Assert.Contains(loaded.ConfiguredFolders, folder => folder.ProviderFolderId == "Label_123" && folder.DisplayName == "Invoices");
    }

    [Fact]
    public async Task Email_ingestion_run_persists_audit_fields_without_payment_side_effects()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var mailboxConnectionId = Guid.NewGuid();

        await using (var dbContext = CreateContext(connection))
        {
            await dbContext.Database.EnsureCreatedAsync();
            SeedTenantAndUser(dbContext, companyId, userId);
            dbContext.MailboxConnections.Add(new MailboxConnection(
                mailboxConnectionId,
                companyId,
                userId,
                MailboxProvider.Microsoft365,
                "ap@example.com"));

            var run = new EmailIngestionRun(
                Guid.NewGuid(),
                companyId,
                mailboxConnectionId,
                userId,
                MailboxProvider.Microsoft365,
                new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 27, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc));
            run.Complete(new DateTime(2026, 4, 26, 8, 1, 0, DateTimeKind.Utc), 12, 3, 9, 2, 1);
            dbContext.EmailIngestionRuns.Add(run);

            await dbContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var loaded = await readContext.EmailIngestionRuns.SingleAsync();
        Assert.Equal(companyId, loaded.CompanyId);
        Assert.Equal(mailboxConnectionId, loaded.MailboxConnectionId);
        Assert.Equal(userId, loaded.TriggeredByUserId);
        Assert.Equal(MailboxProvider.Microsoft365, loaded.Provider);
        Assert.Equal(12, loaded.ScannedMessageCount);
        Assert.Equal(3, loaded.DetectedCandidateCount);
        Assert.Equal(9, loaded.NonCandidateMessageCount);
        Assert.Equal(2, loaded.CandidateAttachmentSnapshotCount);
        Assert.Equal(1, loaded.DeduplicatedAttachmentCount);
        Assert.Null(loaded.FailureDetails);
        Assert.Equal(0, await readContext.Payments.CountAsync());
        Assert.Equal(0, await readContext.ApprovalRequests.CountAsync());
    }

    [Fact]
    public async Task Email_snapshots_round_trip_minimal_untrusted_bill_candidate_data()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var mailboxConnectionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var dbContext = CreateContext(connection))
        {
            await dbContext.Database.EnsureCreatedAsync();
            SeedTenantAndUser(dbContext, companyId, userId);
            dbContext.MailboxConnections.Add(new MailboxConnection(mailboxConnectionId, companyId, userId, MailboxProvider.Gmail, "ap@example.com"));
            dbContext.EmailIngestionRuns.Add(new EmailIngestionRun(runId, companyId, mailboxConnectionId, userId, MailboxProvider.Gmail, DateTime.UtcNow));
            var message = new EmailMessageSnapshot(
                Guid.NewGuid(),
                companyId,
                mailboxConnectionId,
                runId,
                "external-message-1",
                "billing@supplier.example",
                "Supplier Billing",
                "Invoice attached",
                DateTime.UtcNow,
                "INBOX",
                "Invoices",
                "provider-body-ref",
                null,
                BillSourceType.PdfAttachment,
                EmailCandidateDecision.Candidate,
                [BillDetectionRuleMatch.SenderMatch, BillDetectionRuleMatch.AttachmentPresent],
                "Matched deterministic bill detection rules: sender, supported_attachment.");
            message.Attachments.Add(new EmailAttachmentSnapshot(Guid.NewGuid(), companyId, message.Id, "att-1", "invoice.pdf", "application/pdf", 1000, "hash", "provider-ref", BillSourceType.PdfAttachment, "Invoice text", true, Guid.NewGuid()));
            dbContext.EmailMessageSnapshots.Add(message);
            await dbContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var loaded = await readContext.EmailMessageSnapshots.Include(x => x.Attachments).SingleAsync();
        Assert.Equal("external-message-1", loaded.ExternalMessageId);
        Assert.Equal(BillSourceType.PdfAttachment, loaded.SourceType);
        Assert.Equal(EmailCandidateDecision.Candidate, loaded.CandidateDecision);
        Assert.Equal("supplier.example", loaded.SenderDomain);
        Assert.Null(loaded.UntrustedBodyText);
        var attachment = loaded.Attachments.Single();
        Assert.Equal("Invoice text", attachment.UntrustedExtractedText);
        Assert.True(attachment.IsDuplicateByHash);
        Assert.NotNull(attachment.CanonicalAttachmentSnapshotId);
    }

    [Fact]
    public async Task Query_filters_keep_mailbox_records_tenant_scoped()
    {
        await using var connection = await OpenConnectionAsync();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var dbContext = CreateContext(connection))
        {
            await dbContext.Database.EnsureCreatedAsync();
            SeedTenantAndUser(dbContext, companyAId, userId);
            dbContext.Companies.Add(new Company(companyBId, "Mailbox Tenant B"));
            dbContext.MailboxConnections.AddRange(
                new MailboxConnection(Guid.NewGuid(), companyAId, userId, MailboxProvider.Gmail, "a@example.com"),
                new MailboxConnection(Guid.NewGuid(), companyBId, userId, MailboxProvider.Gmail, "b@example.com"));
            await dbContext.SaveChangesAsync();
        }

        await using var tenantAContext = CreateContext(connection, new TestCompanyContextAccessor(companyAId, userId));
        await using var tenantBContext = CreateContext(connection, new TestCompanyContextAccessor(companyBId, userId));

        Assert.Equal("a@example.com", (await tenantAContext.MailboxConnections.SingleAsync()).EmailAddress);
        Assert.Equal("b@example.com", (await tenantBContext.MailboxConnections.SingleAsync()).EmailAddress);
    }

    [Fact]
    public void Tenant_scoped_encryption_rejects_cross_tenant_decryption()
    {
        var encryption = CreateEncryptionService();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var ciphertext = encryption.Encrypt(companyAId, "mailbox:gmail:access_token", "tenant-secret");

        Assert.Equal("tenant-secret", encryption.Decrypt(companyAId, "mailbox:gmail:access_token", ciphertext));
        Assert.ThrowsAny<Exception>(() => encryption.Decrypt(companyBId, "mailbox:gmail:access_token", ciphertext));
    }

    private static void SeedTenantAndUser(VirtualCompanyDbContext dbContext, Guid companyId, Guid userId)
    {
        dbContext.Companies.Add(new Company(companyId, "Mailbox Persistence Company"));
        dbContext.Users.Add(new User(userId, $"user-{userId:N}@example.com", "Mailbox User", "test", userId.ToString("N")));
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<string> ReadScalarAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<string>(value);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor? accessor = null) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            accessor);

    private static IFieldEncryptionService CreateEncryptionService() =>
        new DataProtectionFieldEncryptionService(new EphemeralDataProtectionProvider());

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId, Guid userId)
        {
            CompanyId = companyId;
            UserId = userId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => CompanyId.HasValue && UserId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
