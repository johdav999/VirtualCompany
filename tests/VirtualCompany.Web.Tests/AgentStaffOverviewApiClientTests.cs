using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentStaffOverviewApiClientTests
{
    [Fact]
    public async Task GetAsync_uses_company_scoped_endpoint_and_selected_period()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport(new AgentStaffOverviewViewModel
        {
            CompanyId = companyId,
            CompanyName = "Example Company"
        });
        var client = new AgentStaffOverviewApiClient(transport, false, new FallbackProblemResolver());

        var result = await client.GetAsync(companyId, 2026, 7);

        Assert.NotNull(result);
        Assert.Equal(companyId, transport.CompanyId);
        Assert.Equal(HttpMethod.Get, transport.Method);
        Assert.Equal($"api/companies/{companyId:D}/executive-cockpit/agent-staff?year=2026&month=7", transport.Uri);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_access_is_forbidden()
    {
        var transport = new RecordingTransport(HttpStatusCode.Forbidden);
        var client = new AgentStaffOverviewApiClient(transport, false, new FallbackProblemResolver());

        var result = await client.GetAsync(Guid.NewGuid(), 2026, 7);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_omits_period_when_the_server_should_resolve_the_latest_activity_month()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport(new AgentStaffOverviewViewModel { CompanyId = companyId });
        var client = new AgentStaffOverviewApiClient(transport, false, new FallbackProblemResolver());

        var result = await client.GetAsync(companyId);

        Assert.NotNull(result);
        Assert.Equal($"api/companies/{companyId:D}/executive-cockpit/agent-staff", transport.Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        private readonly HttpStatusCode _statusCode;
        private readonly AgentStaffOverviewViewModel? _payload;

        public RecordingTransport(AgentStaffOverviewViewModel payload)
        {
            _payload = payload;
            _statusCode = HttpStatusCode.OK;
        }

        public RecordingTransport(HttpStatusCode statusCode) => _statusCode = statusCode;

        public Uri? BaseAddress => new("http://localhost/");
        public Guid CompanyId { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }

        public Task<HttpResponseMessage> SendAsync(
            Guid companyId,
            HttpMethod method,
            string uri,
            HttpContent? content,
            CancellationToken cancellationToken)
        {
            CompanyId = companyId;
            Method = method;
            Uri = uri;
            var response = new HttpResponseMessage(_statusCode);
            if (_payload is not null)
            {
                response.Content = JsonContent.Create(_payload);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FallbackProblemResolver : IApiProblemMessageResolver
    {
        public string Resolve(ApiProblemResponse? problem, string fallbackMessage) => fallbackMessage;
    }
}
