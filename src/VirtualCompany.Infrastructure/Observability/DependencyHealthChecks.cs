using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Observability;

public sealed class ApplicationHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("Application process is running."));
}

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly VirtualCompanyDbContext _dbContext;

    public DatabaseHealthCheck(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_dbContext.Database.IsRelational())
            {
                return HealthCheckResult.Healthy("Database health check is using a non-relational provider.");
            }

            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL connection succeeded.")
                : HealthCheckResult.Unhealthy("PostgreSQL connection failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL health check failed.", ex);
        }
    }
}

public sealed class RedisTcpHealthCheck : IHealthCheck
{
    private readonly IOptions<ObservabilityOptions> _options;

    public RedisTcpHealthCheck(IOptions<ObservabilityOptions> options)
    {
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value.Redis;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return HealthCheckResult.Healthy("Redis health check is not configured.");
        }

        if (!TryParseEndpoint(settings.ConnectionString, out var host, out var port))
        {
            return HealthCheckResult.Unhealthy("Redis connection string could not be parsed.");
        }

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.ConnectTimeoutSeconds, 1, 10)));
            using var client = new TcpClient();

            await client.ConnectAsync(host, port, timeoutSource.Token);

            await using var stream = client.GetStream();
            var pingCommand = Encoding.ASCII.GetBytes("*1\r\n$4\r\nPING\r\n");

            await stream.WriteAsync(pingCommand, timeoutSource.Token);
            await stream.FlushAsync(timeoutSource.Token);

            var buffer = new byte[32];
            var bytesRead = await stream.ReadAsync(buffer, timeoutSource.Token);
            var response = bytesRead > 0 ? Encoding.ASCII.GetString(buffer, 0, bytesRead) : string.Empty;

            return response.StartsWith("+PONG", StringComparison.OrdinalIgnoreCase)
                ? HealthCheckResult.Healthy($"Redis endpoint {host}:{port} responded to PING.")
                : HealthCheckResult.Unhealthy("Redis endpoint did not return a valid PING response.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connectivity check failed.", ex);
        }
    }

    private static bool TryParseEndpoint(string connectionString, out string host, out int port)
    {
        host = string.Empty;
        port = 6379;

        var endpointToken = connectionString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => !part.Contains('='));

        if (string.IsNullOrWhiteSpace(endpointToken))
        {
            return false;
        }

        if (Uri.TryCreate(endpointToken, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port > 0 ? uri.Port : 6379;
            return !string.IsNullOrWhiteSpace(host);
        }

        var hostParts = endpointToken.Split(':', StringSplitOptions.TrimEntries);
        if (hostParts.Length == 1)
        {
            host = hostParts[0];
            return !string.IsNullOrWhiteSpace(host);
        }

        if (hostParts.Length == 2 && int.TryParse(hostParts[1], out var parsedPort))
        {
            host = hostParts[0];
            port = parsedPort;
            return !string.IsNullOrWhiteSpace(host);
        }

        return false;
    }
}

public sealed class ObjectStorageHealthCheck : IHealthCheck
{
    private readonly IOptions<ObservabilityOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public ObjectStorageHealthCheck(IOptions<ObservabilityOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value.ObjectStorage;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ServiceUri))
        {
            return HealthCheckResult.Healthy("Object storage health check is not configured.");
        }

        if (!Uri.TryCreate(settings.ServiceUri, UriKind.Absolute, out var serviceUri))
        {
            return HealthCheckResult.Unhealthy("Object storage service URI is invalid.");
        }

        var probeUri = string.IsNullOrWhiteSpace(settings.HealthPath)
            ? serviceUri
            : new Uri(serviceUri, settings.HealthPath);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));

            var client = _httpClientFactory.CreateClient(nameof(ObjectStorageHealthCheck));
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, probeUri);
            using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);

            if (headResponse.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy($"Object storage probe succeeded against {probeUri}.");
            }

            if (headResponse.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, probeUri);
                using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
                return getResponse.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy($"Object storage probe succeeded against {probeUri}.")
                    : HealthCheckResult.Unhealthy($"Object storage probe returned {(int)getResponse.StatusCode}.");
            }

            return HealthCheckResult.Unhealthy($"Object storage probe returned {(int)headResponse.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Object storage connectivity check failed.", ex);
        }
    }
}