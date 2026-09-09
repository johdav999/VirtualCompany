using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesPresentationPreparationApiClientTests
{
    [Fact]
    public async Task Success_uses_company_transport_and_typed_invitation_route()
    {
        var companyId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            ResponseJson(companyId, invitationId, agentId));
        var client = CreateClient(handler);

        var result = await client.GetAsync(companyId, invitationId);

        Assert.NotNull(result);
        Assert.Equal("session_required", result!.ReadinessState);
        Assert.Equal(26_214_400, result.MaximumUploadBytes);
        Assert.Equal(agentId, Assert.Single(result.EligibleAgents).Id);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            $"/api/sales/meeting-invitations/{invitationId:D}/presentation-preparation",
            handler.Path);
        Assert.Equal(companyId.ToString(), handler.CompanyId);
        Assert.True(Guid.TryParseExact(handler.CorrelationId, "N", out _));
    }

    [Fact]
    public async Task Not_found_returns_null_and_problem_response_is_mapped()
    {
        var notFound = CreateClient(new RecordingHandler(HttpStatusCode.NotFound, string.Empty));
        Assert.Null(await notFound.GetAsync(Guid.NewGuid(), Guid.NewGuid()));

        const string problem =
            """{"status":403,"title":"Forbidden","detail":"Presentation preparation is not available.","code":"authorization.denied"}""";
        var forbidden = CreateClient(new RecordingHandler(HttpStatusCode.Forbidden, problem));

        var exception = await Assert.ThrowsAsync<SalesPresentationPreparationApiException>(() =>
            forbidden.GetAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Presentation preparation is not available.", exception.Message);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_transport()
    {
        var handler = new CancellingHandler();
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync(Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));

        Assert.True(handler.ObservedCancellation);
    }

    private static SalesPresentationPreparationApiClient CreateClient(HttpMessageHandler handler) =>
        new(
            new CompanyApiTransport(
                new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }),
            false,
            new ProblemResolver());

    private static string ResponseJson(Guid companyId, Guid invitationId, Guid agentId) => $$"""
        {
          "companyId":"{{companyId}}",
          "invitationId":"{{invitationId}}",
          "leadId":"{{Guid.NewGuid()}}",
          "leadTitle":"Welheld opportunity",
          "customerCompanyName":"Northstar AB",
          "meetingSessionId":null,
          "maximumUploadBytes":26214400,
          "invitation":{
            "id":"{{invitationId}}",
            "leadId":"{{Guid.NewGuid()}}",
            "dealId":null,
            "contactId":null,
            "title":"Welheld presentation",
            "startsUtc":"2026-09-15T09:00:00Z",
            "endsUtc":"2026-09-15T09:45:00Z",
            "timeZoneId":"Europe/Stockholm",
            "location":null,
            "createOnlineMeeting":true,
            "provider":"microsoft365",
            "status":"scheduled",
            "hasProviderEvent":true,
            "isEligibleForSessionCreation":true
          },
          "session":null,
          "eligibleAgents":[{
            "id":"{{agentId}}",
            "displayName":"Alex",
            "roleName":"Sales representative",
            "templateId":"alex-sales",
            "department":"Sales",
            "status":"active",
            "avatarUrl":null
          }],
          "decks":[],
          "activeDeckId":null,
          "activeDeckSlideCount":0,
          "readinessState":"session_required",
          "canOpenPresenter":false,
          "blockingReasons":[{
            "code":"session_missing",
            "explanation":"Create the meeting session."
          }],
          "allowedActions":["create_session"]
        }
        """;

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? CompanyId { get; private set; }
        public string? CorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            CorrelationId = request.Headers.GetValues("X-Correlation-Id").Single();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        public bool ObservedCancellation { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ObservedCancellation = cancellationToken.IsCancellationRequested;
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class ProblemResolver : IApiProblemMessageResolver
    {
        public string Resolve(ApiProblemResponse? problem, string fallbackMessage) =>
            problem?.Detail ?? fallbackMessage;
    }
}
