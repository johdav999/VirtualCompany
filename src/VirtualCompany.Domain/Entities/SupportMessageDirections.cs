using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportMessageDirections
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Internal = "internal";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Inbound, Outbound, Internal], nameof(value));
}

