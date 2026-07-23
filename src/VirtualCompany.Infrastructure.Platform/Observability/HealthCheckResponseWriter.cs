using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VirtualCompany.Infrastructure.Observability;

internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new HealthReportResponse(
            report.Status.ToString(),
            report.TotalDuration,
            report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new HealthReportEntryResponse(
                    entry.Value.Status.ToString(),
                    entry.Value.Description,
                    entry.Value.Duration,
                    entry.Value.Data.Count == 0
                        ? null
                        : entry.Value.Data.ToDictionary(data => data.Key, data => SanitizeValue(data.Value)))));

        return context.Response.WriteAsJsonAsync(payload, SerializerOptions, contentType: "application/json");
    }

    private static object? SanitizeValue(object? value) => value switch
    {
        null => null,
        string => value,
        bool => value,
        byte => value,
        sbyte => value,
        short => value,
        ushort => value,
        int => value,
        uint => value,
        long => value,
        ulong => value,
        float => value,
        double => value,
        decimal => value,
        TimeSpan timeSpan => timeSpan.ToString(),
        Uri uri => uri.ToString(),
        Enum enumValue => enumValue.ToString(),
        _ => value.ToString()
    };

    private sealed record HealthReportResponse(string Status, TimeSpan TotalDuration, IReadOnlyDictionary<string, HealthReportEntryResponse> Results);
    private sealed record HealthReportEntryResponse(string Status, string? Description, TimeSpan Duration, IReadOnlyDictionary<string, object?>? Data);
}