namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveCockpitDashboardCacheOptions
{
    public const string SectionName = "ExecutiveCockpitDashboardCache";

    public bool Enabled { get; set; } = true;
    public string KeyPrefix { get; set; } = "vc:executive-cockpit";
    public string KeyVersion { get; set; } = "v1";
    public int TtlSeconds { get; set; } = 60;

    public TimeSpan GetTtl() =>
        TimeSpan.FromSeconds(Math.Clamp(TtlSeconds, 1, 300));
}