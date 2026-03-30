namespace VirtualCompany.Infrastructure.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public CorrelationIdOptions CorrelationId { get; set; } = new();
    public HealthEndpointOptions Health { get; set; } = new();
    public RateLimitingOptions RateLimiting { get; set; } = new();
    public RedisHealthOptions Redis { get; set; } = new();
    public ObjectStorageHealthOptions ObjectStorage { get; set; } = new();
}

public sealed class CorrelationIdOptions
{
    public string HeaderName { get; set; } = "X-Correlation-ID";
}

public sealed class HealthEndpointOptions
{
    public string Path { get; set; } = "/health";
    public string LivenessPath { get; set; } = "/health/live";
    public string ReadinessPath { get; set; } = "/health/ready";
}

public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public RateLimitPolicyOptions Chat { get; set; } = new();
    public RateLimitPolicyOptions Tasks { get; set; } = new();
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}

public sealed class RedisHealthOptions
{
    public string? ConnectionString { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 2;
}

public sealed class ObjectStorageHealthOptions
{
    public bool Enabled { get; set; }
    public string? ServiceUri { get; set; }
    public string HealthPath { get; set; } = "/";
    public int TimeoutSeconds { get; set; } = 2;
}