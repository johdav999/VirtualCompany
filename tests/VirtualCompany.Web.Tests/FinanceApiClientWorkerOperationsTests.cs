using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientWorkerOperationsTests
{
    [Fact]
    public async Task Worker_recovery_client_uses_company_scoped_status_and_action_routes()
    {
        var companyId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: false);
        var action = new FinanceWorkerOperatorActionApiRequest(4, "Operator reviewed the durable attempt evidence.", "corr-operator");

        await client.GetWorkerOperationsAsync(companyId, "needs_attention", "finance-seed", 10, 25);
        await client.RetryWorkerExecutionAsync(companyId, executionId, action);
        await client.StopWorkerExecutionAsync(companyId, executionId, action);
        await client.AcknowledgeWorkerExecutionAsync(companyId, executionId, action);

        Assert.Collection(transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"api/companies/{companyId}/finance/worker-operations?status=needs_attention&workerKey=finance-seed&skip=10&take=25"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/retry"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/stop"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/acknowledge"));
    }

    [Fact]
    public async Task Worker_recovery_mutations_are_blocked_in_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() => client.RetryWorkerExecutionAsync(Guid.NewGuid(), Guid.NewGuid(),
            new FinanceWorkerOperatorActionApiRequest(0, "Retry reviewed work.")));
    }

    private static void AssertRequest(RecordedRequest request, Guid companyId, HttpMethod method, string uri)
    {
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(companyId, method, uri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri);
}
