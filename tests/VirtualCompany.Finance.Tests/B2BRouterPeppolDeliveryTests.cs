using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class B2BRouterPeppolDeliveryTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("0007", "556677-8899", "0007", "5566778899")]
    [InlineData("gln", "7300010000001", "0088", "7300010000001")]
    [InlineData("peppol", "0007:5566778899", "0007", "5566778899")]
    public void Retained_participant_route_is_normalized_without_provider_schema_leakage(
        string type, string identifier, string expectedScheme, string expectedIdentifier)
    {
        var route = B2BRouterInvoiceSnapshot.ReadRoute(Snapshot(type, identifier), "customer_invoice");

        Assert.True(route.Supported);
        Assert.Equal(expectedScheme, route.ParticipantScheme);
        Assert.Equal(expectedIdentifier, route.ParticipantIdentifier);
        Assert.Equal("invoice", route.DocumentType);
    }

    [Fact]
    public void Peppol_bis_billing_document_is_deterministic_and_contains_required_profile_tax_route_and_attachment()
    {
        var delivery = Delivery();
        var pdf = "%PDF-1.7\nfixture"u8.ToArray();

        var first = B2BRouterPeppolBisBillingDocument.Build(Snapshot("0007", "5566778899"), delivery,
            "INV-100.pdf", pdf, "SE3550000000054910000003", "Operating account", "ESSESESS");
        var second = B2BRouterPeppolBisBillingDocument.Build(Snapshot("0007", "5566778899"), delivery,
            "INV-100.pdf", pdf, "SE3550000000054910000003", "Operating account", "ESSESESS");

        Assert.True(first.Validation.IsValid);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.Validation.DocumentHash, second.Validation.DocumentHash);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(first.Content));
        XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        Assert.Equal("urn:oasis:names:specification:ubl:schema:xsd:Invoice-2", xml.Root!.Name.NamespaceName);
        Assert.Equal("urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
            xml.Descendants(cbc + "CustomizationID").Single().Value);
        Assert.Contains(xml.Descendants(cbc + "EndpointID"), x => (string?)x.Attribute("schemeID") == "0007" && x.Value == "5566778899");
        Assert.Equal("2026-09-25", xml.Descendants(cbc + "DueDate").Single().Value);
        Assert.Contains(xml.Descendants(cbc + "TaxAmount"), x => x.Value == "25.00");
        var attachment = xml.Descendants(cbc + "EmbeddedDocumentBinaryObject").Single();
        Assert.Equal("application/pdf", (string?)attachment.Attribute("mimeCode"));
        Assert.Equal(pdf, Convert.FromBase64String(attachment.Value));
    }

    [Fact]
    public void Local_profile_validation_stops_before_transport_when_payment_or_buyer_evidence_is_missing()
    {
        var invalidSnapshot = Snapshot("0007", "5566778899").Replace("\"BUYER-42\"", "null", StringComparison.Ordinal);

        var result = B2BRouterPeppolBisBillingDocument.Build(invalidSnapshot, Delivery(), "INV-100.pdf",
            "%PDF-1.7\nfixture"u8.ToArray(), null, null, null);

        Assert.False(result.Validation.IsValid);
        Assert.Contains("buyer_reference_missing", result.Validation.ReasonCodes);
        Assert.Contains("payment_account_missing", result.Validation.ReasonCodes);
        Assert.Empty(result.Content);
    }

    [Fact]
    public void Credit_note_uses_ubl_credit_note_and_retains_the_original_invoice_reference()
    {
        var result = B2BRouterPeppolBisBillingDocument.Build(Snapshot("0007", "5566778899"),
            Delivery("credit_note"), "CR-100.pdf", "%PDF-1.7\nfixture"u8.ToArray(), null, null, null,
            "INV-ORIGINAL-42");

        Assert.True(result.Validation.IsValid);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Content));
        XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        Assert.Equal("urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2", xml.Root!.Name.NamespaceName);
        Assert.Equal("INV-ORIGINAL-42", xml.Descendants(cbc + "ID")
            .First(x => x.Parent?.Name.LocalName == "InvoiceDocumentReference").Value);
        Assert.Single(xml.Descendants(cbc + "CreditedQuantity"));
        Assert.DoesNotContain(xml.Descendants(), x => x.Name.LocalName == "PaymentMeans");
    }

    [Fact]
    public void Possible_submission_is_reconciliation_only_and_cannot_be_blindly_retried()
    {
        var delivery = Delivery();
        delivery.StartParticipantVerification(Now);
        delivery.StartDocumentValidation(Now.AddSeconds(1));
        delivery.RecordDocumentHash(new string('c', 64), Now.AddSeconds(2));
        delivery.StartSubmission(Now.AddSeconds(3));
        delivery.RequireReconciliation("timeout", "The provider may have accepted the invoice.",
            Now.AddSeconds(4), Now.AddMinutes(1));

        Assert.True(delivery.ExternalSubmissionMayExist);
        Assert.Equal(CustomerInvoiceElectronicDeliveryStatuses.ReconciliationRequired, delivery.Status);
        Assert.Throws<InvalidOperationException>(() => delivery.RequestRetry(Now.AddMinutes(2)));
    }

    [Fact]
    public void Webhook_signatures_are_timestamp_bounded_and_compared_over_the_signed_data()
    {
        const string secret = "fixture-webhook-secret";
        const long timestamp = 1787745600;
        const string body = "{\"code\":\"issued_invoice.state_change\",\"data\":{\"invoice_id\":123,\"state\":\"sent\"}}";
        var data = "{\"invoice_id\":123,\"state\":\"sent\"}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{data}"))).ToLowerInvariant();
        var options = new B2BRouterOptions { WebhookSecret = secret, WebhookToleranceSeconds = 300 };
        var received = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

        Assert.True(B2BRouterCustomerInvoiceElectronicDeliveryProvider.VerifyWebhookSignature(
            $"t={timestamp},s={signature}", body, received, options, out _));
        Assert.False(B2BRouterCustomerInvoiceElectronicDeliveryProvider.VerifyWebhookSignature(
            $"t={timestamp},s={signature}", body, received.AddMinutes(6), options, out _));
        Assert.False(B2BRouterCustomerInvoiceElectronicDeliveryProvider.VerifyWebhookSignature(
            $"t={timestamp},s={new string('0', 64)}", body, received, options, out _));
    }

    [Fact]
    public async Task Persistence_has_company_filters_and_tenant_scoped_business_idempotency()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options);

        var deliveryType = db.Model.FindEntityType(typeof(CustomerInvoiceElectronicDelivery))!;
        var eventType = db.Model.FindEntityType(typeof(CustomerInvoiceElectronicDeliveryEvent))!;
        Assert.NotNull(deliveryType.GetQueryFilter());
        Assert.NotNull(eventType.GetQueryFilter());
        Assert.Contains(deliveryType.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CustomerInvoiceElectronicDelivery.CompanyId), nameof(CustomerInvoiceElectronicDelivery.IdempotencyKey)]));
        Assert.Contains(eventType.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CustomerInvoiceElectronicDeliveryEvent.CompanyId), nameof(CustomerInvoiceElectronicDeliveryEvent.ProviderKey), nameof(CustomerInvoiceElectronicDeliveryEvent.EventKey)]));
    }

    [Fact]
    public void Options_reject_cross_environment_hosts_and_unsigned_enabled_webhooks()
    {
        var validator = new B2BRouterOptionsValidator();
        var result = validator.Validate(null, new B2BRouterOptions
        {
            Enabled = true, Environment = B2BRouterOptions.SandboxEnvironment,
            ApiBaseUrl = "https://api.b2brouter.net/", ApiVersion = "2026-06-26",
            AccountId = "account", ApiKey = "secret", WebhooksEnabled = true
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, x => x.Contains("staging host", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, x => x.Contains("WebhookSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Company_connection_mapping_is_a_tenant_allowlist_and_disables_single_tenant_default()
    {
        var firstCompany = Guid.NewGuid();
        var secondCompany = Guid.NewGuid();
        var options = new B2BRouterOptions
        {
            AccountId = "single-tenant-default",
            CompanyAccountIds = new Dictionary<string, string>
            {
                [firstCompany.ToString("D")] = "first-company-account"
            }
        };

        Assert.Equal("first-company-account",
            B2BRouterCustomerInvoiceElectronicDeliveryProvider.ResolveAccountId(options, firstCompany));
        Assert.Null(B2BRouterCustomerInvoiceElectronicDeliveryProvider.ResolveAccountId(options, secondCompany));
    }

    [Theory]
    [InlineData("sending", CustomerInvoiceElectronicDeliveryOutcomes.Accepted, false)]
    [InlineData("sent", CustomerInvoiceElectronicDeliveryOutcomes.Delivered, true)]
    [InlineData("refused", CustomerInvoiceElectronicDeliveryOutcomes.Rejected, true)]
    [InlineData("unknown-provider-state", CustomerInvoiceElectronicDeliveryOutcomes.ReconciliationRequired, false)]
    public void Provider_acknowledgements_are_not_collapsed_into_http_success(
        string state, string expectedOutcome, bool expectedTerminal)
    {
        var status = B2BRouterCustomerInvoiceElectronicDeliveryProvider.MapProviderState("provider-1", state);

        Assert.Equal(expectedOutcome, status.Outcome);
        Assert.Equal(expectedTerminal, status.IsTerminal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "valid", false)]
    [InlineData(HttpStatusCode.Accepted, "pending", true)]
    [InlineData(HttpStatusCode.NotFound, "not_found", false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "invalid", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", true)]
    [InlineData(HttpStatusCode.BadGateway, "upstream_unavailable", true)]
    [InlineData(HttpStatusCode.Unauthorized, "credentials_invalid", false)]
    public void Directory_contract_classifies_participant_and_retry_evidence(
        HttpStatusCode statusCode, string expectedStatus, bool expectedRetryable)
    {
        var result = B2BRouterCustomerInvoiceElectronicDeliveryProvider.ClassifyParticipantResponse(
            statusCode, "0007", "5566778899");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedRetryable, result.IsRetryable);
    }

    [Fact]
    public void Enabled_provider_without_api_key_is_rejected_at_startup()
    {
        var result = new B2BRouterOptionsValidator().Validate(null, new B2BRouterOptions
        {
            Enabled = true,
            Environment = B2BRouterOptions.SandboxEnvironment,
            ApiBaseUrl = "https://api-staging.b2brouter.net/",
            ApiVersion = "2026-06-26",
            AccountId = "account"
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, x => x.Contains("ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "B2BRouterSandbox")]
    public async Task Sandbox_directory_and_schema_contract_runs_only_when_explicitly_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("B2BROUTER_INTEGRATION_TESTS_ENABLED"), "true",
                StringComparison.OrdinalIgnoreCase)) return;
        var apiKey = Environment.GetEnvironmentVariable("B2BROUTER_API_KEY");
        var participantScheme = Environment.GetEnvironmentVariable("B2BROUTER_TEST_PARTICIPANT_SCHEME");
        var participantIdentifier = Environment.GetEnvironmentVariable("B2BROUTER_TEST_PARTICIPANT_ID");
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(string.IsNullOrWhiteSpace(participantScheme));
        Assert.False(string.IsNullOrWhiteSpace(participantIdentifier));

        using var client = new HttpClient { BaseAddress = new("https://api-staging.b2brouter.net/") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-B2B-API-Key", apiKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-B2B-API-Version",
            B2BRouterOptions.VerifiedApiVersion);
        using var directory = await client.GetAsync(
            $"directory/{Uri.EscapeDataString(participantScheme!)}/{Uri.EscapeDataString(participantIdentifier!)}");
        Assert.True(directory.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,
            $"B2Brouter sandbox directory contract returned HTTP {(int)directory.StatusCode}.");

        var document = B2BRouterPeppolBisBillingDocument.Build(Snapshot("0007", "5566778899"), Delivery(),
            "INV-100.pdf", "%PDF-1.7\nfixture"u8.ToArray(), "SE3550000000054910000003",
            "Operating account", "ESSESESS");
        Assert.True(document.Validation.IsValid);
        using var content = new ByteArrayContent(document.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var validation = await client.PostAsync("documents/validate", content);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
    }

    private static CustomerInvoiceElectronicDelivery Delivery(string documentType = "invoice") => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), new string('b', 64), "b2brouter",
        B2BRouterOptions.PeppolBisBillingProfile, B2BRouterOptions.PeppolBisBillingVersion, "0007",
        "5566778899", documentType, "INV-100", "delivery-key", false, null, "Customer requested Peppol",
        Guid.NewGuid(), Now);

    private static string Snapshot(string scheme, string identifier) => $$"""
        {
          "schemaVersion":"native-customer-invoice-issue-2026.1",
          "documentNumber":"INV-100",
          "draft":{"documentType":"invoice","issueDate":"2026-08-26","dueDate":"2026-09-25","currency":"SEK","buyerReference":"BUYER-42","netTotal":100.00,"taxTotal":25.00,"grossTotal":125.00,"roundingAmount":0.00},
          "seller":{"legalName":"Seller AB","swedishOrganisationNumber":"556036-0793","vatRegistrationNumber":"SE556036079301","registeredAddressLine1":"Seller street 1","registeredPostalCode":"11122","registeredCity":"Stockholm","registeredCountryCode":"SE"},
          "buyer":{"legalName":"Buyer AB","vatIdentifier":"SE556677889901","billingAddressLine1":"Buyer street 2","billingPostalCode":"41110","billingCity":"Gothenburg","billingCountryCode":"SE","eInvoiceIdentifier":"{{identifier}}","eInvoiceIdentifierType":"{{scheme}}"},
          "lines":[{"sequence":1,"description":"Consulting","quantity":1,"unit":"hour","unitPrice":100.00,"discountPercent":0,"discountAmount":0,"netAmount":100.00,"taxRate":25,"taxAmount":25.00,"grossAmount":125.00}]
        }
        """;
}
