using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class EnableBankingProviderContractTests
{
    [Fact]
    public async Task Transaction_contract_preserves_stable_entry_identity_status_pagination_and_raw_evidence()
    {
        using var rsa = RSA.Create(2048);
        var handler = new ScriptedHandler(
            Json(HttpStatusCode.OK, """
                {
                  "transactions": [{
                    "entry_reference": "stable-entry-1",
                    "transaction_id": "session-transaction-99",
                    "status": "BOOK",
                    "booking_date": "2026-08-27",
                    "value_date": "2026-08-27",
                    "transaction_date": "2026-08-26",
                    "transaction_amount": { "amount": "125.50", "currency": "SEK" },
                    "credit_debit_indicator": "DBIT",
                    "remittance_information": ["Invoice", "4711"],
                    "creditor": { "name": "Supplier AB" }
                  }],
                  "continuation_key": "next-page"
                }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "transactions": [{
                    "entry_reference": "pending-entry-1",
                    "transaction_id": "pending-session-id",
                    "status": "PDNG",
                    "transaction_date": "2026-08-28",
                    "transaction_amount": { "amount": "75.00", "currency": "SEK" },
                    "credit_debit_indicator": "CRDT",
                    "debtor": { "name": "Customer AB" },
                    "note": "Incoming payment"
                  }]
                }
                """));
        var provider = CreateProvider(handler, rsa);

        var booked = await provider.GetTransactionsAsync(Guid.NewGuid(), "session", Credentials(),
            new("account uid/1", new(2026, 8, 1), new(2026, 8, 28),
                BankFeedProviderTransactionStatuses.Booked, "previous page"), default);
        var pending = await provider.GetTransactionsAsync(Guid.NewGuid(), "session", Credentials(),
            new("account uid/1", new(2026, 8, 1), new(2026, 8, 28),
                BankFeedProviderTransactionStatuses.Pending, null), default);

        var bookedRow = Assert.Single(booked.Transactions);
        Assert.Equal("stable-entry-1", bookedRow.StableIdentity);
        Assert.Equal("session-transaction-99", bookedRow.ProviderTransactionReference);
        Assert.Equal(-125.50m, bookedRow.Amount);
        Assert.Equal("Invoice 4711", bookedRow.ReferenceText);
        Assert.Equal("Supplier AB", bookedRow.Counterparty);
        Assert.Equal("next-page", booked.NextContinuationToken);
        Assert.Contains("stable-entry-1", Encoding.UTF8.GetString(booked.SourceEvidence.Span));

        var pendingRow = Assert.Single(pending.Transactions);
        Assert.Equal(BankFeedProviderTransactionStatuses.Pending, pendingRow.Status);
        Assert.Null(pendingRow.BookingDateUtc);
        Assert.Equal(75m, pendingRow.Amount);
        Assert.Contains("transaction_status=BOOK", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("continuation_key=previous%20page", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("transaction_status=PDNG", handler.Requests[1].RequestUri!.Query);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(3, request.Headers.Authorization!.Parameter!.Split('.').Length);
        });
    }

    [Fact]
    public async Task Account_and_balance_contract_separates_stable_identity_from_session_access_reference()
    {
        using var rsa = RSA.Create(2048);
        var handler = new ScriptedHandler(
            Json(HttpStatusCode.OK, """
                { "accounts_data": [{ "uid": "session-account-uid", "identification_hash": "stable-account-hash" }] }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "name": "Operating account",
                  "currency": "SEK",
                  "psu_status": "Account Holder",
                  "account_id": { "iban": "SE3550000000054910000003" }
                }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "balances": [{
                    "balance_type": "CLAV",
                    "balance_amount": { "amount": "1234.56", "currency": "SEK" },
                    "reference_date": "2026-08-28",
                    "last_committed_transaction": "stable-entry-9"
                  }]
                }
                """));
        var provider = CreateProvider(handler, rsa);

        var account = Assert.Single(await provider.DiscoverAccountsAsync(Guid.NewGuid(), "consent-1",
            Credentials(), default));
        Assert.Equal("stable-account-hash", account.ProviderAccountId);
        Assert.Equal("session-account-uid", account.ProviderAccessReference);
        Assert.Equal(BankAccountOwnershipStatuses.Verified, account.OwnershipStatus);
        Assert.EndsWith("0003", account.MaskedAccountNumber, StringComparison.Ordinal);

        var balance = Assert.Single((await provider.GetBalancesAsync(Guid.NewGuid(), "consent-1",
            Credentials(), account.ProviderAccessReference!, default)).Balances);
        Assert.Equal(1234.56m, balance.Amount);
        Assert.Equal("stable-entry-9", balance.LastCommittedTransactionIdentity);
        Assert.Equal("/accounts/session-account-uid/balances", handler.Requests[2].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Rate_limit_is_safe_transient_and_honors_retry_after_without_exposing_provider_body()
    {
        using var rsa = RSA.Create(2048);
        var response = Json(HttpStatusCode.TooManyRequests, "{\"error\":\"sensitive-provider-detail\"}");
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
        var provider = CreateProvider(new ScriptedHandler(response), rsa);

        var error = await Assert.ThrowsAsync<BankProviderSafeException>(() => provider.GetBalancesAsync(
            Guid.NewGuid(), "consent", Credentials(), "account", default));

        Assert.Equal(BankFeedReasonCodes.RateLimited, error.ReasonCode);
        Assert.True(error.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(90), error.RetryAfter);
        Assert.DoesNotContain("sensitive-provider-detail", error.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"transactions\":[{\"status\":\"BOOK\"}]}")]
    [InlineData("{\"transactions\":{}}")]
    [InlineData("{\"transactions\":[{\"entry_reference\":\"x\",\"status\":\"PDNG\"}]}")]
    public async Task Malformed_or_ambiguous_transaction_responses_fail_closed(string payload)
    {
        using var rsa = RSA.Create(2048);
        var provider = CreateProvider(new ScriptedHandler(Json(HttpStatusCode.OK, payload)), rsa);

        var error = await Assert.ThrowsAsync<BankProviderSafeException>(() => provider.GetTransactionsAsync(
            Guid.NewGuid(), "consent", Credentials(), new("account", new(2026, 8, 1), new(2026, 8, 28),
                BankFeedProviderTransactionStatuses.Booked, null), default));

        Assert.Equal(BankFeedReasonCodes.MalformedSource, error.ReasonCode);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task Payment_initiation_contract_maps_approved_sepa_instruction_and_separates_provider_completion()
    {
        using var rsa = RSA.Create(2048);
        var handler = new ScriptedHandler(
            Json(HttpStatusCode.OK, """
                { "payment_id": "payment-71", "status": "RCVD", "url": "https://bank.test/authorize/71" }
                """),
            Json(HttpStatusCode.OK, """
                {
                  "payment_id": "payment-71",
                  "status": "ACSC",
                  "final_status": true,
                  "payment_details": {
                    "debtor_account": { "identification": "SE3550000000054910000003" },
                    "credit_transfer_transaction": [{
                      "payment_id": { "instruction_id": "11111111-1111-1111-1111-111111111111" },
                      "transaction_id": "provider-transaction-9",
                      "transaction_status": "ACSC"
                    }]
                  }
                }
                """));
        var provider = CreateProvider(handler, rsa);
        var instructionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var submitted = await provider.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "business-key",
            "SE|Sandbox Bank", "consent", Credentials(), new("https://app.test/payment-return"),
            new("https://api.test/webhooks/finance/payment-initiation/enable-banking"),
            [new(instructionId, Guid.NewGuid(), 1, new DateOnly(2026, 8, 31), 125.50m, "EUR",
                "RF18539007547034", "Supplier AB", PaymentRails.SepaCreditTransfer,
                "SE3550000000054910000003", new string('a', 64))]), default);
        var completed = await provider.GetStatusAsync(Guid.NewGuid(), submitted.ProviderPaymentId, default);

        Assert.Equal("payment-71", submitted.ProviderPaymentId);
        Assert.Equal("RCVD", submitted.Status);
        Assert.False(submitted.IsFinal);
        Assert.False(submitted.CanCancel);
        Assert.Contains("\"payment_type\":\"SEPA\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"instruction_id\":\"11111111-1111-1111-1111-111111111111\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("\"identification\":\"SE3550000000054910000003\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Equal("ACSC", completed.Status);
        Assert.True(completed.IsFinal);
        Assert.Equal("•••• 0003", completed.DebtorAccountMasked);
        Assert.Equal("provider-transaction-9", Assert.Single(completed.Instructions).ProviderTransactionId);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/payments", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/payments/payment-71", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Terminal_create_payment_response_does_not_invent_an_authorization_url()
    {
        using var rsa = RSA.Create(2048);
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, """
            {
              "payment_id": "payment-terminal-1",
              "status": "ACSC",
              "final_status": true,
              "status_reason_information": {
                "status_reason_code": "ACSC",
                "status_reason_description": "Completed by the provider"
              }
            }
            """));
        var provider = CreateProvider(handler, rsa);
        var instructionId = Guid.NewGuid();

        var result = await provider.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "terminal-key",
            "SE|Sandbox Bank", "consent", Credentials(), new("https://app.test/payment-return"),
            new("https://api.test/webhooks/finance/payment-initiation/enable-banking"),
            [new(instructionId, Guid.NewGuid(), 1, new DateOnly(2026, 8, 31), 42m, "EUR",
                "REF-TERMINAL", "Supplier AB", PaymentRails.SepaCreditTransfer,
                "SE3550000000054910000003", new string('c', 64))]), default);

        Assert.True(result.IsFinal);
        Assert.False(result.UpdatesExpected);
        Assert.Null(result.AuthorizationUri);
        Assert.Equal("ACSC", result.ReasonCode);
        var instruction = Assert.Single(result.Instructions);
        Assert.True(instruction.IsFinal);
        Assert.Equal("ACSC", instruction.Status);
    }

    [Fact]
    public async Task Created_payment_without_safe_authorization_url_retains_reference_for_status_reconciliation()
    {
        using var rsa = RSA.Create(2048);
        var provider = CreateProvider(new ScriptedHandler(Json(HttpStatusCode.OK, """
            { "payment_id": "payment-no-url-1", "status": "RCVD" }
            """)), rsa);

        var result = await provider.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "missing-url-key",
            "SE|Sandbox Bank", "consent", Credentials(), new("https://app.test/payment-return"),
            new("https://api.test/webhooks/finance/payment-initiation/enable-banking"),
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new DateOnly(2026, 8, 31), 42m, "EUR",
                "REF-NO-URL", "Supplier AB", PaymentRails.SepaCreditTransfer,
                "SE3550000000054910000003", new string('d', 64))]), default);

        Assert.Equal("payment-no-url-1", result.ProviderPaymentId);
        Assert.Null(result.AuthorizationUri);
        Assert.False(result.IsFinal);
        Assert.True(result.UpdatesExpected);
        Assert.Equal(PaymentExecutionReasonCodes.StatusReconciliationRequired, result.ReasonCode);
    }

    [Fact]
    public async Task Malformed_http_success_retains_known_provider_payment_identity_and_blocks_resubmission()
    {
        using var rsa = RSA.Create(2048);
        var provider = CreateProvider(new ScriptedHandler(Json(HttpStatusCode.OK, """
            { "payment_id": "payment-malformed-1" }
            """)), rsa);

        var error = await Assert.ThrowsAsync<PaymentProviderOperationException>(() => provider.SubmitAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), "malformed-success-key", "SE|Sandbox Bank", "consent",
                Credentials(), new("https://app.test/payment-return"),
                new("https://api.test/webhooks/finance/payment-initiation/enable-banking"),
                [new(Guid.NewGuid(), Guid.NewGuid(), 1, new DateOnly(2026, 8, 31), 42m, "EUR",
                    "REF-MALFORMED", "Supplier AB", PaymentRails.SepaCreditTransfer,
                    "SE3550000000054910000003", new string('e', 64))]), default));

        Assert.True(error.IsAmbiguous);
        Assert.False(error.IsRetryable);
        Assert.Equal("payment-malformed-1", error.ProviderPaymentId);
        Assert.Equal(PaymentExecutionReasonCodes.SubmissionAmbiguous, error.ReasonCode);
    }

    [Fact]
    public async Task Final_bulk_status_retains_each_partial_instruction_outcome()
    {
        using var rsa = RSA.Create(2048);
        var completedId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, $$"""
            {
              "payment_id": "payment-partial-1",
              "status": "PART",
              "final_status": true,
              "payment_details": {
                "debtor_account": { "identification": "SE3550000000054910000003" },
                "credit_transfer_transaction": [
                  {
                    "payment_id": { "instruction_id": "{{completedId:D}}" },
                    "transaction_id": "transaction-completed",
                    "transaction_status": "ACSC"
                  },
                  {
                    "payment_id": { "instruction_id": "{{rejectedId:D}}" },
                    "transaction_id": "transaction-rejected",
                    "transaction_status": "RJCT"
                  }
                ]
              }
            }
            """));
        var provider = CreateProvider(handler, rsa);

        var result = await provider.GetStatusAsync(Guid.NewGuid(), "payment-partial-1", default);

        Assert.True(result.IsFinal);
        Assert.False(result.UpdatesExpected);
        Assert.Equal("ACSC", Assert.Single(result.Instructions, x => x.InstructionId == completedId).Status);
        Assert.Equal("RJCT", Assert.Single(result.Instructions, x => x.InstructionId == rejectedId).Status);
    }

    [Fact]
    public async Task Unknown_payment_submission_transport_outcome_is_ambiguous_and_not_safe_to_replay()
    {
        using var rsa = RSA.Create(2048);
        var provider = CreateProvider(new ThrowingHandler(), rsa);
        var instructionId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<PaymentProviderOperationException>(() => provider.SubmitAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), "business-key", "SE|Sandbox Bank", "consent", Credentials(),
                new("https://app.test/payment-return"),
                new("https://api.test/webhooks/finance/payment-initiation/enable-banking"),
                [new(instructionId, Guid.NewGuid(), 1, new DateOnly(2026, 8, 31), 100m, "EUR", "REF-1",
                    "Supplier AB", PaymentRails.SepaCreditTransfer, "SE3550000000054910000003", new string('b', 64))]),
            default));

        Assert.True(error.IsAmbiguous);
        Assert.False(error.IsRetryable);
        Assert.Equal(PaymentExecutionReasonCodes.SubmissionAmbiguous, error.ReasonCode);
    }

    [Fact]
    public async Task Payment_webhook_requires_trusted_rs256_signature_subject_environment_and_body_digest()
    {
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=webhook.test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var certificateResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(certificate.ExportCertificatePem()))
        };
        var provider = CreateProvider(new ScriptedHandler(certificateResponse), rsa);
        var payload = Encoding.UTF8.GetBytes("""
            {"payment_id":"payment-71","webhook_id":"webhook-9","payment_status":"ACSP","payment_updates_expected":true,"webhook_triggered":"2026-08-28T12:00:00Z"}
            """);
        var token = SignWebhookToken(rsa, payload);

        var result = await provider.ValidateWebhookAsync($"Bearer {token}", payload, default);

        Assert.Equal("payment-71", result.ProviderPaymentId);
        Assert.Equal("webhook-9", result.WebhookId);
        Assert.Equal("ACSP", result.Status);
        Assert.True(result.UpdatesExpected);

        var secondProvider = CreateProvider(new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(certificate.ExportCertificatePem()))
        }), rsa);
        var rejected = await Assert.ThrowsAsync<PaymentProviderOperationException>(() =>
            secondProvider.ValidateWebhookAsync($"Bearer {token}", Encoding.UTF8.GetBytes("{}"), default));
        Assert.Equal(PaymentExecutionReasonCodes.WebhookInvalid, rejected.ReasonCode);
    }

    private static EnableBankingProvider CreateProvider(HttpMessageHandler handler, RSA rsa)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.test/") };
        var options = Options.Create(new EnableBankingOptions
        {
            Enabled = true,
            BaseUri = client.BaseAddress.ToString(),
            ApplicationId = "application-id",
            PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
            PaymentInitiationEnabled = true,
            Environment = "SANDBOX"
        });
        return new EnableBankingProvider(new SingleClientFactory(client), options,
            new FixedTimeProvider(new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc)),
            NullLogger<EnableBankingProvider>.Instance);
    }

    private static BankProviderCredentialBundle Credentials() => new("session", null, null, null);

    private static HttpResponseMessage Json(HttpStatusCode status, string payload) => new(status)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private static string SignWebhookToken(RSA rsa, byte[] payload)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            typ = "JWT",
            alg = "RS256",
            x5u = "https://webhooks.enablebanking.com/signing-certificate.pem"
        }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = "application-id",
            environment = "SANDBOX",
            msgi = "sha256-" + Convert.ToBase64String(SHA256.HashData(payload))
        }));
        var input = Encoding.ASCII.GetBytes($"{header}.{claims}");
        var signature = rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);
            Requests.Add(Clone(request));
            return Task.FromResult(_responses.Dequeue());
        }
        private static HttpRequestMessage Clone(HttpRequestMessage source)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri);
            foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("simulated broken connection");
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
