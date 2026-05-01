using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxRealApiIntegrationTests
{
    [Fact]
    public async Task Live_company_information_read_runs_only_when_explicitly_enabled()
    {
        if (!RealFortnoxEnvironment.IsEnabled)
        {
            return;
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = FortnoxApiClient.NormalizeBaseAddress(RealFortnoxEnvironment.ApiBaseUrl)
        };
        var client = new FortnoxApiClient(
            httpClient,
            new StaticTokenOAuthService(RealFortnoxEnvironment.AccessToken),
            Options.Create(new FortnoxOptions
            {
                Enabled = true,
                ApiBaseUrl = RealFortnoxEnvironment.ApiBaseUrl,
                ApiMaxRetries = 1,
                ApiRetryBaseDelayMilliseconds = 100,
                ApiMaxRetryDelaySeconds = 1
            }).ToMonitor(),
            NullLogger<FortnoxApiClient>.Instance,
            TimeProvider.System);

        var result = await client.GetCompanyInformationAsync(
            new FortnoxRequestContext(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), correlationId: "fortnox-live-test"),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.CompanyName));
    }

    private static class RealFortnoxEnvironment
    {
        public static bool IsEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("FORTNOX_INTEGRATION_TESTS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(AccessToken);

        public static string AccessToken =>
            Environment.GetEnvironmentVariable("FORTNOX_ACCESS_TOKEN") ?? string.Empty;

        public static string ApiBaseUrl =>
            Environment.GetEnvironmentVariable("FORTNOX_API_BASE_URL") ?? "https://api.fortnox.se/3";
    }

    private sealed class StaticTokenOAuthService(string accessToken) : IFortnoxOAuthService
    {
        public Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(CompleteFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(RefreshFortnoxAccessTokenCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(FortnoxAccessTokenResult.Success(accessToken, DateTime.UtcNow.AddMinutes(30)));

        public Task<FortnoxConnectionStatusResult> GetStatusAsync(GetFortnoxConnectionStatusQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

internal static class OptionsMonitorTestExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options) =>
        new StaticOptionsMonitor<T>(options.Value);

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
