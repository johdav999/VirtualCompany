namespace VirtualCompany.Domain.Entities;

public sealed record AgentResponsibilityPolicy(
    IReadOnlyList<ResponsibilityDomainRule> AllowedDomains,
    IReadOnlyList<ResponsibilityDomainRule> DeniedDomains,
    IReadOnlyDictionary<string, DelegationTarget> DelegationTargets)
{
    public static AgentResponsibilityPolicy Empty { get; } =
        new([], [], new Dictionary<string, DelegationTarget>(StringComparer.OrdinalIgnoreCase));
}

public sealed record ResponsibilityDomainRule(
    string RuleId,
    string DomainPattern)
{
    public bool IsMatch(string requestedDomain)
    {
        if (string.IsNullOrWhiteSpace(requestedDomain))
        {
            return false;
        }

        var domain = requestedDomain.Trim();
        var pattern = DomainPattern.Trim();

        if (pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return domain.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(domain, pattern, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DelegationTarget(
    string Target,
    string ActionType = "delegation")
{
    public static DelegationTarget EscalateTo(string target) => new(target, "escalation");
}

public static class ResponsibilityDomainRules
{
    public static IReadOnlyList<ResponsibilityDomainRule> Normalize(
        IEnumerable<string>? domains,
        string rulePrefix)
    {
        if (domains is null)
        {
            return [];
        }

        return domains
            .Where(static domain => !string.IsNullOrWhiteSpace(domain))
            .Select(static domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static domain => domain.Count(static character => character == '*'))
            .ThenByDescending(static domain => domain.Length)
            .ThenBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
            .Select((domain, index) => new ResponsibilityDomainRule($"{rulePrefix}:{index + 1}", domain))
            .ToArray();
    }

    public static string NormalizeRequestedDomain(string? requestedDomain, string fallback)
    {
        var domain = string.IsNullOrWhiteSpace(requestedDomain) ? fallback : requestedDomain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return "unknown";
        }

        return domain.Trim().ToLowerInvariant();
    }

    public static ResponsibilityDomainRule? FirstMatch(
        IEnumerable<ResponsibilityDomainRule> rules,
        string requestedDomain)
    {
        foreach (var rule in rules)
        {
            if (rule.IsMatch(requestedDomain))
            {
                return rule;
            }
        }

        return null;
    }
}
