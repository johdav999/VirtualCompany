using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualCompany.Domain.Enums;

 [JsonConverter(typeof(CompanyMembershipRoleJsonConverter))]
public enum CompanyMembershipRole
{
    Owner = 1,
    Admin = 2,
    Manager = 3,
    Employee = 4,
    FinanceApprover = 5,
    SupportSupervisor = 6
}

public enum CompanyMembershipStatus
{
    Pending = 1,
    Active = 2,
    Revoked = 3
}

public sealed record CompanyMembershipRoleOption(
    CompanyMembershipRole Role,
    string Value,
    string DisplayName);

public static class CompanyMembershipRoles
{
    private static readonly CompanyMembershipRoleOption[] SupportedRoles =
    [
        new(CompanyMembershipRole.Owner, "owner", "Owner"),
        new(CompanyMembershipRole.Admin, "admin", "Admin"),
        new(CompanyMembershipRole.Manager, "manager", "Manager"),
        new(CompanyMembershipRole.Employee, "employee", "Employee"),
        new(CompanyMembershipRole.FinanceApprover, "finance_approver", "Finance Approver"),
        new(CompanyMembershipRole.SupportSupervisor, "support_supervisor", "Support Supervisor")
    ];

    private static readonly IReadOnlyDictionary<CompanyMembershipRole, CompanyMembershipRoleOption> RolesByEnum =
        SupportedRoles.ToDictionary(x => x.Role);

    private static readonly IReadOnlyDictionary<string, CompanyMembershipRole> RolesByValue =
        SupportedRoles.ToDictionary(x => x.Value, x => x.Role, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CompanyMembershipRoleOption> All { get; } = SupportedRoles;

    public static IReadOnlyList<string> AllowedValues { get; } =
        SupportedRoles.Select(x => x.Value).ToArray();

    public static bool IsSupported(CompanyMembershipRole role) => RolesByEnum.ContainsKey(role);

    public static CompanyMembershipRoleOption Get(CompanyMembershipRole role) =>
        RolesByEnum.TryGetValue(role, out var option)
            ? option
            : throw new ArgumentOutOfRangeException(nameof(role), role, BuildValidationMessage());

    public static string ToStorageValue(CompanyMembershipRole role) => Get(role).Value;

    public static string ToDisplayName(CompanyMembershipRole role) => Get(role).DisplayName;

    public static bool TryParse(string? value, out CompanyMembershipRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (RolesByValue.TryGetValue(normalized, out role))
        {
            return true;
        }

        if (Enum.TryParse<CompanyMembershipRole>(normalized, ignoreCase: true, out var legacyRole) &&
            IsSupported(legacyRole))
        {
            role = legacyRole;
            return true;
        }

        return false;
    }

    public static CompanyMembershipRole Parse(string value)
    {
        if (TryParse(value, out var role))
        {
            return role;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(CompanyMembershipRole role, string paramName)
    {
        if (!IsSupported(role))
        {
            throw new ArgumentOutOfRangeException(paramName, role, BuildValidationMessage());
        }
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Membership role is required. Allowed values: {allowedValues}."
            : $"Unsupported company membership role value '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class CompanyMembershipRoleValues
{
    public static IReadOnlyList<CompanyMembershipRoleOption> All => CompanyMembershipRoles.All;

    public static IReadOnlyList<string> AllowedValues => CompanyMembershipRoles.AllowedValues;

    public static string ToStorageValue(this CompanyMembershipRole role) =>
        CompanyMembershipRoles.ToStorageValue(role);

    public static string ToDisplayName(this CompanyMembershipRole role) =>
        CompanyMembershipRoles.ToDisplayName(role);

    public static CompanyMembershipRole Parse(string value) =>
        CompanyMembershipRoles.Parse(value);
}

public static class CompanyMembershipStatusValues
{
    public static string ToStorageValue(this CompanyMembershipStatus status) =>
        status switch
        {
            CompanyMembershipStatus.Pending => "pending",
            CompanyMembershipStatus.Active => "active",
            CompanyMembershipStatus.Revoked => "revoked",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company membership status.")
        };

    public static CompanyMembershipStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Membership status is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => CompanyMembershipStatus.Pending,
            "active" => CompanyMembershipStatus.Active,
            "revoked" => CompanyMembershipStatus.Revoked,
            _ when Enum.TryParse<CompanyMembershipStatus>(value.Trim(), ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company membership status value.")
        };
    }
}

internal sealed class CompanyMembershipRoleJsonConverter : JsonConverter<CompanyMembershipRole>
{
    public override CompanyMembershipRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(CompanyMembershipRoles.BuildValidationMessage());
        }

        var value = reader.GetString();
        if (CompanyMembershipRoles.TryParse(value, out var role))
        {
            return role;
        }

        throw new JsonException(CompanyMembershipRoles.BuildValidationMessage(value));
    }

    public override void Write(Utf8JsonWriter writer, CompanyMembershipRole value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToStorageValue());
}