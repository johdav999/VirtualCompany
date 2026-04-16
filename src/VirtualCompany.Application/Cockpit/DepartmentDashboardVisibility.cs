namespace VirtualCompany.Application.Cockpit;

using System.Text.Json.Nodes;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;

public static class RequiredDepartmentDashboardSections
{
    private static readonly IReadOnlyDictionary<string, int> RequiredDisplayOrder =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["finance"] = 10,
            ["sales"] = 20,
            ["support"] = 30,
            ["operations"] = 40
        };

    public static IReadOnlyList<string> OrderedDepartmentKeys { get; } =
        ["finance", "sales", "support", "operations"];

    public static int GetDisplayOrder(string departmentKey, int fallbackDisplayOrder) =>
        RequiredDisplayOrder.TryGetValue(departmentKey, out var displayOrder)
            ? displayOrder
            : fallbackDisplayOrder;
}

public static class DepartmentDashboardVisibility
{
    public static bool IsVisibleToMembership(
        IReadOnlyDictionary<string, JsonNode?> visibility,
        ResolvedCompanyMembershipContext membership,
        Guid companyId)
    {
        if (membership.CompanyId != companyId || membership.Status != CompanyMembershipStatus.Active)
        {
            return false;
        }

        return IsVisibleToRole(visibility, membership.MembershipRole);
    }

    public static bool IsVisibleToRole(
        IReadOnlyDictionary<string, JsonNode?> visibility,
        CompanyMembershipRole membershipRole)
    {
        if (!TryReadRoles(visibility, out var roles))
        {
            return false;
        }

        var roleValue = membershipRole.ToStorageValue();
        return roles.Any(role => string.Equals(role, roleValue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadRoles(
        IReadOnlyDictionary<string, JsonNode?> visibility,
        out IReadOnlyList<string> roles)
    {
        roles = [];
        if (!visibility.TryGetValue("roles", out var rolesNode) ||
            rolesNode is not JsonArray configuredRoles ||
            configuredRoles.Count == 0)
        {
            return false;
        }

        roles = configuredRoles
            .OfType<JsonValue>()
            .Select(node => node.TryGetValue<string>(out var role) ? role.Trim() : string.Empty)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return roles.Count > 0;
    }
}
