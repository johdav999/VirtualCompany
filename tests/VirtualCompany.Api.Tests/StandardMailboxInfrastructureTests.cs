using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class StandardMailboxInfrastructureTests
{
    [Theory]
    [InlineData("standard_email")]
    [InlineData("standard-email")]
    [InlineData("Standard Email")]
    public void Standard_mailbox_provider_storage_value_can_be_materialized(string value)
    {
        Assert.Equal(MailboxProvider.StandardEmail, MailboxProviderValues.Parse(value));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    public async Task Endpoint_policy_rejects_non_public_destinations(string host)
    {
        var policy = new SecureMailboxEndpointPolicy();

        var result = await policy.EvaluateAsync(
            new MailboxEndpointSettings(host, 993, MailboxTlsMode.ImplicitTls),
            CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("private_network_not_allowed", result.ReasonCode);
    }

    [Theory]
    [InlineData(143, MailboxTlsMode.StartTls)]
    [InlineData(993, MailboxTlsMode.StartTls)]
    [InlineData(465, MailboxTlsMode.StartTls)]
    [InlineData(587, MailboxTlsMode.ImplicitTls)]
    public async Task Endpoint_policy_rejects_unsupported_port_or_tls_pairs(int port, MailboxTlsMode tlsMode)
    {
        var result = await new SecureMailboxEndpointPolicy().EvaluateAsync(
            new MailboxEndpointSettings("8.8.8.8", port, tlsMode),
            CancellationToken.None);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Endpoint_policy_accepts_public_secure_endpoint()
    {
        var result = await new SecureMailboxEndpointPolicy().EvaluateAsync(
            new MailboxEndpointSettings("8.8.8.8", 993, MailboxTlsMode.ImplicitTls),
            CancellationToken.None);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Zoho_and_custom_profiles_share_the_same_transport_contract()
    {
        var registry = new StandardMailboxConnectionProfileRegistry();

        var zoho = registry.Resolve(StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey);
        var custom = registry.Resolve(StandardMailboxConnectionProfileRegistry.CustomProfileKey);

        Assert.Equal("imappro.zoho.eu", zoho.Imap.Host);
        Assert.Contains(MailboxAuthenticationType.ApplicationPassword, zoho.AuthenticationTypes);
        Assert.False(zoho.AllowsEndpointOverride);
        Assert.True(custom.AllowsEndpointOverride);
    }

    [Fact]
    public void Trusted_profile_accepts_its_canonical_endpoint_from_the_web_contract()
    {
        var profile = new StandardMailboxConnectionProfileRegistry()
            .Resolve(StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey);

        var resolved = StandardMailboxConnectionService.ResolveEndpoint(profile, profile.Imap, profile.Imap);

        Assert.Same(profile.Imap, resolved);
    }

    [Fact]
    public void Trusted_profile_rejects_a_changed_endpoint()
    {
        var profile = new StandardMailboxConnectionProfileRegistry()
            .Resolve(StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey);
        var changed = new MailboxEndpointSettings("imap.example.com", profile.Imap.Port, profile.Imap.TlsMode);

        Assert.Throws<ArgumentException>(() =>
            StandardMailboxConnectionService.ResolveEndpoint(profile, changed, profile.Imap));
    }

    [Fact]
    public void Unknown_profile_has_safe_failure()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() =>
            new StandardMailboxConnectionProfileRegistry().Resolve("not-a-profile"));

        Assert.DoesNotContain("token", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not available", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Administrator_profile_is_loaded_from_trusted_configuration()
    {
        var options = new MailboxIntegrationOptions
        {
            StandardProfiles =
            [
                new MailboxIntegrationOptions.StandardProfileOptions
                {
                    ProfileKey = "example-hosted",
                    DisplayName = "Example hosted mail",
                    Region = "Europe",
                    ImapHost = "imap.example.com",
                    SmtpHost = "smtp.example.com",
                    SmtpPort = 587,
                    SmtpTlsMode = MailboxTlsMode.StartTls
                }
            ]
        };

        var profile = new StandardMailboxConnectionProfileRegistry(Options.Create(options)).Resolve("example-hosted");

        Assert.Equal("imap.example.com", profile.Imap.Host);
        Assert.Equal(587, profile.Smtp.Port);
        Assert.Null(profile.OAuth);
        Assert.Contains(MailboxAuthenticationType.ApplicationPassword, profile.AuthenticationTypes);
    }

    [Fact]
    public void Administrator_profile_cannot_replace_built_in_or_use_unsafe_transport()
    {
        var duplicate = new MailboxIntegrationOptions
        {
            StandardProfiles =
            [
                new MailboxIntegrationOptions.StandardProfileOptions
                {
                    ProfileKey = StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey,
                    DisplayName = "Replacement",
                    Region = "Unknown",
                    ImapHost = "imap.example.com",
                    SmtpHost = "smtp.example.com"
                }
            ]
        };
        Assert.Throws<OptionsValidationException>(() =>
            new StandardMailboxConnectionProfileRegistry(Options.Create(duplicate)));

        var plaintext = new MailboxIntegrationOptions
        {
            StandardProfiles =
            [
                new MailboxIntegrationOptions.StandardProfileOptions
                {
                    ProfileKey = "unsafe-hosted",
                    DisplayName = "Unsafe",
                    Region = "Unknown",
                    ImapHost = "imap.example.com",
                    ImapPort = 143,
                    ImapTlsMode = MailboxTlsMode.StartTls,
                    SmtpHost = "smtp.example.com"
                }
            ]
        };
        Assert.Throws<OptionsValidationException>(() =>
            new StandardMailboxConnectionProfileRegistry(Options.Create(plaintext)));
    }

    [Fact]
    public void Cursor_marks_reconciliation_when_uid_validity_changes()
    {
        var cursor = new MailboxFolderSyncCursor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INBOX", DateTime.UtcNow);
        cursor.Advance(100, 44, 200, DateTime.UtcNow);

        cursor.Advance(101, 45, 201, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(MailboxCursorStatus.ReconciliationRequired, cursor.Status);
        Assert.Equal(44, cursor.LastProcessedUid);
    }

    [Fact]
    public void Cursor_can_resume_from_zero_only_after_bounded_reconciliation()
    {
        var cursor = new MailboxFolderSyncCursor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INBOX", DateTime.UtcNow);
        cursor.Advance(100, 44, 200, DateTime.UtcNow);
        cursor.Advance(101, 45, 201, DateTime.UtcNow.AddMinutes(1));

        cursor.ResetAfterReconciliation(101, DateTime.UtcNow.AddMinutes(2));
        cursor.Advance(101, 3, null, DateTime.UtcNow.AddMinutes(3));

        Assert.Equal(MailboxCursorStatus.Active, cursor.Status);
        Assert.Equal(101, cursor.UidValidity);
        Assert.Equal(3, cursor.LastProcessedUid);
    }

    [Fact]
    public void Transport_registry_rejects_duplicate_keys()
    {
        var transport = new StubTransport();

        Assert.Throws<ArgumentException>(() => new MailboxTransportRegistry([transport, transport]));
    }

    [Fact]
    public async Task Operation_gate_bounds_parallel_work_per_connection_and_recovers_capacity()
    {
        using var gate = new MailboxOperationConcurrencyGate();
        var companyId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var first = await gate.TryAcquireAsync(companyId, connectionId, "smtp.example.com", CancellationToken.None);
        var second = await gate.TryAcquireAsync(companyId, connectionId, "smtp.example.com", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(await gate.TryAcquireAsync(companyId, connectionId, "smtp.example.com", CancellationToken.None));

        await first!.DisposeAsync();
        var recovered = await gate.TryAcquireAsync(companyId, connectionId, "smtp.example.com", CancellationToken.None);
        Assert.NotNull(recovered);
        await recovered!.DisposeAsync();
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task Ambiguous_submission_is_reconciled_by_stable_message_id_without_resending()
    {
        var transport = new AmbiguousTransport(reconcile: true);
        var provider = new StandardMailboxProviderClient(new MailboxTransportRegistry([transport]));
        var context = new MailboxTransportContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "support@example.com",
            new MailboxTransportSettings(
                new MailboxEndpointSettings("imap.example.com", 993, MailboxTlsMode.ImplicitTls),
                new MailboxEndpointSettings("smtp.example.com", 465, MailboxTlsMode.ImplicitTls)),
            new MailboxCredentialLease(MailboxAuthenticationType.ApplicationPassword, "support@example.com", "not-a-real-secret", null));
        var session = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context)));

        var result = await provider.SendReplyAsync(
            session,
            new MailboxReplyExecutionRequest(
                context.CompanyId,
                context.ConnectionId,
                "standard_email",
                "original",
                null,
                "<original@example.com>",
                "customer@example.com",
                null,
                "Re: Help",
                "Reply",
                "stable-intent"),
            CancellationToken.None);

        Assert.Equal("sent", result.Status);
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(1, transport.ReconciliationCalls);
        Assert.Equal("<original@example.com>", transport.LastMessage!.InReplyTo);
        Assert.Equal(["<original@example.com>"], transport.LastMessage.References);
        Assert.StartsWith("<", transport.LastMessage.MessageId);
        Assert.EndsWith("@example.com>", transport.LastMessage.MessageId);
    }

    [Fact]
    public async Task Transport_rejects_an_untrusted_tls_certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=mail.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("mail.test");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            using var tls = new SslStream(socket.GetStream(), false);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException)
            {
            }
        });
        var transport = new MailKitMailboxTransport(new FixedEndpointPolicy(IPAddress.Loopback));

        var result = await transport.TestIncomingAsync(CreateProtocolTestContext(port), CancellationToken.None);

        Assert.False(result.ImapSucceeded);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Transport_rejects_plaintext_on_an_implicit_tls_endpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("* OK plaintext is not TLS\r\n"));
        });
        var transport = new MailKitMailboxTransport(new FixedEndpointPolicy(IPAddress.Loopback));

        var result = await transport.TestIncomingAsync(CreateProtocolTestContext(port), CancellationToken.None);

        Assert.False(result.ImapSucceeded);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Smtp_protocol_phase_only_marks_delivery_ambiguous_after_server_accepts_data()
    {
        using var beforeData = new MailKitMailboxTransport.SmtpSubmissionProtocolState();
        beforeData.LogClient(Encoding.ASCII.GetBytes("DATA\r\n"), 0, 6);
        beforeData.LogServer(Encoding.ASCII.GetBytes("451 temporary failure\r\n"), 0, 23);
        Assert.False(beforeData.MessageBodyAccepted);

        using var afterData = new MailKitMailboxTransport.SmtpSubmissionProtocolState();
        afterData.LogClient(Encoding.ASCII.GetBytes("DATA\r\n"), 0, 6);
        afterData.LogServer(Encoding.ASCII.GetBytes("354 continue\r\n"), 0, 14);
        Assert.True(afterData.MessageBodyAccepted);
    }

    [Fact]
    public void Application_password_authentication_excludes_oauth_mechanisms()
    {
        var mechanisms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLAIN",
            "LOGIN",
            "XOAUTH2",
            "OAUTHBEARER"
        };

        MailKitMailboxTransport.ConfigureApplicationPasswordAuthentication(mechanisms);

        Assert.Contains("PLAIN", mechanisms);
        Assert.Contains("LOGIN", mechanisms);
        Assert.DoesNotContain("XOAUTH2", mechanisms);
        Assert.DoesNotContain("OAUTHBEARER", mechanisms);
    }

    [Fact]
    public void Zoho_application_password_removes_display_formatting_whitespace()
    {
        var normalized = StandardMailboxConnectionService.NormalizeApplicationPassword(
            StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey,
            "abcd efgh\tijkl\r\n");

        Assert.Equal("abcdefghijkl", normalized);
        Assert.Equal(
            "valid password with spaces",
            StandardMailboxConnectionService.NormalizeApplicationPassword(
                StandardMailboxConnectionProfileRegistry.CustomProfileKey,
                "valid password with spaces"));
    }

    private static MailboxTransportContext CreateProtocolTestContext(int port) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "support@mail.test",
            new MailboxTransportSettings(
                new MailboxEndpointSettings("mail.test", port, MailboxTlsMode.ImplicitTls),
                new MailboxEndpointSettings("mail.test", port, MailboxTlsMode.ImplicitTls),
                ConnectionTimeoutSeconds: 2,
                CommandTimeoutSeconds: 2),
            new MailboxCredentialLease(MailboxAuthenticationType.ApplicationPassword, "support@mail.test", "not-a-real-secret", null));


    [Fact]
    public async Task OAuth_state_nonce_can_only_be_consumed_once()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using (var registrationContext = new VirtualCompanyDbContext(options))
        {
            await registrationContext.Database.EnsureCreatedAsync();
        }
        var now = DateTime.UtcNow;
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var registrationContext = new VirtualCompanyDbContext(options))
        {
            var registrationGuard = new MailboxOAuthReplayGuard(registrationContext);
            await registrationGuard.RegisterAsync(
                companyId,
                userId,
                MailboxPurpose.Support,
                MailboxProvider.Gmail,
                "one-time-state",
                now.AddMinutes(10),
                CancellationToken.None);
        }

        await using (var wrongTenantContext = new VirtualCompanyDbContext(options))
        {
            var wrongTenantGuard = new MailboxOAuthReplayGuard(wrongTenantContext);
            Assert.False(await wrongTenantGuard.TryConsumeAsync(Guid.NewGuid(), userId, MailboxPurpose.Support, MailboxProvider.Gmail, "one-time-state", now, CancellationToken.None));
        }

        await using (var callbackContext = new VirtualCompanyDbContext(options))
        {
            var callbackGuard = new MailboxOAuthReplayGuard(callbackContext);
            Assert.True(await callbackGuard.TryConsumeAsync(companyId, userId, MailboxPurpose.Support, MailboxProvider.Gmail, "one-time-state", now, CancellationToken.None));
        }

        await using (var replayContext = new VirtualCompanyDbContext(options))
        {
            var replayGuard = new MailboxOAuthReplayGuard(replayContext);
            Assert.False(await replayGuard.TryConsumeAsync(companyId, userId, MailboxPurpose.Support, MailboxProvider.Gmail, "one-time-state", now, CancellationToken.None));
        }
    }

    [Fact]
    public void Standard_message_reference_preserves_uid_checkpoint_and_transport_locator()
    {
        var reference = StandardMailboxMessageReference.WithUidValidity("SU5CT1g.42", 1234);

        Assert.True(StandardMailboxMessageReference.TryRead(reference, out var uidValidity, out var uid));
        Assert.Equal(1234, uidValidity);
        Assert.Equal(42, uid);
        Assert.Equal("SU5CT1g.42", StandardMailboxMessageReference.WithoutUidValidity(reference));
    }

    private sealed class StubTransport : IMailboxTransport
    {
        public string TransportKey => "standard";
        public Task<MailboxTransportHealthResult> TestAsync(MailboxTransportContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<MailboxTransportFolder>> ListFoldersAsync(MailboxTransportContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxIncrementalPage> ReadIncrementalAsync(MailboxTransportContext context, MailboxIncrementalQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxInboundMessage> GetMessageAsync(MailboxTransportContext context, MailboxMessageFetchRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxAttachmentContent?> GetAttachmentAsync(MailboxTransportContext context, MailboxAttachmentFetchRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxSubmissionResult> CreateDraftAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxSubmissionResult> SendAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class AmbiguousTransport(bool reconcile) : IMailboxTransport
    {
        public string TransportKey => MailKitMailboxTransport.Key;
        public int SendCalls { get; private set; }
        public int ReconciliationCalls { get; private set; }
        public MailboxOutboundMessage? LastMessage { get; private set; }
        public Task<MailboxSubmissionResult> SendAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken)
        {
            SendCalls++;
            LastMessage = message;
            return Task.FromResult(new MailboxSubmissionResult(
                MailboxSubmissionOutcome.Ambiguous,
                message.MessageId,
                null,
                "smtp_delivery_ambiguous",
                "Delivery could not be confirmed."));
        }
        public Task<string?> FindSentMessageAsync(MailboxTransportContext context, string messageId, CancellationToken cancellationToken)
        {
            ReconciliationCalls++;
            return Task.FromResult(reconcile ? "sent-folder-reference" : null);
        }
        public Task<MailboxTransportHealthResult> TestAsync(MailboxTransportContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<MailboxTransportFolder>> ListFoldersAsync(MailboxTransportContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxIncrementalPage> ReadIncrementalAsync(MailboxTransportContext context, MailboxIncrementalQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxInboundMessage> GetMessageAsync(MailboxTransportContext context, MailboxMessageFetchRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxAttachmentContent?> GetAttachmentAsync(MailboxTransportContext context, MailboxAttachmentFetchRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MailboxSubmissionResult> CreateDraftAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FixedEndpointPolicy(IPAddress address) : IMailboxEndpointPolicy
    {
        public Task<MailboxEndpointPolicyDecision> EvaluateAsync(MailboxEndpointSettings endpoint, CancellationToken cancellationToken) =>
            Task.FromResult(MailboxEndpointPolicyDecision.Permit([address]));
    }
}
