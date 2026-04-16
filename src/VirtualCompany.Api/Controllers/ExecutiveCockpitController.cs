using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/executive-cockpit")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ExecutiveCockpitController : ControllerBase
{
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.ExecutiveCockpit.Api");
    private static readonly Meter Meter = new("VirtualCompany.ExecutiveCockpit.Api");
    private static readonly Histogram<double> EndpointLatency = Meter.CreateHistogram<double>("executive_cockpit_endpoint_latency_ms", "ms");
    private static readonly Histogram<long> EndpointPayloadSize = Meter.CreateHistogram<long>("executive_cockpit_endpoint_payload_bytes", "By");

    private readonly IExecutiveCockpitDashboardService _dashboardService;
    private readonly IExecutiveCockpitKpiQueryService _kpiQueryService;
    private readonly IDepartmentDashboardConfigurationService _departmentDashboardConfigurationService;
    private readonly ILogger<ExecutiveCockpitController> _logger;

    public ExecutiveCockpitController(
        IExecutiveCockpitDashboardService dashboardService,
        IExecutiveCockpitKpiQueryService kpiQueryService,
        IDepartmentDashboardConfigurationService departmentDashboardConfigurationService,
        ILogger<ExecutiveCockpitController> logger)
    {
        _dashboardService = dashboardService;
        _kpiQueryService = kpiQueryService;
        _departmentDashboardConfigurationService = departmentDashboardConfigurationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ExecutiveCockpitDashboardDto>> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("executive_cockpit.endpoint.dashboard");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _dashboardService.GetAsync(new GetExecutiveCockpitDashboardQuery(companyId), cancellationToken);
            var cacheOutcome = result.CacheTimestampUtc is null ? "miss" : "hit";
            LogEndpointTelemetry("dashboard", stopwatch.Elapsed, "success", cacheOutcome, result);
            activity?.SetTag("vc.cockpit.endpoint", "dashboard");
            activity?.SetTag("vc.cockpit.cache.outcome", cacheOutcome);
            activity?.SetTag("vc.cockpit.status", "success");
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            LogEndpointTelemetry("dashboard", TimeSpan.Zero, "forbidden", "unknown", null);
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("composition")]
    public Task<ActionResult<DepartmentDashboardConfigurationDto>> GetCompositionAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        GetDepartmentSectionsCoreAsync(companyId, cancellationToken);

    [HttpGet("department-sections")]
    public async Task<ActionResult<DepartmentDashboardConfigurationDto>> GetDepartmentSectionsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await GetDepartmentSectionsCoreAsync(companyId, cancellationToken);

    private async Task<ActionResult<DepartmentDashboardConfigurationDto>> GetDepartmentSectionsCoreAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _departmentDashboardConfigurationService.GetAsync(new GetDepartmentDashboardConfigurationQuery(companyId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("widgets/{widgetKey}")]
    public async Task<ActionResult<ExecutiveCockpitWidgetPayloadDto>> GetWidgetAsync(
        Guid companyId,
        string widgetKey,
        [FromQuery] string? department,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("executive_cockpit.endpoint.widget");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _dashboardService.GetWidgetAsync(
                new GetExecutiveCockpitWidgetPayloadQuery(companyId, widgetKey, department, startUtc, endUtc),
                cancellationToken);
            var normalizedWidget = NormalizeTag(widgetKey);
            var cacheOutcome = result.CacheTimestampUtc is null ? "miss" : "hit";
            LogEndpointTelemetry("widget", stopwatch.Elapsed, "success", cacheOutcome, result, normalizedWidget);
            activity?.SetTag("vc.cockpit.endpoint", "widget");
            activity?.SetTag("vc.cockpit.widget", normalizedWidget);
            activity?.SetTag("vc.cockpit.cache.outcome", cacheOutcome);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("kpis")]
    public async Task<ActionResult<ExecutiveCockpitKpiDashboardDto>> GetKpisAsync(
        Guid companyId,
        [FromQuery] string? department,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("executive_cockpit.endpoint.kpis");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _kpiQueryService.GetAsync(
                new GetExecutiveCockpitKpiDashboardQuery(companyId, department, startUtc, endUtc),
                cancellationToken);
            const string cacheOutcome = "n/a";
            LogEndpointTelemetry("kpis", stopwatch.Elapsed, "success", cacheOutcome, result);
            activity?.SetTag("vc.cockpit.endpoint", "kpis");
            activity?.SetTag("vc.cockpit.cache.outcome", cacheOutcome);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private void LogEndpointTelemetry(
        string endpoint,
        TimeSpan elapsed,
        string status,
        string cacheOutcome,
        object? payload,
        string widget = "none")
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("endpoint", endpoint),
            new("status", status),
            new("cache_outcome", cacheOutcome),
            new("widget", widget)
        };
        EndpointLatency.Record(
            elapsed.TotalMilliseconds,
            tags);
        if (payload is not null)
        {
            EndpointPayloadSize.Record(JsonSerializer.SerializeToUtf8Bytes(payload).LongLength, tags);
        }

        _logger.LogInformation(
            "Executive cockpit endpoint {Endpoint} completed with {Status} in {ElapsedMilliseconds} ms using cache outcome {CacheOutcome}.",
            endpoint,
            status,
            elapsed.TotalMilliseconds,
            cacheOutcome);
    }

    private static string NormalizeTag(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
}
