using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

// Compose only from source-bound claims, never from a potentially unsupported summary.
internal static class SalesMeetingAnswerGrounding
{
    internal sealed record Answer(string Text, IReadOnlyList<AgentAiClaim> Claims, bool Partial);
    internal static Answer Compose(AgentReasoningResult result, IReadOnlySet<string> sourceIds)
    {
        var claims = new List<AgentAiClaim>();
        var length = 0;
        var words = 0;
        foreach (var claim in result.Claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Text) || claim.SourceIds.Count == 0 ||
                !claim.SourceIds.All(sourceIds.Contains) || claim.Type is not ("confirmed_fact" or "fact") ||
                length + claim.Text.Length + 1 > 650 || claims.Count >= 3 ||
                words + WordCount(claim.Text) > 65) continue;
            claims.Add(claim); length += claim.Text.Length + 1; words += WordCount(claim.Text);
        }
        var partial = result.Status == AgentAiRunStatuses.NeedsReview || result.MissingEvidence.Count > 0 ||
            result.Uncertainty.Count > 0 || claims.Count != result.Claims.Count;
        if (claims.Count == 0) return new(SalesMeetingQuestion.SafeNoEvidenceLimitation, claims, false);
        var text = string.Join(" ", claims.Select(x => x.Text.Trim()));
        if (partial)
        {
            var gaps = result.MissingEvidence.Concat(result.Uncertainty).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).ToArray();
            // Whole bounded descriptions only: never cut a qualifier out of a sentence.
            var selected = new List<string>(); var gapLength = 0; var gapWords = 0;
            foreach (var gap in gaps)
                if (selected.Count < 2 && gapLength + gap.Length + 2 <= 180 && gapWords + WordCount(gap) <= 20)
                { selected.Add(gap.Trim().TrimEnd('.')); gapLength += gap.Length + 2; gapWords += WordCount(gap); }
            text += selected.Count > 0
                ? " Still to confirm: " + string.Join("; ", selected) + ". Other unverified details require follow-up."
                : " I can't verify the remaining details; they need follow-up.";
        }
        return new(text, claims, partial);
    }

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
