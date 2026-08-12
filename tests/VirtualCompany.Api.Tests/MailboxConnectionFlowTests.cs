using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Application.Security;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MailboxConnectionFlowTests
{
    private static readonly IDataProtectionProvider TestDataProtectionProvider = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Start_oauth_returns_provider_authorization_url_with_minimal_scopes()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider);

        var result = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Gmail,
                new Uri("https://app.example.test/callback"),
                ConfiguredFolders: [new MailboxFolderSelection("INBOX", "Inbox")]),
            CancellationToken.None);

        Assert.Contains("scope=gmail.readonly", result.AuthorizationUrl.Query);
        Assert.DoesNotContain("gmail.modify", result.AuthorizationUrl.Query);
        Assert.DoesNotContain("gmail.send", result.AuthorizationUrl.Query);
    }

    [Fact]
    public async Task Start_oauth_protects_company_user_provider_and_return_uri_in_state_with_callback_metadata()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var returnUri = new Uri("https://app.example.test/finance/mailbox?tab=connections");
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var provider = new FakeProvider(MailboxProvider.Microsoft365);
        var service = CreateService(connection, companyId, userId, provider, stateProtector: stateProtector);

        var result = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Microsoft365,
                new Uri("https://app.example.test/api/mailbox-connections/microsoft365/callback"),
                returnUri,
                Purpose: MailboxPurpose.Support),
            CancellationToken.None);

        var query = System.Web.HttpUtility.ParseQueryString(result.AuthorizationUrl.Query);
        var state = stateProtector.Unprotect(query["state"]!);
        var redirectUri = query["redirect_uri"];

        Assert.Equal(companyId, state.CompanyId);
        Assert.Equal(userId, state.UserId);
        Assert.Equal(MailboxProvider.Microsoft365, state.Provider);
        Assert.Equal(MailboxPurpose.Support, state.Purpose);
        Assert.Equal(returnUri, state.ReturnUri);
        Assert.Equal(
            "https://app.example.test/api/mailbox-connections/microsoft365/callback",
            redirectUri);
        Assert.DoesNotContain($"/api/companies/{companyId:D}/", redirectUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_oauth_protects_company_user_provider_and_return_uri_in_state()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var returnUri = new Uri("https://app.example.test/finance/mailbox?tab=connections");
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var provider = new FakeProvider(MailboxProvider.Microsoft365);
        var service = CreateService(connection, companyId, userId, provider, stateProtector: stateProtector);

        var result = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Microsoft365,
                new Uri("https://app.example.test/api/mailbox-connections/microsoft365/callback"),
                returnUri),
            CancellationToken.None);

        var query = System.Web.HttpUtility.ParseQueryString(result.AuthorizationUrl.Query);
        var state = stateProtector.Unprotect(query["state"]!);
        var redirectUri = query["redirect_uri"];

        Assert.Equal(companyId, state.CompanyId);
        Assert.Equal(userId, state.UserId);
        Assert.Equal(MailboxProvider.Microsoft365, state.Provider);
        Assert.Equal(returnUri, state.ReturnUri);
        Assert.Equal(
            "https://app.example.test/api/mailbox-connections/microsoft365/callback",
            redirectUri);
        Assert.DoesNotContain($"/api/companies/{companyId:D}/", redirectUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_oauth_requires_resolved_tenant_user_before_state_is_issued()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var stateProtector = new ThrowingStateProtector();
        var service = CreateService(connection, companyId, userId, provider, contextAccessor: new TestCompanyContextAccessor(companyId, null), stateProtector: stateProtector);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Gmail,
                new Uri("https://app.example.test/api/mailbox-connections/gmail/callback")),
            CancellationToken.None));

        Assert.Equal(0, stateProtector.ProtectCallCount);
    }

    [Fact]
    public async Task Callback_persists_connected_connection_with_encrypted_tokens()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedCompanyAndUserAsync(connection, companyId, userId);
        var provider = new FakeProvider(MailboxProvider.Microsoft365);
        var service = CreateService(connection, companyId, userId, provider);
        var start = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Microsoft365,
                new Uri("https://app.example.test/callback")),
            CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(start.AuthorizationUrl.Query)["state"]!;

        var result = await service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(state, "oauth-code", new Uri("https://app.example.test/callback")),
            CancellationToken.None);

        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var stored = await dbContext.MailboxConnections.SingleAsync();
        Assert.Equal(result.MailboxConnectionId, stored.Id);
        Assert.Equal(MailboxConnectionStatus.Active, stored.Status);
        Assert.Null(stored.EncryptedAccessToken);
        Assert.Null(stored.EncryptedRefreshToken);
        Assert.NotNull(stored.ExternalAccountConnectionId);
        var external = await dbContext.ExternalAccountConnections.SingleAsync();
        Assert.NotEqual("access-token", external.EncryptedAccessToken);
        Assert.NotEqual("refresh-token", external.EncryptedRefreshToken);
        Assert.Equal(0, await dbContext.Payments.CountAsync());
        Assert.Equal(0, await dbContext.ApprovalRequests.CountAsync());
    }

    [Fact]
    public async Task Startup_refresh_restores_expiring_support_mailbox_credentials_without_interactive_authentication()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc);
        await SeedConnectedMailboxAsync(
            connection,
            companyId,
            userId,
            MailboxProvider.Gmail,
            purpose: MailboxPurpose.Support,
            accessTokenExpiresUtc: now.AddMinutes(-1));
        var provider = new FakeProvider(MailboxProvider.Gmail);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var refresher = new MailboxConnectionCredentialRefresher(
            dbContext,
            new FakeProviderRegistry(provider),
            CreateEncryption(),
            new MailboxOAuthAccessTokenLeaseService(
                dbContext, new FakeProviderRegistry(provider), CreateEncryption()),
            null!,
            new FakeTimeProvider(now),
            NullLogger<MailboxConnectionCredentialRefresher>.Instance);

        await refresher.RefreshExpiringConnectionsAsync(companyId, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.MailboxConnections.SingleAsync();
        Assert.Equal(1, provider.RefreshCallCount);
        Assert.Equal(MailboxPurpose.Support, stored.Purpose);
        Assert.Equal(MailboxConnectionStatus.Active, stored.Status);
        Assert.Null(stored.LastErrorSummary);
        var external = await dbContext.ExternalAccountConnections.SingleAsync();
        Assert.True(external.AccessTokenExpiresUtc > now);
    }

    [Fact]
    public async Task Startup_restore_marks_credentials_from_an_unavailable_key_ring_for_reconnection()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc);
        var unavailableEncryption = new DataProtectionFieldEncryptionService(new EphemeralDataProtectionProvider());
        await SeedConnectedMailboxAsync(
            connection,
            companyId,
            userId,
            MailboxProvider.Gmail,
            purpose: MailboxPurpose.Support,
            accessTokenExpiresUtc: now.AddHours(1),
            encryption: unavailableEncryption);
        var provider = new FakeProvider(MailboxProvider.Gmail);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var refresher = new MailboxConnectionCredentialRefresher(
            dbContext,
            new FakeProviderRegistry(provider),
            CreateEncryption(),
            new MailboxOAuthAccessTokenLeaseService(
                dbContext, new FakeProviderRegistry(provider), CreateEncryption()),
            null!,
            new FakeTimeProvider(now),
            NullLogger<MailboxConnectionCredentialRefresher>.Instance);

        await refresher.RefreshExpiringConnectionsAsync(companyId, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.MailboxConnections.SingleAsync();
        Assert.Equal(0, provider.RefreshCallCount);
        Assert.Equal(MailboxConnectionStatus.TokenExpired, stored.Status);
        Assert.Equal("Automatic mailbox authentication could not be restored. Reconnect this mailbox.", stored.LastErrorSummary);
    }

    [Fact]
    public async Task Callback_uses_protected_state_for_company_user_provider_and_return_uri()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedCompanyAndUserAsync(connection, companyId, userId);
        var returnUri = new Uri("https://app.example.test/finance/mailbox?tab=connections");
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider);
        var start = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                userId,
                MailboxProvider.Gmail,
                new Uri("https://app.example.test/api/mailbox-connections/gmail/callback"),
                returnUri),
            CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(start.AuthorizationUrl.Query)["state"]!;

        var result = await service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(
                state,
                "oauth-code",
                new Uri("https://app.example.test/api/mailbox-connections/gmail/callback"),
                MailboxProvider.Gmail),
            CancellationToken.None);

        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var stored = await dbContext.MailboxConnections.SingleAsync();
        Assert.Equal(companyId, stored.CompanyId);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(MailboxProvider.Gmail, stored.Provider);
        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(returnUri, result.ReturnUri);
        Assert.Equal(new Uri("https://app.example.test/api/mailbox-connections/gmail/callback"), provider.LastTokenExchangeRequest!.CallbackUri);
    }

    [Fact]
    public async Task Callback_rejects_provider_mismatch_before_token_exchange_and_persistence()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider);
        var start = await service.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(companyId, userId, MailboxProvider.Gmail, new Uri("https://app.example.test/callback")),
            CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(start.AuthorizationUrl.Query)["state"]!;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(state, "oauth-code", new Uri("https://app.example.test/callback"), MailboxProvider.Microsoft365),
            CancellationToken.None));

        Assert.Equal(0, provider.ExchangeCallCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.MailboxConnections.CountAsync());
    }

    [Fact]
    public async Task Callback_rejects_expired_state_before_token_exchange_and_persistence_for_replayed_state()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var expiredState = stateProtector.Protect(new MailboxOAuthState(
            companyId,
            userId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            new DateTime(2026, 4, 26, 7, 59, 0, DateTimeKind.Utc),
            new Uri("https://app.example.test/finance/mailbox")));
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider, stateProtector: stateProtector);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(expiredState, "oauth-code", new Uri("https://app.example.test/callback"), MailboxProvider.Gmail),
            CancellationToken.None));

        Assert.Equal(0, provider.ExchangeCallCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.MailboxConnections.CountAsync());
    }

    [Fact]
    public async Task Callback_rejects_cross_tenant_completion_before_token_exchange_and_persistence_for_active_context()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var protectedState = stateProtector.Protect(new MailboxOAuthState(
            companyId,
            userId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            new DateTime(2026, 4, 26, 8, 10, 0, DateTimeKind.Utc),
            new Uri("https://app.example.test/finance/mailbox")));
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(
            connection,
            companyId,
            userId,
            provider,
            stateProtector: stateProtector,
            contextAccessor: new TestCompanyContextAccessor(otherCompanyId, null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(protectedState, "oauth-code", new Uri("https://app.example.test/api/mailbox-connections/gmail/callback"), MailboxProvider.Gmail),
            CancellationToken.None));

        Assert.Equal(0, provider.ExchangeCallCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.MailboxConnections.CountAsync());
    }

    [Fact]
    public async Task Callback_rejects_expired_state_before_token_exchange_and_persistence()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var expiredState = stateProtector.Protect(new MailboxOAuthState(
            companyId,
            userId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            new DateTime(2026, 4, 26, 7, 59, 0, DateTimeKind.Utc),
            new Uri("https://app.example.test/finance/mailbox")));
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider, stateProtector: stateProtector);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(expiredState, "oauth-code", new Uri("https://app.example.test/callback"), MailboxProvider.Gmail),
            CancellationToken.None));

        Assert.Equal(0, provider.ExchangeCallCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.MailboxConnections.CountAsync());
    }

    [Fact]
    public async Task Callback_rejects_cross_tenant_completion_before_token_exchange_and_persistence()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());
        var protectedState = stateProtector.Protect(new MailboxOAuthState(
            companyId,
            userId,
            MailboxProvider.Gmail,
            [new MailboxFolderSelection("INBOX", "Inbox")],
            new DateTime(2026, 4, 26, 8, 10, 0, DateTimeKind.Utc),
            new Uri("https://app.example.test/finance/mailbox")));
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(
            connection,
            companyId,
            userId,
            provider,
            stateProtector: stateProtector,
            contextAccessor: new TestCompanyContextAccessor(otherCompanyId, null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteOAuthConnectionAsync(
            new CompleteMailboxOAuthConnectionCommand(protectedState, "oauth-code", new Uri("https://app.example.test/api/mailbox-connections/gmail/callback"), MailboxProvider.Gmail),
            CancellationToken.None));

        Assert.Equal(0, provider.ExchangeCallCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.MailboxConnections.CountAsync());
    }

    [Fact]
    public void State_protector_rejects_tampered_state_as_authentication_failure()
    {
        var stateProtector = new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider());

        Assert.Throws<UnauthorizedAccessException>(() =>
            stateProtector.Unprotect("tampered-state"));
    }

    [Fact]
    public async Task Manual_scan_is_user_scoped_uses_last_30_days_and_keyword_filtering()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary(
                    "1",
                    "Monthly invoice",
                    null,
                    null,
                    ["invoice.pdf"],
                    "billing@supplier.example",
                    null,
                    DateTime.UtcNow,
                    "INBOX",
                    "Invoices",
                    null,
                    [new MailboxAttachmentSummary("a1", "invoice.pdf", "application/pdf", 1000, UntrustedExtractedText: "Invoice amount due")]),
                new MailboxMessageSummary("2", "Hello", "No keyword", null, []),
                new MailboxMessageSummary("3", null, null, null, ["bankgiro.pdf"])
            ]
        };
        var service = CreateService(connection, companyId, userId, provider, nowUtc: new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc));
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId, await ReadMailboxConnectionIdAsync(connection)),
            CancellationToken.None);

        Assert.True(result.FailureDetails is null, result.FailureDetails);
        Assert.Equal(new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc), result.ScanFromUtc);
        Assert.Equal(new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc), result.ScanToUtc);
        Assert.Equal(3, result.ScannedMessageCount);
        Assert.Equal(2, result.DetectedCandidateCount);
        Assert.Equal(result.ScanFromUtc, provider.LastQuery!.FromUtc);
        Assert.Equal(result.ScanToUtc, provider.LastQuery.ToUtc);
        Assert.Single(provider.LastQuery.Folders);
        Assert.Equal("INBOX", provider.LastQuery.Folders.Single().ProviderFolderId);
        Assert.Equal(1, result.NonCandidateMessageCount);
        Assert.Equal(2, result.CandidateAttachmentSnapshotCount);
        Assert.Equal(0, result.DeduplicatedAttachmentCount);

        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Single(await dbContext.EmailIngestionRuns.ToListAsync());
        var snapshots = await dbContext.EmailMessageSnapshots.Include(x => x.Attachments).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(BillSourceType.PdfAttachment, snapshot.SourceType));
        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Attachments));
        Assert.Equal(0, await dbContext.Payments.CountAsync());
        Assert.Equal(0, await dbContext.ApprovalRequests.CountAsync());
    }

    [Fact]
    public async Task Manual_scan_can_start_for_current_users_active_connection_without_client_supplied_connection_id()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary("1", "Amount due", null, "Invoice number 42 amount due tomorrow", [], "billing@supplier.example")
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.MailboxConnectionId);
        Assert.Equal(1, result.ScannedMessageCount);
        Assert.Equal(1, result.DetectedCandidateCount);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task Manual_scan_persists_body_only_candidate_as_untrusted_body_text()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary("body-1", "Amount due", null, "Invoice number 42 amount due tomorrow", [], "billing@supplier.example")
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId),
            CancellationToken.None);

        Assert.Equal(1, result.DetectedCandidateCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var snapshot = await dbContext.EmailMessageSnapshots.Include(x => x.Attachments).SingleAsync();
        Assert.Equal(BillSourceType.EmailBodyOnly, snapshot.SourceType);
        Assert.Equal("Invoice number 42 amount due tomorrow", snapshot.UntrustedBodyText);
        Assert.Empty(snapshot.Attachments);
    }

    [Fact]
    public async Task Manual_scan_hydrates_weak_gmail_invoice_snippet_before_rejecting_body_only_message()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string messageId = "gmail-prosa-invoice";
        const string fullBody = """
            Hello,

            Please find our test supplier invoice details below.

            Supplier: Prosa Test Services AB
            Org.nr: 559123-4567
            Address: Testgatan 10, 111 22 Stockholm
            VAT No: SE559123456701

            Invoice number: VC-TEST-2026-001
            Invoice date: 2026-07-25
            Due date: 2026-08-24

            Description: Test subscription services for Virtual Company
            Amount excluding VAT: SEK 8,000.00
            VAT: SEK 2,000.00
            Total amount due: SEK 10,000.00

            Payment reference: VC2026001
            """;
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary(
                    messageId,
                    "Invoice Prosa Services",
                    "Hello, Please find our test supplier invoice details below.",
                    null,
                    [],
                    "johan@example.com",
                    null,
                    new DateTime(2026, 7, 25, 14, 41, 0, DateTimeKind.Utc),
                    "INBOX",
                    "Inbox")
            ],
            FullMessages = new Dictionary<string, MailboxInboundMessage>
            {
                [messageId] = new(
                    messageId,
                    null,
                    null,
                    "Invoice Prosa Services",
                    fullBody,
                    null,
                    new MailboxAddress("johan@example.com", "Johan Davidsson"),
                    [new MailboxAddress("finance@example.com", null)],
                    new DateTime(2026, 7, 25, 14, 41, 0, DateTimeKind.Utc),
                    new Dictionary<string, string>())
            }
        };
        var service = CreateService(connection, companyId, userId, provider, nowUtc: new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc));
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId),
            CancellationToken.None);

        Assert.Equal(1, provider.GetMessageCallCount);
        Assert.Equal(1, result.ScannedMessageCount);
        Assert.Equal(1, result.DetectedCandidateCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var snapshot = await dbContext.EmailMessageSnapshots.SingleAsync();
        Assert.Equal(BillSourceType.EmailBodyOnly, snapshot.SourceType);
        Assert.Contains("VC-TEST-2026-001", snapshot.UntrustedBodyText);
    }

    [Fact]
    public async Task Manual_scan_counts_non_candidates_without_creating_snapshots()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary("1", "Team update", null, "No billing content", [], "hello@example.com")
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(new TriggerManualMailboxScanCommand(companyId, userId), CancellationToken.None);

        Assert.Equal(1, result.ScannedMessageCount);
        Assert.Equal(0, result.DetectedCandidateCount);
        Assert.Equal(1, result.NonCandidateMessageCount);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(0, await dbContext.EmailMessageSnapshots.CountAsync());
        Assert.Equal(0, await dbContext.EmailAttachmentSnapshots.CountAsync());
    }

    [Fact]
    public async Task Manual_scan_marks_duplicate_candidate_attachments_by_company_hash()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary(
                    "1",
                    "Invoice one",
                    null,
                    null,
                    ["invoice-1.pdf"],
                    "billing@supplier.example",
                    Attachments: [new MailboxAttachmentSummary("a1", "invoice-1.pdf", "application/pdf", 1000, ContentHash: "same-sha256", UntrustedExtractedText: "Invoice amount due")]),
                new MailboxMessageSummary(
                    "2",
                    "Invoice two",
                    null,
                    null,
                    ["invoice-2.pdf"],
                    "billing@supplier.example",
                    Attachments: [new MailboxAttachmentSummary("a2", "invoice-2.pdf", "application/pdf", 1000, ContentHash: "same-sha256", UntrustedExtractedText: "Invoice amount due")])
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(new TriggerManualMailboxScanCommand(companyId, userId), CancellationToken.None);

        Assert.Equal(2, result.ScannedMessageCount);
        Assert.Equal(2, result.DetectedCandidateCount);
        Assert.Equal(2, result.CandidateAttachmentSnapshotCount);
        Assert.Equal(1, result.DeduplicatedAttachmentCount);

        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var attachments = await dbContext.EmailAttachmentSnapshots.OrderBy(x => x.CreatedUtc).ToListAsync();
        Assert.Equal(2, attachments.Count);
        Assert.Contains(attachments, x => !x.IsDuplicateByHash && x.CanonicalAttachmentSnapshotId is null);
        Assert.Contains(attachments, x => x.IsDuplicateByHash && x.CanonicalAttachmentSnapshotId == attachments.First(a => !a.IsDuplicateByHash).Id);
    }

    [Fact]
    public async Task Manual_scan_is_idempotent_for_same_provider_message_id()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary(
                    "same-message",
                    "Monthly invoice",
                    null,
                    null,
                    ["invoice.pdf"],
                    "billing@supplier.example",
                    null,
                    DateTime.UtcNow,
                    "INBOX",
                    "Invoices",
                    null,
                    [new MailboxAttachmentSummary("a1", "invoice.pdf", "application/pdf", 1000, UntrustedExtractedText: "Invoice amount due")])
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        await service.TriggerManualScanAsync(new TriggerManualMailboxScanCommand(companyId, userId), CancellationToken.None);
        var second = await service.TriggerManualScanAsync(new TriggerManualMailboxScanCommand(companyId, userId), CancellationToken.None);

        Assert.Equal(1, second.ScannedMessageCount);
        Assert.Equal(1, second.DetectedCandidateCount);
        Assert.Equal(0, second.CandidateAttachmentSnapshotCount);

        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        Assert.Equal(2, await dbContext.EmailIngestionRuns.CountAsync());
        Assert.Equal(1, await dbContext.EmailMessageSnapshots.CountAsync());
        Assert.Equal(1, await dbContext.EmailAttachmentSnapshots.CountAsync());
        Assert.Equal(0, await dbContext.Payments.CountAsync());
        Assert.Equal(0, await dbContext.ApprovalRequests.CountAsync());
    }

    [Fact]
    public async Task Status_query_returns_current_users_connection_and_latest_audit_summary()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail)
        {
            Messages =
            [
                new MailboxMessageSummary("1", "Faktura", null, "Faktura amount due due date", [], "billing@supplier.example")
            ]
        };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);
        await SeedConnectedMailboxAsync(connection, companyId, otherUserId, MailboxProvider.Microsoft365, "other@example.com");

        await service.TriggerManualScanAsync(new TriggerManualMailboxScanCommand(companyId, userId), CancellationToken.None);
        var status = await service.GetStatusAsync(new GetMailboxConnectionStatusQuery(companyId, userId), CancellationToken.None);

        Assert.True(status.IsConnected);
        Assert.Equal("gmail", status.Provider);
        Assert.Equal("ap@example.com", status.EmailAddress);
        Assert.NotNull(status.LastRun);
        Assert.Equal(1, status.LastRun!.DetectedCandidateCount);
    }

    [Fact]
    public async Task Manual_scan_is_rejected_when_current_user_has_no_connection()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail);
        var service = CreateService(connection, companyId, userId, provider);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Manual_scan_failure_still_persists_ingestion_run()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provider = new FakeProvider(MailboxProvider.Gmail) { ThrowOnList = true };
        var service = CreateService(connection, companyId, userId, provider);
        await SeedConnectedMailboxAsync(connection, companyId, userId, MailboxProvider.Gmail);

        var result = await service.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId, await ReadMailboxConnectionIdAsync(connection)),
            CancellationToken.None);

        Assert.NotNull(result.FailureDetails);
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var run = await dbContext.EmailIngestionRuns.SingleAsync();
        Assert.NotNull(run.CompletedUtc);
        Assert.NotNull(run.FailureDetails);
    }

    private static async Task SeedConnectedMailboxAsync(
        SqliteConnection connection,
        Guid companyId,
        Guid userId,
        MailboxProvider provider,
        string emailAddress = "ap@example.com",
        MailboxPurpose purpose = MailboxPurpose.Finance,
        DateTime? accessTokenExpiresUtc = null,
        IFieldEncryptionService? encryption = null)
    {
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        await dbContext.Database.EnsureCreatedAsync();
        if (!await dbContext.Companies.IgnoreQueryFilters().AnyAsync(x => x.Id == companyId))
        {
            dbContext.Companies.Add(new Company(companyId, "Mailbox Flow Company"));
        }

        if (!await dbContext.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == userId))
        {
            dbContext.Users.Add(new User(userId, $"user-{userId:N}@example.com", "Mailbox User", "test", userId.ToString("N")));
        }
        encryption ??= CreateEncryption();
        var mailbox = new MailboxConnection(Guid.NewGuid(), companyId, userId, provider, emailAddress, purpose: purpose);
        if (provider is MailboxProvider.Gmail or MailboxProvider.Microsoft365)
        {
            var externalProvider = provider == MailboxProvider.Gmail
                ? ExternalAccountProvider.Google
                : ExternalAccountProvider.Microsoft365;
            var externalId = Guid.NewGuid();
            var external = new ExternalAccountConnection(
                externalId, companyId, userId, externalProvider,
                emailAddress, "Mailbox User", emailAddress,
                $"mailbox:{provider.ToStorageValue()}");
            external.StoreEncryptedCredentials(
                encryption.Encrypt(companyId, external.CredentialPurpose("access_token"), "access-token"),
                encryption.Encrypt(companyId, external.CredentialPurpose("refresh_token"), "refresh-token"),
                accessTokenExpiresUtc ?? DateTime.UtcNow.AddHours(1),
                provider == MailboxProvider.Gmail
                    ? ["https://www.googleapis.com/auth/gmail.readonly"]
                    : ["Mail.Read"]);
            external.SetStatus(ExternalConnectionStatus.Active);
            mailbox.LinkExternalAccount(externalId);
            mailbox.StoreEncryptedCredentials(null, null, null, ["scope"]);
            dbContext.ExternalAccountConnections.Add(external);
        }
        else
        {
            mailbox.StoreEncryptedCredentials(
                encryption.Encrypt(companyId, StandardMailboxCredentialPurposes.AccessToken(mailbox.Id), "access-token"),
                encryption.Encrypt(companyId, StandardMailboxCredentialPurposes.RefreshToken(mailbox.Id), "refresh-token"),
                accessTokenExpiresUtc ?? DateTime.UtcNow.AddHours(1),
                ["scope"]);
        }
        mailbox.ConfigureFolders([new MailboxFolderSelection("INBOX", "Inbox")]);
        var alreadyHasPrimary = await dbContext.MailboxConnections
            .AnyAsync(x => x.CompanyId == companyId && x.Purpose == purpose && x.IsPrimaryInbound);
        mailbox.SetPrimaryInbound(!alreadyHasPrimary);
        mailbox.SetStatus(MailboxConnectionStatus.Active);
        dbContext.MailboxConnections.Add(mailbox);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedCompanyAndUserAsync(SqliteConnection connection, Guid companyId, Guid userId)
    {
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        dbContext.Companies.Add(new Company(companyId, "Mailbox Flow Company"));
        dbContext.Users.Add(new User(userId, $"user-{userId:N}@example.com", "Mailbox User", "test", userId.ToString("N")));
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> ReadMailboxConnectionIdAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM mailbox_connections LIMIT 1;";
        return Guid.Parse((string)(await command.ExecuteScalarAsync())!);
    }

    private static CompanyMailboxConnectionService CreateService(
        SqliteConnection connection,
        Guid companyId,
        Guid userId,
        FakeProvider provider,
        DateTime? nowUtc = null,
        IMailboxOAuthStateProtector? stateProtector = null,
        ICompanyContextAccessor? contextAccessor = null) =>
        new(
            CreateContext(connection, contextAccessor ?? new TestCompanyContextAccessor(companyId, userId)),
            contextAccessor ?? new TestCompanyContextAccessor(companyId, userId),
            stateProtector ?? new DataProtectionMailboxOAuthStateProtector(new EphemeralDataProtectionProvider()),
            new FakeProviderRegistry(provider),
            CreateEncryption(),
            new InlineManualInboxBillScanJobScheduler(new CompanyManualInboxBillScanOrchestrator(
                CreateContext(connection, new TestCompanyContextAccessor(companyId, userId)),
                new FakeProviderRegistry(provider),
                new BillDetectionService(),
                CreateDocumentExtractionService(connection, companyId, userId, nowUtc ?? new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc)),
                new MailboxOAuthAccessTokenLeaseService(
                    CreateContext(connection, new TestCompanyContextAccessor(companyId, userId)),
                    new FakeProviderRegistry(provider),
                    CreateEncryption()),
                new FakeTimeProvider(nowUtc ?? new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc)),
                NullLogger<CompanyManualInboxBillScanOrchestrator>.Instance)),
            new FakeTimeProvider(nowUtc ?? new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc)),
            NullLogger<CompanyMailboxConnectionService>.Instance);

    private static DocumentExtractionService CreateDocumentExtractionService(
        SqliteConnection connection,
        Guid companyId,
        Guid userId,
        DateTime nowUtc)
    {
        var timeProvider = new FakeTimeProvider(nowUtc);
        var duplicateContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var persistenceContext = CreateContext(connection, new TestCompanyContextAccessor(companyId, userId));
        var duplicateRepository = new BillDuplicateCheckRepository(duplicateContext, timeProvider);
        return new DocumentExtractionService(
            [new EmailBodyTextExtractor(), new DocxDocumentTextExtractor(), new PdfDocumentTextExtractor()],
            duplicateRepository,
            timeProvider,
            new BillInformationExtractor(duplicateRepository, timeProvider),
            new BillExtractionPersistenceRepository(persistenceContext, timeProvider));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(null, null));
        await dbContext.Database.EnsureCreatedAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor accessor) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);

    private static DataProtectionFieldEncryptionService CreateEncryption() =>
        new(TestDataProtectionProvider);

    private sealed class FakeProviderRegistry : IMailboxProviderRegistry
    {
        private readonly FakeProvider _provider;
        public FakeProviderRegistry(FakeProvider provider) => _provider = provider;
        public IMailboxProviderClient Resolve(MailboxProvider provider) => _provider.Provider == provider ? _provider : throw new InvalidOperationException();
    }

    private sealed class FakeProvider : IMailboxProviderClient
    {
        public FakeProvider(MailboxProvider provider) => Provider = provider;
        public MailboxProvider Provider { get; }
        public IReadOnlyCollection<string> DefaultScopes { get; } = ["gmail.readonly"];
        public IReadOnlyList<MailboxMessageSummary> Messages { get; init; } = [];
        public IReadOnlyDictionary<string, MailboxInboundMessage> FullMessages { get; init; } =
            new Dictionary<string, MailboxInboundMessage>();
        public MailboxMessageQuery? LastQuery { get; private set; }
        public MailboxTokenExchangeRequest? LastTokenExchangeRequest { get; private set; }
        public int ExchangeCallCount { get; private set; }
        public int RefreshCallCount { get; private set; }
        public int GetMessageCallCount { get; private set; }
        public bool ThrowOnList { get; init; }

        public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request) =>
            new(
                $"https://provider.example.test/oauth?scope={string.Join('%', DefaultScopes)}" +
                $"&redirect_uri={Uri.EscapeDataString(request.CallbackUri.ToString())}" +
                $"&state={Uri.EscapeDataString(request.State)}");

        public Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken)
        {
            ExchangeCallCount++;
            LastTokenExchangeRequest = request;
            return Task.FromResult(new MailboxOAuthTokenResult("access-token", "refresh-token", DateTime.UtcNow.AddHours(1), DefaultScopes));
        }

        public Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken)
        {
            RefreshCallCount++;
            return Task.FromResult(new MailboxOAuthTokenResult("access-token", "refresh-token", DateTime.UtcNow.AddHours(1), DefaultScopes));
        }

        public Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new MailboxAccountProfile("ap@example.com", "AP", "provider-account"));

        public Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken)
        {
            if (ThrowOnList)
            {
                throw new InvalidOperationException("Provider throttled.");
            }

            LastQuery = query;
            return Task.FromResult(Messages);
        }

        public Task<MailboxInboundMessage> GetMessageAsync(string accessToken, MailboxMessageFetchRequest request, CancellationToken cancellationToken)
        {
            GetMessageCallCount++;
            return FullMessages.TryGetValue(request.MessageId, out var message)
                ? Task.FromResult(message)
                : throw new NotSupportedException("This fake mailbox provider has no full message for the requested id.");
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTime nowUtc) => _now = new DateTimeOffset(nowUtc);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid? companyId, Guid? userId)
        {
            CompanyId = companyId;
            UserId = userId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => CompanyId.HasValue && UserId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }

    private sealed class ThrowingStateProtector : IMailboxOAuthStateProtector
    {
        public int ProtectCallCount { get; private set; }

        public string Protect(MailboxOAuthState state)
        {
            ProtectCallCount++;
            throw new InvalidOperationException("State should not be issued.");
        }

        public MailboxOAuthState Unprotect(string protectedState) =>
            throw new NotSupportedException();
    }
}
