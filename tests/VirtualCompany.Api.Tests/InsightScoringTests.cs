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
    public void Equal_scores_use_stable_tie_breakers()
    {
        var now = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc);
        var companyId = Guid.NewGuid();
        var laterKey = Candidate(companyId, ActionInsightType.Risk, ActionInsightTargetType.Alert, "z-key", now.AddHours(8), ActionInsightSlaState.OnTrack, now.AddMinutes(2));
        var earlierKey = Candidate(companyId, ActionInsightType.Risk, ActionInsightTargetType.Alert, "a-key", now.AddHours(8), ActionInsightSlaState.OnTrack, now.AddMinutes(2));
        var earliestCreated = Candidate(companyId, ActionInsightType.Risk, ActionInsightTargetType.Alert, "m-key", now.AddHours(8), ActionInsightSlaState.OnTrack, now.AddMinutes(1));
        var earliestDue = Candidate(companyId, ActionInsightType.Risk, ActionInsightTargetType.Alert, "x-key", now.AddHours(4), ActionInsightSlaState.OnTrack, now.AddMinutes(3));

        var ordered = _scoring.Prioritize([laterKey, earlierKey, earliestCreated, earliestDue], now);

        Assert.Equal(["x-key", "m-key", "a-key", "z-key"], ordered.Select(x => x.Candidate.InsightKey).ToArray());
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