using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Low, Normal, High, Urgent], nameof(value));
}

