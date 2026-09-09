using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class DemoScenarioApiClientTests
{
    [Fact]
    public async Task Client_uses_company_scoped_typed_control_routes()
    {
        var companyId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var meetingId = Guid.NewGuid();
        var handler = new RecordingHandler(companyId, runId);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new DemoScenarioApiClient(http, new CompanyApiTransport(http), false);

        await client.GetCurrentAsync(companyId);
        await client.LinkMeetingAsync(companyId, new(meetingId, "northstar-sales", 1));
        await client.StartAsync(companyId);
        var preview = await client.PreviewResetAsync(companyId, "northstar-sales", 1);
        await client.ResetAsync(companyId, new("northstar-sales", 1, "Northstar Demo Company", preview!.PreviewToken));
        var executed = await client.ExecuteAsync(companyId, new("demo.sales.qualify_lead", "step-1"));

        Assert.Equal("demo.sales.qualify_lead", executed!.CommandName);
        Assert.Collection(handler.Requests,
            request => AssertRequest(request, HttpMethod.Get, "/api/demo-scenarios/current", companyId),
            request => AssertRequest(request, HttpMethod.Post, "/api/demo-scenarios/current/link-meeting", companyId),
            request => AssertRequest(request, HttpMethod.Post, "/api/demo-scenarios/current/start", companyId),
            request => AssertRequest(request, HttpMethod.Get, "/api/demo-scenarios/current/reset-preview", companyId),
            request => AssertRequest(request, HttpMethod.Post, "/api/demo-scenarios/current/reset", companyId),
            request => AssertRequest(request, HttpMethod.Post, "/api/demo-scenarios/current/commands", companyId));
        Assert.All(handler.Requests, request => Assert.True(Guid.TryParseExact(
            request.Headers.GetValues("X-Correlation-Id").Single(), "N", out _)));
    }

    [Fact]
    public async Task Offline_mode_never_substitutes_mock_demo_data()
    {
        var handler = new RecordingHandler(Guid.NewGuid(), Guid.NewGuid());
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new DemoScenarioApiClient(http, new CompanyApiTransport(http), true);

        var exception = await Assert.ThrowsAsync<DemoScenarioApiException>(() => client.GetCurrentAsync(Guid.NewGuid()));

        Assert.Contains("never substitutes mock demo data", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private static void AssertRequest(HttpRequestMessage request, HttpMethod method, string path, Guid companyId)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal(companyId.ToString(), request.Headers.GetValues("X-Company-Id").Single());
    }

    private sealed class RecordingHandler(Guid companyId, Guid runId) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var status = StatusJson();
            var path = request.RequestUri!.AbsolutePath;
            string response;
            if (path.EndsWith("/reset-preview", StringComparison.Ordinal))
            {
                response = $$"""
                    {"companyId":"{{companyId}}","companyName":"Northstar Demo Company","runId":"{{runId}}",
                     "scenarioKey":"northstar-sales","scenarioVersion":1,"resetGeneration":1,
                     "affectedRecords":[],"disabledIntegrations":["Email delivery"],"validations":[],
                     "expectedPostResetInvariants":[],"auditHistoryPreserved":true,"canReset":true,"previewToken":"token"}
                    """;
            }
            else if (path.EndsWith("/commands", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("demo.sales.qualify_lead", body.RootElement.GetProperty("commandName").GetString());
                response = $$"""
                    {"disposition":"executed","reasonCode":null,"message":"done","commandName":"demo.sales.qualify_lead",
                     "stepNumber":1,"expectedVisibleOutcome":"Lead qualified","entityType":"lead","entityId":"{{Guid.NewGuid()}}","status":{{status}}}
                    """;
            }
            else
            {
                response = status;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }

        private string StatusJson() => $$"""
            {"companyId":"{{companyId}}","companyName":"Northstar Demo Company","runId":"{{runId}}",
             "scenarioKey":"northstar-sales","scenarioVersion":1,"meetingSessionId":null,"status":"ready",
             "currentStep":0,"stepCount":3,"resetGeneration":1,"externalIntegrationsBlocked":true,
             "nextAction":null,"actions":[],"validations":[],"updatedUtc":"2026-01-15T09:00:00Z"}
            """;
    }
}
