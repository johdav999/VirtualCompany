using System.Text.Json;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class HealthEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public HealthEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Liveness_endpoint_reports_only_application_process_health()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var payload = await JsonDocument.ParseAsync(stream);

        Assert.True(payload.RootElement.TryGetProperty("status", out var status));
        Assert.False(string.IsNullOrWhiteSpace(status.GetString()));

        var results = payload.RootElement.GetProperty("results");
        Assert.True(results.TryGetProperty("application", out _));
        Assert.Single(results.EnumerateObject());
        Assert.False(results.TryGetProperty("database", out _));
        Assert.False(results.TryGetProperty("redis", out _));
        Assert.False(results.TryGetProperty("object-storage", out _));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Readiness_endpoints_report_expected_dependency_checks(string path)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var payload = await JsonDocument.ParseAsync(stream);

        Assert.True(payload.RootElement.TryGetProperty("status", out var status));
        Assert.False(string.IsNullOrWhiteSpace(status.GetString()));

        var results = payload.RootElement.GetProperty("results");

        Assert.True(results.TryGetProperty("database", out _));
        Assert.True(results.TryGetProperty("redis", out _));
        Assert.True(results.TryGetProperty("object-storage", out _));
        Assert.False(results.TryGetProperty("application", out _));

        foreach (var result in results.EnumerateObject())
        {
            Assert.True(result.Value.TryGetProperty("status", out _));
            Assert.True(result.Value.TryGetProperty("duration", out _));
        }
    }
}
