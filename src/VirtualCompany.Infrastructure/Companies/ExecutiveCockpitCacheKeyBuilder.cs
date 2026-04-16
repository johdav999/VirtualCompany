using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveCockpitCacheKeyBuilder
{
    private readonly ExecutiveCockpitDashboardCacheOptions _options;

    public ExecutiveCockpitCacheKeyBuilder(IOptions<ExecutiveCockpitDashboardCacheOptions> options)
    {
        _options = options.Value;
    }

    public string BuildDataKey(ExecutiveCockpitCacheScope scope, string versionToken)
    {
        var prefix = Prefix();
        var normalizedDepartments = NormalizeDepartments(scope.DepartmentFilters);
        var role = NormalizeSegment(scope.EffectiveRole);
        var identity = NormalizeSegment(scope.Identity);
        var range = NormalizeRange(scope.StartUtc, scope.EndUtc);
        var filterHash = Hash(string.Join("|", normalizedDepartments));

        return $"{prefix}:data:{versionToken}:company:{scope.CompanyId:N}:role:{role}:filters:{filterHash}:range:{range}:id:{identity}";
    }

    public string BuildVersionKey(Guid companyId)
    {
        return $"{Prefix()}:version:company:{companyId:N}";
    }

    public string BuildLegacyCompanyKey(Guid companyId)
    {
        return $"{Prefix()}:company:{companyId:N}";
    }

    public static IReadOnlyList<string> NormalizeDepartments(IEnumerable<string>? departments) =>
        (departments ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static ExecutiveCockpitCacheScope DashboardScope(Guid companyId, string role) =>
        new(companyId, role, [], null, null, "dashboard");

    public static ExecutiveCockpitCacheScope WidgetScope(
        Guid companyId,
        string role,
        string widgetKey,
        IEnumerable<string>? departments = null,
        DateTime? startUtc = null,
        DateTime? endUtc = null) =>
        new(
            companyId,
            role,
            NormalizeDepartments(departments),
            NormalizeUtc(startUtc),
            NormalizeUtc(endUtc),
            $"widget:{widgetKey}");

    public static ExecutiveCockpitCacheScope KpiScope(
        Guid companyId,
        string role,
        string? department,
        DateTime startUtc,
        DateTime endUtc) =>
        new(
            companyId,
            role,
            string.IsNullOrWhiteSpace(department) ? [] : [department],
            NormalizeUtc(startUtc),
            NormalizeUtc(endUtc),
            "kpis");

    private string Prefix() =>
        $"{_options.KeyPrefix.Trim().TrimEnd(':')}:{_options.KeyVersion.Trim().ToLowerInvariant()}";

    private static string NormalizeRange(DateTime? startUtc, DateTime? endUtc) =>
        $"{FormatDateTime(startUtc)}-{FormatDateTime(endUtc)}";

    private static string FormatDateTime(DateTime? value) =>
        value is null
            ? "all"
            : NormalizeUtc(value).Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

    private static string NormalizeSegment(string value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Trim().ToLowerInvariant().Replace(" ", "-");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
}