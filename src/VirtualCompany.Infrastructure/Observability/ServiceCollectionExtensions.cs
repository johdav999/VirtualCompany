using System.Net;
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
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Observability;

public static class ServiceCollectionExtensions
{
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
                var observabilityOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                var correlationId = CorrelationIdMiddleware.GetCorrelationId(context.HttpContext) ?? context.HttpContext.TraceIdentifier;
                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "The request was throttled by a platform rate limiting policy."
                };
                problemDetails.Extensions["correlationId"] = correlationId;

                context.HttpContext.Response.Headers[observabilityOptions.CorrelationId.HeaderName] = correlationId;
                await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            };

            if (!settings.RateLimiting.Enabled)
            {
                options.AddPolicy(PlatformRateLimitPolicyNames.Chat, static _ => RateLimitPartition.GetNoLimiter("chat"));
                options.AddPolicy(PlatformRateLimitPolicyNames.Tasks, static _ => RateLimitPartition.GetNoLimiter("tasks"));
                return;
            }

            options.AddFixedWindowLimiter(PlatformRateLimitPolicyNames.Chat, limiterOptions => ApplyRateLimit(limiterOptions, settings.RateLimiting.Chat));
            options.AddFixedWindowLimiter(PlatformRateLimitPolicyNames.Tasks, limiterOptions => ApplyRateLimit(limiterOptions, settings.RateLimiting.Tasks));
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

    private static void ApplyRateLimit(FixedWindowRateLimiterOptions options, RateLimitPolicyOptions settings)
    {
        options.PermitLimit = Math.Max(1, settings.PermitLimit);
        options.Window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds));
        options.QueueLimit = Math.Max(0, settings.QueueLimit);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.AutoReplenishment = true;
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
}

public static class PlatformRateLimitPolicyNames
{
    public const string Chat = "chat";
    public const string Tasks = "tasks";
}
