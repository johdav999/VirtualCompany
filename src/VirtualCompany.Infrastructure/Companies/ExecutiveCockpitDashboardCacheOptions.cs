namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveCockpitDashboardCacheOptions
{
    public const string SectionName = "ExecutiveCockpitDashboardCache";

    public bool Enabled { get; set; } = true;
    public string KeyPrefix { get; set; } = "vc:executive-cockpit";
    public string KeyVersion { get; set; } = "v1";
    public int TtlSeconds { get; set; } = 60;
    public int WidgetTtlSeconds { get; set; } = 60;
    public int VersionTokenTtlSeconds { get; set; } = 86400;

    public TimeSpan GetTtl() =>
        TimeSpan.FromSeconds(Math.Clamp(TtlSeconds, 1, 300));

    public TimeSpan GetWidgetTtl() =>
        TimeSpan.FromSeconds(Math.Clamp(WidgetTtlSeconds, 1, 300));

    public TimeSpan GetVersionTokenTtl() =>
        TimeSpan.FromSeconds(Math.Clamp(VersionTokenTtlSeconds, 60, 604800));
}