using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Observability;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class RateLimitingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RateLimitingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Task_endpoint_returns_429_with_problem_details_after_limit_is_exceeded()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory();
        using var client = CreateAuthenticatedClient(rateLimitedFactory, "rate-limit-user", "ratelimit@example.com", "Rate Limit User");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/onboarding/company", CreateCompanyRequest($"Rate Limited Company {attempt}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var throttledResponse = await client.PostAsJsonAsync("/api/onboarding/company", CreateCompanyRequest("Rate Limited Company 3"));

        Assert.Equal(HttpStatusCode.TooManyRequests, throttledResponse.StatusCode);
        Assert.True(throttledResponse.Headers.Contains("Retry-After"));
        Assert.True(throttledResponse.Headers.Contains("X-Correlation-ID"));

        using var payload = JsonDocument.Parse(await throttledResponse.Content.ReadAsStringAsync());
        Assert.Equal(429, payload.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal("/api/onboarding/company", payload.RootElement.GetProperty("instance").GetString());
        Assert.Equal("tasks", payload.RootElement.GetProperty("rateLimitPolicy").GetString());
        Assert.True(payload.RootElement.TryGetProperty("retryAfterSeconds", out var retryAfterSeconds));
        Assert.True(retryAfterSeconds.GetInt32() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("correlationId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Task_rate_limit_is_partitioned_by_authenticated_user()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory();
        using var firstClient = CreateAuthenticatedClient(rateLimitedFactory, "partition-a", "partition-a@example.com", "Partition A");
        using var secondClient = CreateAuthenticatedClient(rateLimitedFactory, "partition-b", "partition-b@example.com", "Partition B");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await firstClient.PostAsJsonAsync("/api/onboarding/company", CreateCompanyRequest($"Partition A {attempt}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var throttledResponse = await firstClient.PostAsJsonAsync("/api/onboarding/company", CreateCompanyRequest("Partition A throttled"));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttledResponse.StatusCode);

        var unaffectedResponse = await secondClient.PostAsJsonAsync("/api/onboarding/company", CreateCompanyRequest("Partition B allowed"));
        Assert.Equal(HttpStatusCode.OK, unaffectedResponse.StatusCode);
    }

    [Fact]
    public async Task Unprotected_and_health_endpoints_are_not_rate_limited()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory();
        using var client = CreateAuthenticatedClient(rateLimitedFactory, "health-user", "health@example.com", "Health User");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var templatesResponse = await client.GetAsync("/api/onboarding/templates");
            Assert.Equal(HttpStatusCode.OK, templatesResponse.StatusCode);

            var healthResponse = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        }
    }

    private WebApplicationFactory<Program> CreateRateLimitedFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Enabled"] = "true",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Chat:PermitLimit"] = "2",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Chat:WindowSeconds"] = "60",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Chat:QueueLimit"] = "0",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Tasks:PermitLimit"] = "2",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Tasks:WindowSeconds"] = "60",
                    [$"{ObservabilityOptions.SectionName}:RateLimiting:Tasks:QueueLimit"] = "0"
                });
            });
        });

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        string subject,
        string email,
        string displayName,
        string? provider = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);

        if (!string.IsNullOrWhiteSpace(provider))
        {
            client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.ProviderHeader, provider);
        }

        return client;
    }

    private static object CreateCompanyRequest(string name) => new
    {
        Name = name,
        Industry = "Technology",
        BusinessType = "Software Company",
        Timezone = "Europe/Stockholm",
        Currency = "SEK",
        Language = "sv-SE",
        ComplianceRegion = "EU",
        SelectedTemplateId = "saas-operations"
    };
}
