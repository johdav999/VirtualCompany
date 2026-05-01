using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Api.Tests;

internal static class FortnoxApiClientTestFactory
{
    public static FortnoxApiClient Create(CapturingHandler handler, IFortnoxWriteApprovalService? approvalService = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = FortnoxApiClient.NormalizeBaseAddress("https://api.fortnox.se/3")
        };

        return new FortnoxApiClient(
            httpClient,
            new StaticOAuthService(),
            new StaticOptionsMonitor<FortnoxOptions>(new FortnoxOptions
            {
                Enabled = true,
                ApiBaseUrl = "https://api.fortnox.se/3",
                ApiMaxRetries = 1,
                ApiRetryBaseDelayMilliseconds = 1,
                ApiMaxRetryDelaySeconds = 1
            }),
            NullLogger<FortnoxApiClient>.Instance,
            TimeProvider.System,
            approvalService);
    }

    private sealed class StaticOAuthService : IFortnoxOAuthService
    {
        public Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(CompleteFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(RefreshFortnoxAccessTokenCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(FortnoxAccessTokenResult.Success("access-token", DateTime.UtcNow.AddHours(1)));

        public Task<FortnoxConnectionStatusResult> GetStatusAsync(GetFortnoxConnectionStatusQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public CapturingHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(CloneRequest(request));
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
