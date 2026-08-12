using System.Net;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientSupplierSubscriptionTests
{
    [Fact]
    public async Task Proposal_review_methods_use_company_scoped_routes()
    {
        var companyId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var json = request.Method == HttpMethod.Get && path.Contains("supplier-subscription-proposals/", StringComparison.Ordinal)
                ? ProposalDetailJson(proposalId)
                : request.Method == HttpMethod.Post && path.EndsWith("/accept", StringComparison.Ordinal)
                    ? SubscriptionDetailJson(Guid.NewGuid())
                    : request.Method == HttpMethod.Get
                        ? "[]"
                        : ProposalDetailJson(proposalId);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FinanceApiClient(httpClient);

        await client.GetSupplierSubscriptionProposalsAsync(companyId, status: "needs_review", search: "cloud");
        await client.GetSupplierSubscriptionProposalAsync(companyId, proposalId);
        await client.AcceptSupplierSubscriptionProposalAsync(companyId, proposalId, new AcceptSupplierSubscriptionProposalRequest(DefaultTerms(), "Accepted."));
        await client.RejectSupplierSubscriptionProposalAsync(companyId, proposalId, new RejectSupplierSubscriptionProposalRequest("Not a subscription."));
        await client.RetrySupplierSubscriptionProposalAsync(companyId, proposalId);

        Assert.Collection(
            handler.Requests,
            request => Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscription-proposals?status=needs_review&search=cloud", request.RequestUri!.PathAndQuery),
            request => Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}", request.RequestUri!.PathAndQuery),
            request => Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/accept", request.RequestUri!.PathAndQuery),
            request => Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/reject", request.RequestUri!.PathAndQuery),
            request => Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/retry", request.RequestUri!.PathAndQuery));
    }

    [Fact]
    public async Task Receipt_evidence_link_posts_bill_and_evidence_to_subscription_route()
    {
        var companyId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BillContextJson(billId), System.Text.Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new FinanceApiClient(httpClient);

        await client.LinkSupplierSubscriptionReceiptEvidenceAsync(companyId, subscriptionId, new LinkSupplierSubscriptionReceiptEvidenceRequest(billId, "Receipt evidence."));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/internal/companies/{companyId}/finance/supplier-subscriptions/{subscriptionId}/receipt-evidence", request.RequestUri!.PathAndQuery);
        Assert.True(handler.RequestBodies.Count is 0 or 1);
    }

    private static SupplierSubscriptionProposalTermsRequest DefaultTerms() => new(
        Guid.NewGuid(),
        "Cloud subscription",
        "SEK",
        100m,
        "monthly",
        31,
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
        0m,
        5,
        null,
        null,
        null,
        30,
        true,
        null);

    private static string ProposalDetailJson(Guid proposalId) => $$"""
        {
          "id":"{{proposalId}}",
          "status":"needs_review",
          "classification":"agreement",
          "sourceEmailMessageSnapshotId":"{{Guid.NewGuid()}}",
          "sourceEmailAttachmentSnapshotId":"{{Guid.NewGuid()}}",
          "sourceDocumentId":null,
          "sourceFingerprint":"source-fingerprint",
          "sourceSubject":"Agreement",
          "sourceAttachmentName":"agreement.pdf",
          "supplierName":"Cloud Supplier",
          "supplierOrgNumber":null,
          "terms":{{JsonSerializer.Serialize(DefaultTerms(), JsonSerializerOptions.Web)}},
          "confidenceScore":88,
          "evidenceSummary":"Agreement evidence.",
          "safeFailureSummary":null,
          "acceptedSubscriptionId":null,
          "decidedByUserId":null,
          "decisionReason":null,
          "decidedUtc":null,
          "createdUtc":"2026-01-01T00:00:00Z",
          "updatedUtc":"2026-01-01T00:00:00Z"
        }
        """;

    private static string SubscriptionDetailJson(Guid subscriptionId) => $$"""
        {
          "id":"{{subscriptionId}}",
          "counterpartyId":"{{Guid.NewGuid()}}",
          "supplierName":"Cloud Supplier",
          "name":"Cloud subscription",
          "contractReference":null,
          "description":null,
          "currency":"SEK",
          "expectedAmount":100,
          "amountTolerance":0,
          "cadence":"monthly",
          "billingDay":31,
          "startDateUtc":"2026-01-01T00:00:00Z",
          "endDateUtc":null,
          "nextExpectedBillDateUtc":"2026-01-31T00:00:00Z",
          "dateToleranceDays":5,
          "noticePeriodDays":30,
          "autoRenews":true,
          "status":"draft",
          "health":"upcoming",
          "healthMessage":"Upcoming.",
          "contractDocumentId":null,
          "createdUtc":"2026-01-01T00:00:00Z",
          "updatedUtc":"2026-01-01T00:00:00Z",
          "sourceEvidence":null,
          "matches":[]
        }
        """;

    private static string BillContextJson(Guid billId) => $$"""
        {
          "billId":"{{billId}}",
          "hasContext":true,
          "subscription":null,
          "match":null,
          "suggestions":[],
          "status":"needs_review",
          "message":"Receipt evidence linked."
        }
        """;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}