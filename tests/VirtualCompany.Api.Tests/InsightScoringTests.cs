using VirtualCompany.Application.Insights;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InsightScoringTests
{
    private readonly DefaultInsightScoringService _scoring = new();
    private readonly DefaultActionDeepLinkResolver _links = new();

    [Fact]
    public void Breached_approval_scores_above_non_urgent_opportunity()
    {
        var now = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc);
        var companyId = Guid.NewGuid();

        var approval = Candidate(
            companyId,
            ActionInsightType.Approval,
            ActionInsightTargetType.Approval,
            dueUtc: now.AddMinutes(-10),
            slaState: ActionInsightSlaState.Breached);
        var opportunity = Candidate(
            companyId,
            ActionInsightType.Opportunity,
            ActionInsightTargetType.Alert,
            dueUtc: now.AddDays(2),
            slaState: ActionInsightSlaState.OnTrack);

        Assert.True(_scoring.Score(approval, now).PriorityScore > _scoring.Score(opportunity, now).PriorityScore);
    }

    [Fact]
    public void Equal_priority_and_due_items_use_impact_then_source_id_tie_breakers()
    {
        var companyId = Guid.NewGuid();
        var dueUtc = new DateTime(2026, 4, 14, 16, 0, 0, DateTimeKind.Utc);
        var ordered = ActionQueueOrdering.Order(
        [
            ScoredCandidate(companyId, "null-due", Guid.Parse("44444444-4444-4444-4444-444444444444"), null, 95, ActionInsightPriority.High),
            ScoredCandidate(companyId, "lower-impact", Guid.Parse("33333333-3333-3333-3333-333333333333"), dueUtc, 70, ActionInsightPriority.High),
            ScoredCandidate(companyId, "higher-id", Guid.Parse("22222222-2222-2222-2222-222222222222"), dueUtc, 90, ActionInsightPriority.High),
            ScoredCandidate(companyId, "earlier-id", Guid.Parse("11111111-1111-1111-1111-111111111111"), dueUtc, 90, ActionInsightPriority.High),
            ScoredCandidate(companyId, "lower-priority", Guid.Parse("55555555-5555-5555-5555-555555555555"), dueUtc, 100, ActionInsightPriority.Medium)
        ]);

        Assert.Equal(
            ["earlier-id", "higher-id", "lower-impact", "null-due", "lower-priority"],
            ordered.Select(x => x.Candidate.InsightKey).ToArray());
    }

    [Theory]
    [InlineData(ActionInsightTargetType.Approval, "/approvals?companyId=")]
    [InlineData(ActionInsightTargetType.Task, "/tasks?companyId=")]
    [InlineData(ActionInsightTargetType.Workflow, "/workflows?companyId=")]
    public void Deep_links_use_canonical_action_routes(ActionInsightTargetType targetType, string routePrefix)
    {
        var companyId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var link = _links.Resolve(companyId, new ActionDeepLinkTarget(targetType, targetId));

        Assert.StartsWith(routePrefix, link.Href);
        Assert.Equal(targetType, link.TargetType);
        Assert.Equal(targetId, link.TargetId);
        Assert.Contains(companyId.ToString("D"), link.Href);
        Assert.Contains(targetId.ToString("D"), link.Href);
    }

    [Fact]
    public void Unsupported_deep_link_target_returns_dashboard_fallback()
    {
        var companyId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var link = _links.Resolve(companyId, new ActionDeepLinkTarget((ActionInsightTargetType)999, targetId));

        Assert.Equal($"/dashboard?companyId={companyId:D}", link.Href);
    }

    [Fact]
    public void Insight_keys_are_stable_for_same_action_identity()
    {
        var companyId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        Assert.Equal(InsightKey.For(companyId, ActionInsightType.Task, sourceId, 3), InsightKey.For(companyId, ActionInsightType.Task, sourceId, 3));
    }

    private static ScoredInsightCandidate ScoredCandidate(
        Guid companyId,
        string key,
        Guid sourceId,
        DateTime? dueUtc,
        int impactScore,
        ActionInsightPriority priority) =>
        new(
            new InsightCandidate(
                key,
                companyId,
                ActionInsightType.Task,
                ActionInsightTargetType.Task.ToStorageValue(),
                sourceId,
                ActionInsightTargetType.Task,
                sourceId,
                "Insight",
                "Reason",
                "Owner",
                dueUtc,
                dueUtc.HasValue ? ActionInsightSlaState.OnTrack : ActionInsightSlaState.None,
                new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)),
            impactScore,
            impactScore,
            priority);

    private static InsightCandidate Candidate(
        Guid companyId,
        ActionInsightType type,
        ActionInsightTargetType targetType,
        DateTime? dueUtc,
        ActionInsightSlaState slaState) =>
        Candidate(companyId, type, targetType, InsightKey.For(companyId, type, Guid.NewGuid()), dueUtc, slaState, DateTime.UtcNow);

    private static InsightCandidate Candidate(
        Guid companyId,
        ActionInsightType type,
        ActionInsightTargetType targetType,
        string key,
        DateTime? dueUtc,
        ActionInsightSlaState slaState,
        DateTime createdUtc)
    {
        var sourceId = Guid.NewGuid();
        return new InsightCandidate(
            key,
            companyId,
            type,
            targetType.ToStorageValue(),
            sourceId,
            targetType,
            sourceId,
            "Insight",
            "Reason",
            "Owner",
            dueUtc,
            slaState,
            createdUtc);
    }
}