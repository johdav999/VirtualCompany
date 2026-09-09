using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingSessionApiClientTests
{
    [Fact]
    public async Task Client_uses_company_transport_and_typed_session_routes()
    {
        var companyId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler(companyId, invitationId, sessionId);
        var client = CreateClient(handler);

        var created = await client.CreateOrUpdateAsync(
            companyId,
            invitationId,
            new("Confirm fit", "Finance leadership", 45, null, "pending", "standard", 365));
        var byInvitation = await client.GetByInvitationAsync(companyId, invitationId);
        var byId = await client.GetAsync(companyId, sessionId);
        var transitioned = await client.TransitionAsync(
            companyId,
            sessionId,
            new("presenting", created.ConcurrencyVersion, 1, 2));

        Assert.Equal(sessionId, created.Id);
        Assert.Equal(sessionId, byInvitation!.Id);
        Assert.Equal(sessionId, byId!.Id);
        Assert.Equal("presenting", transitioned.Status);
        Assert.Collection(
            handler.Requests,
            request => AssertRequest(request, HttpMethod.Put, $"/api/sales/meeting-invitations/{invitationId:D}/session", companyId),
            request => AssertRequest(request, HttpMethod.Get, $"/api/sales/meeting-invitations/{invitationId:D}/session", companyId),
            request => AssertRequest(request, HttpMethod.Get, $"/api/sales/meeting-sessions/{sessionId:D}", companyId),
            request => AssertRequest(request, HttpMethod.Post, $"/api/sales/meeting-sessions/{sessionId:D}/transitions", companyId));
        Assert.All(handler.Requests, request =>
            Assert.True(Guid.TryParseExact(
                request.Headers.GetValues("X-Correlation-Id").Single(), "N", out _)));
    }

    [Fact]
    public async Task Read_returns_null_for_not_found_without_disclosing_data()
    {
        var client = CreateClient(new StaticHandler(HttpStatusCode.NotFound, string.Empty));

        Assert.Null(await client.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Null(await client.GetByInvitationAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Conflict_preserves_status_and_safe_problem_message()
    {
        const string problem = """
            {"status":409,"title":"Sales meeting session conflict","detail":"Refresh the meeting session.","code":"sales.meeting_session.conflict"}
            """;
        var client = CreateClient(new StaticHandler(HttpStatusCode.Conflict, problem));

        var exception = await Assert.ThrowsAsync<SalesMeetingSessionApiException>(() =>
            client.TransitionAsync(
                Guid.NewGuid(), Guid.NewGuid(), new("presenting", 1)));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("Refresh the meeting session.", exception.Message);
    }

    [Fact]
    public async Task Empty_company_is_rejected_before_transport()
    {
        var handler = new StaticHandler(HttpStatusCode.OK, SessionJson(Guid.NewGuid(), Guid.NewGuid()));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetAsync(Guid.Empty, Guid.NewGuid()));

        Assert.Equal(0, handler.CallCount);
    }

    private static SalesMeetingSessionApiClient CreateClient(HttpMessageHandler handler) =>
        new(
            new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }),
            false,
            new ProblemResolver());

    private static void AssertRequest(
        HttpRequestMessage request,
        HttpMethod method,
        string path,
        Guid companyId)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal(companyId.ToString(), request.Headers.GetValues("X-Company-Id").Single());
    }

    private static string SessionJson(Guid companyId, Guid sessionId, string status = "ready", long version = 1) => $$"""
        {
          "id":"{{sessionId}}","companyId":"{{companyId}}","invitationId":"{{Guid.NewGuid()}}",
          "leadId":"{{Guid.NewGuid()}}","dealId":null,"contactId":"{{Guid.NewGuid()}}",
          "customerCompanyId":"{{Guid.NewGuid()}}","meetingGoal":"Confirm fit",
          "intendedAudience":"Finance leadership","plannedDurationMinutes":45,"demoScenario":null,
          "providerMeetingId":"provider-event","status":"{{status}}","currentSlideIndex":1,
          "currentTalkingPointIndex":2,"resumeMarker":null,"consentStatus":"pending",
          "consentRecordedUtc":"2026-09-03T10:00:00Z","consentRecordedByUserId":"{{Guid.NewGuid()}}",
          "retentionPolicy":"standard","retentionDays":365,"retentionStartsUtc":"2026-09-04T10:45:00Z",
          "retentionUntilUtc":"2027-09-04T10:45:00Z","statusReason":null,"endedUtc":null,
          "createdByUserId":"{{Guid.NewGuid()}}","updatedByUserId":"{{Guid.NewGuid()}}",
          "createdUtc":"2026-09-03T10:00:00Z","updatedUtc":"2026-09-03T10:00:00Z",
          "concurrencyVersion":{{version}}
        }
        """;

    private sealed class RecordingHandler(Guid companyId, Guid invitationId, Guid sessionId) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var status = "ready";
            var version = 1L;
            if (request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("Confirm fit", body.RootElement.GetProperty("meetingGoal").GetString());
                Assert.Equal(365, body.RootElement.GetProperty("retentionDays").GetInt32());
            }
            else if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                status = body.RootElement.GetProperty("targetStatus").GetString()!;
                version = 2;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    SessionJson(companyId, sessionId, status, version),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class StaticHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ProblemResolver : IApiProblemMessageResolver
    {
        public string Resolve(ApiProblemResponse? problem, string fallbackMessage) =>
            problem?.Detail ?? fallbackMessage;
    }
}
