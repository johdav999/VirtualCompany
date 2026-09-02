using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayAgentStateMappingTests
{
    [Theory]
    [InlineData(WorkTaskStatus.New, TodayAgentStates.Monitoring)]
    [InlineData(WorkTaskStatus.InProgress, TodayAgentStates.Working)]
    [InlineData(WorkTaskStatus.AwaitingApproval, TodayAgentStates.NeedsUser)]
    [InlineData(WorkTaskStatus.Blocked, TodayAgentStates.Blocked)]
    [InlineData(WorkTaskStatus.Failed, TodayAgentStates.Blocked)]
    [InlineData(WorkTaskStatus.Completed, TodayAgentStates.Completed)]
    public void Task_states_map_to_stable_today_states(WorkTaskStatus source, string expected) =>
        Assert.Equal(expected, TodayAgentStateMapper.FromTask(source));

    [Theory]
    [InlineData("running", TodayAgentStates.Working)]
    [InlineData("needs_review", TodayAgentStates.Recommended)]
    [InlineData("blocked", TodayAgentStates.Blocked)]
    [InlineData("failed", TodayAgentStates.Blocked)]
    [InlineData("completed", TodayAgentStates.Completed)]
    [InlineData("requested", TodayAgentStates.Monitoring)]
    public void Agent_run_states_map_to_stable_today_states(string source, string expected) =>
        Assert.Equal(expected, TodayAgentStateMapper.FromAgentRun(source));

    [Theory]
    [InlineData(ApprovalRequestStatus.Pending, TodayAgentStates.NeedsUser)]
    [InlineData(ApprovalRequestStatus.Approved, TodayAgentStates.Completed)]
    [InlineData(ApprovalRequestStatus.Rejected, TodayAgentStates.Blocked)]
    public void Approval_states_map_to_stable_today_states(ApprovalRequestStatus source, string expected) =>
        Assert.Equal(expected, TodayAgentStateMapper.FromApproval(source));

    [Fact]
    public void Normalized_state_catalog_contains_every_supported_user_facing_state()
    {
        Assert.Equal(6, TodayAgentStates.All.Count);
        Assert.Contains(TodayAgentStates.Monitoring, TodayAgentStates.All);
        Assert.Contains(TodayAgentStates.Working, TodayAgentStates.All);
        Assert.Contains(TodayAgentStates.Recommended, TodayAgentStates.All);
        Assert.Contains(TodayAgentStates.NeedsUser, TodayAgentStates.All);
        Assert.Contains(TodayAgentStates.Blocked, TodayAgentStates.All);
        Assert.Contains(TodayAgentStates.Completed, TodayAgentStates.All);
    }

    [Fact]
    public void Task_workflow_approval_and_audit_evidence_deduplicate_to_the_most_actionable_state()
    {
        var now = DateTime.UtcNow;
        var evidence = new[]
        {
            Candidate("task:1", TodayAgentStates.Monitoring, "task", now),
            Candidate("task:1", TodayAgentStates.Working, "workflow", now.AddMinutes(1)),
            Candidate("task:1", TodayAgentStates.NeedsUser, "approval", now.AddMinutes(2)),
            Candidate("task:1", TodayAgentStates.Completed, "audit", now.AddMinutes(3))
        };

        var selected = TodayAgentActivityQueryService.Deduplicate(evidence);

        var update = Assert.Single(selected).Update;
        Assert.Equal(TodayAgentStates.NeedsUser, update.AgentState);
        Assert.Equal("approval", update.EvidenceSourceType);
    }

    [Fact]
    public void Visibility_reasons_cover_primary_executive_and_direct_involvement()
    {
        var agentId = Guid.NewGuid();
        var resolution = Resolution(agentId, isPrimary: true, executive: false);
        Assert.Contains("own", TodayAgentActivityQueryService.VisibilityReason(agentId, resolution));

        resolution = Resolution(agentId, isPrimary: false, executive: true);
        Assert.Contains("executive oversight", TodayAgentActivityQueryService.VisibilityReason(agentId, resolution));
        Assert.Contains("directly involved", TodayAgentActivityQueryService.VisibilityReason(agentId, resolution, directlyInvolved: true));
    }

    private static TodayAgentActivityQueryService.ActivityCandidate Candidate(
        string deduplicationKey, string state, string source, DateTime updatedUtc) => new(deduplicationKey,
        new TodayWorkspaceAgentUpdateDto(source, source, source, "Agent", updatedUtc, source, "/work",
            AgentState: state, UpdatedUtc: updatedUtc));

    private static TodayWorkspaceLensResolution Resolution(Guid agentId, bool isPrimary, bool executive)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        return new(companyId, userId, membershipId, CompanyMembershipRole.Owner, "Example", "finance", "finance", "r1",
            [new TodayWorkspaceLensAccess("finance", "Finance", "Reason", isPrimary, executive, membershipId, "Owner", "Finley", agentId)]);
    }
}
