using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportCaseStatuses
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string WaitingForCustomer = "waiting_for_customer";
    public const string WaitingInternal = "waiting_internal";
    public const string Escalated = "escalated";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Resolved = "resolved";
    public const string Reopened = "reopened";
    public const string Closed = "closed";
    public static string Normalize(string value) => NormalizeKnownForSupport(value, [New, Triaged, WaitingForCustomer, WaitingInternal, Escalated, AwaitingApproval, Resolved, Reopened, Closed], nameof(value));
    public static string NormalizeKnownForSupport(string value, IReadOnlyCollection<string> known, string name)
    {
        var normalized = SupportEntityText.NormalizeRequired(value, name, 80).Trim().ToLowerInvariant().Replace(' ', '_');
        return known.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : throw new ArgumentException($"Unsupported value '{value}'.", name);
    }
}

