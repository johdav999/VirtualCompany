using System.Net;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Observability;

public static class ServiceCollectionExtensions
{
    private const string ProblemDetailsContentType = "application/problem+json";
    private const string RateLimitingLoggerName = "VirtualCompany.Infrastructure.Observability.RateLimiting";

    public static IServiceCollection AddVirtualCompanyObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName));

        services.AddHttpClient();
        services.AddScoped<ICorrelationContextAccessor, RequestCorrelationContextAccessor>();
        services.AddScoped<CorrelationIdMiddleware>();

        services.AddHealthChecks()
            .AddCheck<ApplicationHealthCheck>("application", tags: ["live"])
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<RedisTcpHealthCheck>("redis", tags: ["ready"])
            .AddCheck<ObjectStorageHealthCheck>("object-storage", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddVirtualCompanyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = static async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                var services = httpContext.RequestServices;
                var observabilityOptions = services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(RateLimitingLoggerName);
                var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext) ?? httpContext.TraceIdentifier;
                var traceId = httpContext.TraceIdentifier;
                var policyName = ResolveRateLimitPolicyName(httpContext);
                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "The request was throttled by a platform rate limiting policy.",
                    Type = "https://httpstatuses.com/429",
                    Instance = httpContext.Request.Path.HasValue ? httpContext.Request.Path.Value : null
                };

                problemDetails.Extensions["correlationId"] = correlationId;
                problemDetails.Extensions["traceId"] = traceId;

                if (!string.IsNullOrWhiteSpace(policyName))
                {
                    problemDetails.Extensions["rateLimitPolicy"] = policyName;
                }

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                    problemDetails.Extensions["retryAfterSeconds"] = retryAfterSeconds;
                }

                using var scope = logger.BeginScope(ExecutionLogScope.ForHttpContext(httpContext));
                logger.LogWarning(
                    "Rate limiting rejected HTTP {Method} {Path} for policy {PolicyName}.",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    policyName ?? "unknown");

                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                httpContext.Response.Headers[observabilityOptions.CorrelationId.HeaderName] = correlationId;
                await httpContext.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: ProblemDetailsContentType, cancellationToken: cancellationToken);
            };

            if (!settings.RateLimiting.Enabled)
            {
                options.AddPolicy(PlatformRateLimitPolicyNames.Chat, static _ => RateLimitPartition.GetNoLimiter(PlatformRateLimitPolicyNames.Chat));
                options.AddPolicy(PlatformRateLimitPolicyNames.Tasks, static _ => RateLimitPartition.GetNoLimiter(PlatformRateLimitPolicyNames.Tasks));
                return;
            }

            options.AddPolicy(PlatformRateLimitPolicyNames.Chat, context => CreateFixedWindowPartition(context, PlatformRateLimitPolicyNames.Chat, settings.RateLimiting.Chat));
            options.AddPolicy(PlatformRateLimitPolicyNames.Tasks, context => CreateFixedWindowPartition(context, PlatformRateLimitPolicyNames.Tasks, settings.RateLimiting.Tasks));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapVirtualCompanyHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.ServiceProvider.GetRequiredService<IOptions<ObservabilityOptions>>().Value.Health;

        endpoints.MapHealthChecks(settings.Path, CreateHealthCheckOptions(registration => registration.Tags.Contains("ready")))
            .AllowAnonymous()
            .WithName("health");
        endpoints.MapHealthChecks(settings.LivenessPath, CreateHealthCheckOptions(registration => registration.Tags.Contains("live")))
            .AllowAnonymous()
            .WithName("health-live");
        endpoints.MapHealthChecks(settings.ReadinessPath, CreateHealthCheckOptions(registration => registration.Tags.Contains("ready")))
            .AllowAnonymous()
            .WithName("health-ready");

        return endpoints;
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext context, string policyName, RateLimitPolicyOptions settings) =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(context, policyName),
            _ => CreateFixedWindowOptions(settings));

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(RateLimitPolicyOptions settings)
    {
        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, settings.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds)),
            QueueLimit = Math.Max(0, settings.QueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        };

        return options;
    }

    private static string GetPartitionKey(HttpContext context, string policyName)
    {
        var services = context.RequestServices;
        var companyContextAccessor = services.GetService<ICompanyContextAccessor>();
        var currentUserAccessor = services.GetService<ICurrentUserAccessor>();

        var companyId = companyContextAccessor?.CompanyId;
        var userId = companyContextAccessor?.UserId ?? currentUserAccessor?.UserId;

        if (companyId.HasValue && userId.HasValue)
        {
            return $"{policyName}:company:{companyId.Value:N}:user:{userId.Value:N}";
        }

        if (userId.HasValue)
        {
            return $"{policyName}:user:{userId.Value:N}";
        }

        if (companyId.HasValue)
        {
            return $"{policyName}:company:{companyId.Value:N}";
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(remoteIp)
            ? $"{policyName}:anonymous"
            : $"{policyName}:ip:{remoteIp}";
    }

    private static HealthCheckOptions CreateHealthCheckOptions(Func<HealthCheckRegistration, bool>? predicate = null) =>
        new()
        {
            AllowCachingResponses = false,
            Predicate = predicate,
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        };

    private static string? ResolveRateLimitPolicyName(HttpContext context) =>
        context.GetEndpoint()?
            .Metadata
            .OfType<EnableRateLimitingAttribute>()
            .LastOrDefault()?
            .PolicyName;
}

public static class PlatformRateLimitPolicyNames
{
    public const string Chat = "chat";
    public const string Tasks = "tasks";
}
