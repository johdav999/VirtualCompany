using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientConversationRunTests
{
    [Fact]
    public async Task Conversation_run_client_uses_typed_company_scoped_routes_and_versioned_confirmation()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var transport = new RecordingTransport(runId, companyId, agentId);
        var client = new FinanceApiClient(transport);

        await client.StartConversationRunAsync(companyId, agentId,
            new("Review overdue invoices", "request-1", References: [new("invoice", Guid.NewGuid().ToString("D"))]));
        await client.GetConversationRunAsync(companyId, agentId, runId);
        await client.ListConversationRunsAsync(companyId, agentId, 250);
        await client.ConfirmConversationRunStepAsync(companyId, agentId, runId, "step/1", 7);
        await client.CancelConversationRunAsync(companyId, agentId, runId, "Cancelled after review");
        await client.SupersedeConversationRunAsync(companyId, agentId, runId,
            new("Review invoices for August", "request-2", "Clarified period"));

        Assert.Collection(transport.Requests,
            x => AssertRequest(x, companyId, HttpMethod.Post, Base(companyId, agentId)),
            x => AssertRequest(x, companyId, HttpMethod.Get, $"{Base(companyId, agentId)}/{runId:D}"),
            x => AssertRequest(x, companyId, HttpMethod.Get, $"{Base(companyId, agentId)}?take=100"),
            x =>
            {
                AssertRequest(x, companyId, HttpMethod.Post, $"{Base(companyId, agentId)}/{runId:D}/steps/step%2F1/confirm");
                Assert.Contains("\"expectedStepVersion\":7", x.Body, StringComparison.Ordinal);
            },
            x =>
            {
                AssertRequest(x, companyId, HttpMethod.Post, $"{Base(companyId, agentId)}/{runId:D}/cancel");
                Assert.Contains("Cancelled after review", x.Body, StringComparison.Ordinal);
            },
            x => AssertRequest(x, companyId, HttpMethod.Post, $"{Base(companyId, agentId)}/{runId:D}/supersede"));
    }

    [Fact]
    public async Task Conversation_run_mutations_are_rejected_offline_and_reads_return_no_runs()
    {
        var client = new FinanceApiClient(new RecordingTransport(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), useOfflineMode: true);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        Assert.Null(await client.GetConversationRunAsync(companyId, agentId, Guid.NewGuid()));
        Assert.Null(await client.ListConversationRunsAsync(companyId, agentId));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.StartConversationRunAsync(
            companyId, agentId, new("Review cash", "offline-request")));
        await Assert.ThrowsAsync<FinanceApiException>(() => client.ConfirmConversationRunStepAsync(
            companyId, agentId, Guid.NewGuid(), "step-1", 1));
    }

    private static string Base(Guid companyId, Guid agentId) =>
        $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs";

    private static void AssertRequest(RecordedRequest request, Guid companyId, HttpMethod method, string uri)
    {
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.Uri);
    }

    private sealed class RecordingTransport(Guid runId, Guid companyId, Guid agentId) : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public async Task<HttpResponseMessage> SendAsync(Guid scopedCompanyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new(scopedCompanyId, method, uri,
                content is null ? string.Empty : await content.ReadAsStringAsync(cancellationToken)));
            var run = $$"""
                {"id":"{{runId:D}}","contractVersion":"finance-conversation-run-v1","companyId":"{{companyId:D}}","agentId":"{{agentId:D}}","initiatingUserId":"{{Guid.NewGuid():D}}","idempotencyKey":"request-1","correlationId":"correlation-1","status":"awaiting_confirmation","safeSummary":"Review the exact effect.","finalOutcomeCode":null,"supersededByRunId":null,"cancelledUtc":null,"leaseExpiresUtc":null,"retainUntilUtc":"2026-12-01T00:00:00Z","redactedUtc":null,"version":2,"revisions":[],"steps":[],"createdUtc":"2026-09-01T08:00:00Z","updatedUtc":"2026-09-01T08:01:00Z","completedUtc":null}
                """;
            var response = method == HttpMethod.Get && uri.Contains("?take=", StringComparison.Ordinal)
                ? $"{{\"items\":[{run}],\"totalCount\":1}}"
                : run;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri, string Body);
}
