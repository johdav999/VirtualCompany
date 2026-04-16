using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Orchestration;

public sealed class CommunicationStyleRuleChecker : ICommunicationStyleRuleChecker
{
    public CommunicationStyleRuleCheckResult Check(string? generatedText, AgentCommunicationProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var text = generatedText ?? string.Empty;
        var violations = new List<CommunicationStyleRuleViolation>();

        foreach (var rule in profile.ForbiddenToneRules)
        {
            var normalized = NormalizeRule(rule);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (ContainsForbiddenMarker(text, normalized))
            {
                violations.Add(new CommunicationStyleRuleViolation(
                    BuildRuleId(normalized),
                    "forbidden_tone_rule",
                    $"Generated output contains forbidden communication marker '{normalized}'."));
            }
        }

        return new CommunicationStyleRuleCheckResult(
            violations.Count == 0,
            violations,
            violations.Select(static x => x.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool ContainsForbiddenMarker(string text, string rule)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var marker = rule;
        foreach (var prefix in new[] { "avoid ", "do not use ", "do not ", "never use ", "never " })
        {
            if (marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                marker = marker[prefix.Length..].Trim();
                break;
            }
        }

        return marker.Length > 0 &&
               text.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRule(string value) =>
        value.ReplaceLineEndings(" ").Trim();

    private static string BuildRuleId(string rule)
    {
        var normalized = new string(rule.ToLowerInvariant().Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        normalized = string.Join("_", normalized.Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "forbidden_style_rule" : $"forbidden_style_{normalized}";
    }
}
