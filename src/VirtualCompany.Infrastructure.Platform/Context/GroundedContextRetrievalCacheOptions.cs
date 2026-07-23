namespace VirtualCompany.Infrastructure.Context;

public sealed class GroundedContextRetrievalCacheOptions
{
    public const string SectionName = "GroundedContextRetrievalCache";

    public bool Enabled { get; set; } = true;
    public string KeyVersion { get; set; } = "task-9-4-7-v1";
    public int DefaultTtlSeconds { get; set; } = 60;
    public int KnowledgeTtlSeconds { get; set; } = 120;
    public int MemoryTtlSeconds { get; set; } = 30;
    public int MaxPayloadBytes { get; set; } = 64 * 1024;

    public TimeSpan GetSectionTtl(string sectionId)
    {
        var ttlSeconds = string.Equals(sectionId, "knowledge", StringComparison.OrdinalIgnoreCase)
            ? KnowledgeTtlSeconds
            : string.Equals(sectionId, "memory", StringComparison.OrdinalIgnoreCase)
                ? MemoryTtlSeconds
                : DefaultTtlSeconds;

        return TimeSpan.FromSeconds(Math.Max(0, ttlSeconds));
    }

    public bool IsPayloadCachingAllowed =>
        Enabled &&
        MaxPayloadBytes > 0 &&
        !string.IsNullOrWhiteSpace(KeyVersion);
}
