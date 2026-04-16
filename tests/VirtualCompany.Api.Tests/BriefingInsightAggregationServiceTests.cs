using System.Text.Json.Nodes;
using VirtualCompany.Application.Briefings;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BriefingInsightAggregationServiceTests
{
    private readonly BriefingInsightAggregationService _service = new();

    [Fact]
    public void Aggregate_groups_by_company_entity_id_before_other_keys()
    {
        var companyId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, topic: "Customer renewal", companyEntityId: entityId, workflowInstanceId: workflowId),
                Contribution(companyId, topic: "Renewal risk", companyEntityId: entityId, workflowInstanceId: Guid.NewGuid())
            ]));

        var section = Assert.Single(result.Sections);
        Assert.Equal(BriefingInsightGroupingTypes.CompanyEntity, section.GroupingType);
        Assert.Equal(entityId.ToString("N"), section.GroupingKey);
        Assert.Equal(2, section.Contributions.Count);
    }

    [Fact]
    public void Aggregate_groups_by_workflow_id_when_company_entity_is_absent()
    {
        var companyId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, topic: "Workflow handoff", workflowInstanceId: workflowId),
                Contribution(companyId, topic: "Workflow status", workflowInstanceId: workflowId)
            ]));

        var section = Assert.Single(result.Sections);
        Assert.Equal(BriefingInsightGroupingTypes.Workflow, section.GroupingType);
        Assert.Equal(workflowId.ToString("N"), section.GroupingKey);
        Assert.Equal(2, section.Contributions.Count);
    }

    [Fact]
    public void Aggregate_groups_by_task_id_when_higher_precedence_keys_are_absent()
    {
        var companyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, topic: "Task progress", taskId: taskId),
                Contribution(companyId, topic: "Task blocker", taskId: taskId)
            ]));

        var section = Assert.Single(result.Sections);
        Assert.Equal(BriefingInsightGroupingTypes.Task, section.GroupingType);
        Assert.Equal(taskId.ToString("N"), section.GroupingKey);
        Assert.Equal(2, section.Contributions.Count);
    }

    [Fact]
    public void Aggregate_groups_by_event_correlation_id_when_entity_keys_are_absent()
    {
        var companyId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, topic: "Event update", eventCorrelationId: " EVT-123 "),
                Contribution(companyId, topic: "Event outcome", eventCorrelationId: "evt-123")
            ]));

        var section = Assert.Single(result.Sections);
        Assert.Equal(BriefingInsightGroupingTypes.EventCorrelation, section.GroupingType);
        Assert.Equal("evt-123", section.GroupingKey);
        Assert.Equal(2, section.Contributions.Count);
    }

    [Fact]
    public void Aggregate_excludes_out_of_scope_company_and_tenant_contributions()
    {
        var companyId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var includedTaskId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            tenantId,
            [
                Contribution(companyId, tenantId: tenantId, topic: "Included", taskId: includedTaskId),
                Contribution(Guid.NewGuid(), tenantId: tenantId, topic: "Wrong company", taskId: Guid.NewGuid()),
                Contribution(companyId, tenantId: Guid.NewGuid(), topic: "Wrong tenant", taskId: Guid.NewGuid())
            ]));

        var section = Assert.Single(result.Sections);
        var contribution = Assert.Single(section.Contributions);
        Assert.Equal(includedTaskId, contribution.TaskId);
        Assert.DoesNotContain(section.Contributions, item => item.Topic == "Wrong company");
        Assert.DoesNotContain(section.Contributions, item => item.Topic == "Wrong tenant");
    }

    [Fact]
    public void Aggregate_preserves_contribution_metadata()
    {
        var companyId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-15), DateTimeKind.Utc);
        var contributionId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            tenantId,
            [
                Contribution(
                    companyId,
                    tenantId: tenantId,
                    agentId: agentId,
                    taskId: taskId,
                    timestampUtc: timestamp,
                    confidence: 0.82m,
                    metadata: new Dictionary<string, JsonNode?> { ["sourceSystem"] = JsonValue.Create("ops") },
                    contributionId: contributionId)
            ]));

        var contribution = Assert.Single(Assert.Single(result.Sections).Contributions);
        Assert.Equal(companyId, contribution.CompanyId);
        Assert.Equal(tenantId, contribution.TenantId);
        Assert.Equal(agentId, contribution.AgentId);
        Assert.Equal(taskId, contribution.TaskId);
        Assert.Equal(timestamp, contribution.TimestampUtc);
        Assert.Equal(0.82m, contribution.Confidence);
        Assert.Equal("ops", contribution.Metadata["sourceSystem"]!.GetValue<string>());
        Assert.Equal(contributionId, contribution.ContributionId);
        Assert.Equal("task", contribution.SourceReference.EntityType);
    }

    [Fact]
    public void Aggregate_marks_conflicts_and_includes_both_viewpoints()
    {
        var companyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var firstAgentId = Guid.NewGuid();
        var secondAgentId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, agentId: firstAgentId, topic: "Vendor renewal", narrative: "Renewal should proceed.", taskId: taskId, assessment: "approve"),
                Contribution(companyId, agentId: secondAgentId, topic: "Vendor renewal", narrative: "Renewal should pause.", taskId: taskId, assessment: "reject")
            ]));

        var section = Assert.Single(result.Sections);
        Assert.True(section.IsConflicting);
        Assert.Equal(2, section.ConflictViewpoints.Count);
        Assert.Contains(section.ConflictViewpoints, viewpoint => viewpoint.Assessment == "approve" && viewpoint.AgentIds.Contains(firstAgentId));
        Assert.Contains(section.ConflictViewpoints, viewpoint => viewpoint.Assessment == "reject" && viewpoint.AgentIds.Contains(secondAgentId));
        Assert.Contains("disagree", section.Narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aggregate_returns_narrative_and_structured_sections()
    {
        var companyId = Guid.NewGuid();

        var result = _service.Aggregate(new BriefingInsightAggregationRequest(
            companyId,
            null,
            [
                Contribution(companyId, topic: "Pipeline", taskId: Guid.NewGuid(), assessment: "on_track")
            ]));

        Assert.Equal(companyId, result.CompanyId);
        Assert.False(string.IsNullOrWhiteSpace(result.NarrativeText));
        Assert.Single(result.Sections);
        Assert.Contains("Pipeline", result.NarrativeText);
    }

    private static BriefingInsightContributionDto Contribution(
        Guid companyId,
        Guid? tenantId = null,
        Guid? agentId = null,
        Guid? companyEntityId = null,
        Guid? workflowInstanceId = null,
        Guid? taskId = null,
        string? eventCorrelationId = null,
        string topic = "Insight",
        string narrative = "The agent reported a briefing insight.",
        string? assessment = "aligned",
        DateTime? timestampUtc = null,
        decimal? confidence = null,
        Dictionary<string, JsonNode?>? metadata = null,
        Guid? contributionId = null)
    {
        var sourceReference = new BriefingSourceReferenceDto(
            taskId.HasValue ? "task" : "insight",
            taskId ?? companyEntityId ?? workflowInstanceId ?? Guid.NewGuid(),
            topic,
            assessment,
            null);

        return new BriefingInsightContributionDto(
            companyId,
            tenantId,
            agentId ?? Guid.NewGuid(),
            sourceReference,
            timestampUtc ?? DateTime.UtcNow,
            confidence,
            companyEntityId,
            workflowInstanceId,
            taskId,
            eventCorrelationId,
            topic,
            narrative,
            assessment,
            metadata ?? [],
            contributionId);
    }
}