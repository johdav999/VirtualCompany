using System.Diagnostics;
using System.Net.Http.Headers;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitPerformanceTests
{
    private const double CachedRequestP95TargetMilliseconds = 2500;
    private const double InvalidationLagTargetMilliseconds = 60000;

    [Fact]
    public async Task Staging_cockpit_cached_requests_meet_p95_target_when_perf_endpoint_is_configured()
    {
        var settings = ExecutiveCockpitPerformanceSettings.FromEnvironment();
        if (settings is null)
        {
            return;
        }

        using var client = CreateClient(settings);
        await WarmCacheAsync(client, settings);

        var dashboardSamples = await MeasureAsync(
            settings.SampleCount,
            () => client.GetAsync($"/api/companies/{settings.CompanyId}/executive-cockpit"));
        var widgetSamples = await MeasureAsync(
            settings.SampleCount,
            () => client.GetAsync($"/api/companies/{settings.CompanyId}/executive-cockpit/widgets/summary-kpis"));

        var dashboardP95 = Percentile(dashboardSamples, 0.95);
        var widgetP95 = Percentile(widgetSamples, 0.95);

        Assert.True(
            dashboardP95 <= CachedRequestP95TargetMilliseconds,
            $"Executive cockpit cached dashboard p95 was {dashboardP95:0.0} ms; target is {CachedRequestP95TargetMilliseconds:0.0} ms.");
        Assert.True(
            widgetP95 <= CachedRequestP95TargetMilliseconds,
            $"Executive cockpit cached widget p95 was {widgetP95:0.0} ms; target is {CachedRequestP95TargetMilliseconds:0.0} ms.");
    }

    [Fact]
    public async Task Staging_cockpit_invalidation_is_visible_within_sla_when_probe_is_configured()
    {
        var settings = ExecutiveCockpitPerformanceSettings.FromEnvironment();
        if (settings is null || string.IsNullOrWhiteSpace(settings.InvalidationProbePath))
        {
            return;
        }

        using var client = CreateClient(settings);
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.PostAsync(settings.InvalidationProbePath, content: null);
        response.EnsureSuccessStatusCode();

        while (stopwatch.Elapsed.TotalMilliseconds <= InvalidationLagTargetMilliseconds)
        {
            using var dashboardResponse = await client.GetAsync($"/api/companies/{settings.CompanyId}/executive-cockpit");
            dashboardResponse.EnsureSuccessStatusCode();
            if (dashboardResponse.Headers.TryGetValues("X-Executive-Cockpit-Cache", out var values) &&
                values.Any(value => string.Equals(value, "miss", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.Fail($"Executive cockpit invalidation was not visible within {InvalidationLagTargetMilliseconds:0} ms.");
    }

    private static async Task WarmCacheAsync(HttpClient client, ExecutiveCockpitPerformanceSettings settings)
    {
        using var dashboardResponse = await client.GetAsync($"/api/companies/{settings.CompanyId}/executive-cockpit");
        dashboardResponse.EnsureSuccessStatusCode();
        using var widgetResponse = await client.GetAsync($"/api/companies/{settings.CompanyId}/executive-cockpit/widgets/summary-kpis");
        widgetResponse.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<double>> MeasureAsync(int sampleCount, Func<Task<HttpResponseMessage>> request)
    {
        var samples = new List<double>(sampleCount);
        for (var index = 0; index < sampleCount; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await request();
            stopwatch.Stop();
            response.EnsureSuccessStatusCode();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return samples;
    }

    private static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        var ordered = samples.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static HttpClient CreateClient(ExecutiveCockpitPerformanceSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = settings.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(settings.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.BearerToken);
        }

        if (!string.IsNullOrWhiteSpace(settings.DevSubject))
        {
            client.DefaultRequestHeaders.Add("X-Dev-User-Subject", settings.DevSubject);
            client.DefaultRequestHeaders.Add("X-Dev-User-Email", settings.DevEmail ?? $"{settings.DevSubject}@example.com");
            client.DefaultRequestHeaders.Add("X-Dev-User-DisplayName", settings.DevDisplayName ?? "Cockpit Perf User");
        }

        return client;
    }

    private sealed record ExecutiveCockpitPerformanceSettings(
        Uri BaseAddress,
        Guid CompanyId,
        int SampleCount,
        string? BearerToken,
        string? DevSubject,
        string? DevEmail,
        string? DevDisplayName,
        string? InvalidationProbePath)
    {
        public static ExecutiveCockpitPerformanceSettings? FromEnvironment()
        {
            var baseUrl = Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_BASE_URL");
            var companyIdValue = Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_COMPANY_ID");
            if (string.IsNullOrWhiteSpace(baseUrl) || !Guid.TryParse(companyIdValue, out var companyId))
            {
                return null;
            }

            var sampleCount = int.TryParse(Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_SAMPLE_COUNT"), out var parsedSampleCount)
                ? Math.Clamp(parsedSampleCount, 5, 500)
                : 30;

            return new ExecutiveCockpitPerformanceSettings(
                new Uri(baseUrl, UriKind.Absolute),
                companyId,
                sampleCount,
                Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_BEARER_TOKEN"),
                Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_DEV_SUBJECT"),
                Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_DEV_EMAIL"),
                Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_DEV_DISPLAY_NAME"),
                Environment.GetEnvironmentVariable("EXECUTIVE_COCKPIT_PERF_INVALIDATION_PROBE_PATH"));
        }
    }
}