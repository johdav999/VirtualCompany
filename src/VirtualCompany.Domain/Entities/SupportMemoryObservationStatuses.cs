using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportMemoryObservationStatuses
{
    public const string Review = "review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Deleted = "deleted";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Review, Approved, Rejected, Expired, Deleted], nameof(value));
}

internal static class SupportEntityText
{
    public static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    public static Guid? NormalizeOptionalId(Guid? value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;

    public static DateTime NormalizeUtc(DateTime value, string name) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

