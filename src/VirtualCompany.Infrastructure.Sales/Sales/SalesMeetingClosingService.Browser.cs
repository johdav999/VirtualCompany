using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesMeetingClosingService
{
    private async Task<List<CustomerSeed>> BuildBrowserSeedsAsync(Guid company, Guid user, SalesMeetingSession session,
        Guid agent, AgentEffectiveAuthorityDto authority, Guid generation, CancellationToken ct)
    {
        // Human review selects the excerpts allowed into the customer-minutes reasoning context.
        var segments = await db.SalesMeetingTranscriptSegments.AsNoTracking().Where(x => x.CompanyId == company &&
            x.SessionId == session.Id && x.InputSource == SalesMeetingInputSource.BrowserRoom &&
            x.ReviewState == SalesMeetingReviewState.Reviewed).OrderBy(x => x.Sequence).Take(200).ToListAsync(ct);
        if (segments.Count == 0) return [];
        var sources = segments.Select(x => new AgentAiSource($"transcript:{x.Id:N}", "reviewed_browser_transcript",
            "Reviewed discussion excerpt", x.Content)).ToArray();
        var result = await PolishAsync(company, user, agent, authority, sources,
            "Extract only explicit agreed decisions, actions, and unanswered questions from these human-reviewed excerpts. " +
            "The meeting capture is partial. Do not fill gaps or infer promises, pricing, terms, commitments or owners. " +
            "Exclude internal strategy and private sales analysis. Return claims with type decision, action or outstanding_question; " +
            "each claim must cite exactly one supplied transcript source. All results require human review.", ct, $"browser-closing:{session.Id:N}");
        if (result?.Status != AgentAiRunStatuses.Completed)
            throw Conflict("Discussion summary generation is unavailable. Retained evidence is safe; retry preparation later.");
        var allowed = sources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var seeds = new List<CustomerSeed>();
        var sequence = (await db.SalesMeetingActionItems.Where(x => x.CompanyId == company && x.SessionId == session.Id)
            .MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        foreach (var claim in result.Claims.Where(x => x.SourceIds.Count == 1 && allowed.Contains(x.SourceIds[0]) &&
            x.Confidence >= .5m && !string.IsNullOrWhiteSpace(x.Text) && x.Text.Length <= 4000)
            .GroupBy(x => (x.SourceIds[0], x.Type)).Select(x => x.First()).Take(200))
        {
            var type = claim.Type switch { "decision" => SalesMeetingMinutesItemType.Decision,
                "action" => SalesMeetingMinutesItemType.Action, "outstanding_question" => SalesMeetingMinutesItemType.OutstandingQuestion,
                _ => (SalesMeetingMinutesItemType?)null };
            if (type is null) continue;
            var source = claim.SourceIds[0];
            if (type == SalesMeetingMinutesItemType.Action)
            {
                var sourceId = Guid.Parse(source["transcript:".Length..]);
                if (!await db.SalesMeetingActionItems.AnyAsync(x => x.CompanyId == company && x.SessionId == session.Id &&
                    x.ClientItemId == sourceId, ct))
                {
                    db.SalesMeetingActionItems.Add(new(Guid.NewGuid(), company, session.Id, sourceId, sequence++,
                        claim.Text[..Math.Min(500, claim.Text.Length)], claim.Text, null, null,
                        SalesMeetingActionItemStatus.Open, claim.Confidence, source, SalesMeetingReviewState.Unreviewed,
                        user, generation, timeProvider.GetUtcNow().UtcDateTime));
                }
                // Include newly tracked candidates now; later generations reuse the canonical action item.
                if (!await db.SalesMeetingActionItems.AnyAsync(x => x.CompanyId == company && x.SessionId == session.Id &&
                    x.ClientItemId == sourceId, ct))
                    seeds.Add(new(type.Value, claim.Text, null, null, source, null, true));
            }
            else seeds.Add(new(type.Value, claim.Text, null, null, source, null, true));
        }
        return seeds;
    }
}
