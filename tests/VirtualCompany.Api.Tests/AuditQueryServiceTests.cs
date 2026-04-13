using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Context;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AuditQueryServiceTests
{
    [Fact]
    public async Task ListAsync_filters_by_agent_and_company_context()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.Companies.Add(new Company(otherCompanyId, "Other"));
        dbContext.Agents.Add(new Agent(agentId, companyId, "finance", "Finance Agent", "Finance", "Finance", null, AgentSeniority.Mid));
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            companyId,
            AuditActorTypes.Agent,
            agentId,
            AuditEventActions.AgentToolExecutionExecuted,
            AuditTargetTypes.AgentToolExecution,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded,
            "Executed after policy checks passed.",
            ["policy guardrails"]));
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            companyId,
            AuditActorTypes.Agent,
            Guid.NewGuid(),
            AuditEventActions.AgentToolExecutionExecuted,
            AuditTargetTypes.AgentToolExecution,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var result = await service.ListAsync(companyId, new AuditHistoryFilter(AgentId: agentId), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(agentId, result.Items[0].ActorId);
        Assert.Equal("Finance Agent", result.Items[0].ActorLabel);
    }

    [Fact]
    public async Task ListAsync_filters_by_task_workflow_date_range_combined_filters_and_tenant()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var matchingEventId = Guid.NewGuid();
        var windowStart = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc);
        var windowEnd = new DateTime(2026, 4, 11, 23, 59, 59, DateTimeKind.Utc);
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.Companies.Add(new Company(otherCompanyId, "Other"));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            matchingEventId,
            companyId,
            agentId,
            AuditTargetTypes.WorkTask,
            taskId,
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string?>
            {
                ["agentId"] = agentId.ToString(),
                ["workflowInstanceId"] = workflowInstanceId.ToString()
            }));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            Guid.NewGuid(),
            companyId,
            agentId,
            AuditTargetTypes.WorkTask,
            taskId,
            new DateTime(2026, 4, 10, 13, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string?> { ["workflowInstanceId"] = Guid.NewGuid().ToString() }));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            Guid.NewGuid(),
            companyId,
            agentId,
            AuditTargetTypes.WorkflowInstance,
            workflowInstanceId,
            new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string?> { ["taskId"] = Guid.NewGuid().ToString() }));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            Guid.NewGuid(),
            companyId,
            agentId,
            AuditTargetTypes.WorkTask,
            taskId,
            new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string?> { ["workflowInstanceId"] = workflowInstanceId.ToString() }));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            Guid.NewGuid(),
            otherCompanyId,
            agentId,
            AuditTargetTypes.WorkTask,
            taskId,
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string?> { ["workflowInstanceId"] = workflowInstanceId.ToString() }));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var allForTenant = await service.ListAsync(companyId, new AuditHistoryFilter(), CancellationToken.None);
        var taskResults = await service.ListAsync(companyId, new AuditHistoryFilter(TaskId: taskId), CancellationToken.None);
        var workflowResults = await service.ListAsync(companyId, new AuditHistoryFilter(WorkflowInstanceId: workflowInstanceId), CancellationToken.None);
        var dateResults = await service.ListAsync(companyId, new AuditHistoryFilter(FromUtc: windowStart, ToUtc: windowEnd), CancellationToken.None);
        var combinedResults = await service.ListAsync(
            companyId,
            new AuditHistoryFilter(agentId, taskId, workflowInstanceId, windowStart, windowEnd),
            CancellationToken.None);

        Assert.Equal(4, allForTenant.TotalCount);
        Assert.All(allForTenant.Items, item => Assert.Equal(companyId, item.CompanyId));
        Assert.Equal(3, taskResults.TotalCount);
        Assert.Equal(3, workflowResults.TotalCount);
        Assert.Equal(3, dateResults.TotalCount);
        var item = Assert.Single(combinedResults.Items);
        Assert.Equal(matchingEventId, item.Id);
    }

    [Fact]
    public async Task ListAsync_rejects_invalid_date_range()
    {
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(
            companyId,
            new AuditHistoryFilter(FromUtc: new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc), ToUtc: new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_rejects_active_membership_without_audit_review_role()
    {
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.AuditEvents.Add(CreateAuditEvent(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            AuditTargetTypes.WorkTask,
            Guid.NewGuid(),
            DateTime.UtcNow));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid(), CompanyMembershipRole.Employee));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(companyId, new AuditHistoryFilter(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_does_not_return_cross_tenant_audit_events()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.Companies.Add(new Company(otherCompanyId, "Other"));
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            otherCompanyId,
            AuditActorTypes.System,
            null,
            AuditEventActions.WorkflowInstanceStarted,
            AuditTargetTypes.WorkflowInstance,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(companyId, auditEventId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_preserves_summary_without_raw_reasoning_fields()
    {
        var companyId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            AuditActorTypes.Agent,
            Guid.NewGuid(),
            AuditEventActions.SingleAgentTaskOrchestrationExecuted,
            AuditTargetTypes.WorkTask,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded,
            "The task was completed using the approved data source.",
            ["knowledge document"],
            metadata: new Dictionary<string, string?> { ["policyVersion"] = "task_8_3_7" },
            dataSourcesUsed: [new AuditDataSourceUsed("document", "runbook-1", "Runbook", "kb://runbook-1")]));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var detail = await service.GetAsync(companyId, auditEventId, CancellationToken.None);

        Assert.Equal("The task was completed using the approved data source.", detail.RationaleSummary);
        Assert.Equal("The task was completed using the approved data source.", detail.Explanation.Summary);
        Assert.Equal("succeeded", detail.Explanation.Outcome);
        Assert.Contains("Runbook", detail.Explanation.DataSources);
        Assert.Contains(detail.SourceReferences, source =>
            source.Label == "Document: Runbook" &&
            source.DisplayName == "Runbook" &&
            source.Type == "document" &&
            source.Reference == "kb://runbook-1");
        Assert.Empty(detail.LinkedApprovals);
        Assert.Empty(detail.LinkedToolExecutions);
        Assert.DoesNotContain(detail.Metadata.Keys, key => key.Contains("chainOfThought", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_and_GetAsync_return_concise_safe_explanations()
    {
        var companyId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var longSummary = string.Concat(
            "Completed the task using the approved customer record and finance policy. ",
            "This extra operational context is intentionally long so the audit surface trims it for review without exposing verbose internal notes. ",
            "The reviewer can use linked references for follow-up.");
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            AuditActorTypes.Agent,
            Guid.NewGuid(),
            AuditEventActions.SingleAgentTaskOrchestrationExecuted,
            AuditTargetTypes.WorkTask,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded,
            longSummary,
            ["finance policy", "customer record"],
            metadata: new Dictionary<string, string?>
            {
                ["policyVersion"] = "task_12_2_4",
                ["rawReasoning"] = "chain-of-thought: hidden deliberation",
                ["notes"] = "scratchpad: private reasoning"
            }));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var history = await service.ListAsync(companyId, new AuditHistoryFilter(), CancellationToken.None);
        var detail = await service.GetAsync(companyId, auditEventId, CancellationToken.None);

        var item = Assert.Single(history.Items);
        Assert.True(item.Explanation.Summary.Length <= 240);
        Assert.True(detail.Explanation.Summary.Length <= 240);
        Assert.Contains("finance policy", detail.Explanation.DataSources);
        Assert.Contains("customer record", detail.Explanation.DataSources);
        Assert.Equal("task_12_2_4", detail.Metadata["policyVersion"]);
        Assert.DoesNotContain(detail.Metadata.Keys, key => key.Contains("reasoning", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detail.Metadata.Keys, key => key.Contains("notes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(JsonSerializer.Serialize(detail), "chain-of-thought", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(JsonSerializer.Serialize(detail), "scratchpad", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeAuditExplanationMapper_uses_fallback_for_missing_or_raw_reasoning_summary()
    {
        var missing = SafeAuditExplanationMapper.Build(
            AuditEventActions.AgentToolExecutionExecuted,
            AuditEventOutcomes.Succeeded,
            null,
            ["policy guardrails"]);
        var unsafeSummary = SafeAuditExplanationMapper.Build(
            AuditEventActions.AgentToolExecutionExecuted,
            AuditEventOutcomes.Succeeded,
            "chain-of-thought: I considered hidden alternatives before doing the action.",
            ["policy guardrails"]);

        Assert.Equal(SafeAuditExplanationMapper.FallbackSummary, missing.Summary);
        Assert.Equal(SafeAuditExplanationMapper.FallbackSummary, unsafeSummary.Summary);
        Assert.Contains("policy guardrails", unsafeSummary.DataSources);
        Assert.DoesNotContain("chain-of-thought", unsafeSummary.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_returns_linked_approvals_tool_executions_and_affected_entities_for_action_detail()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 13, 9, 30, 0, DateTimeKind.Utc);
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.AddRange(new Company(companyId, "Acme"), new Company(otherCompanyId, "Other"));
        dbContext.Agents.Add(new Agent(agentId, companyId, "finance", "Finance Agent", "Finance", "Finance", null, AgentSeniority.Mid));
        dbContext.WorkTasks.Add(new WorkTask(
            taskId,
            companyId,
            "approval",
            "Approve vendor payment",
            null,
            WorkTaskPriority.High,
            agentId,
            null,
            AuditActorTypes.Agent,
            agentId,
            workflowInstanceId: workflowInstanceId,
            correlationId: "corr-audit-detail"));

        var execution = new ToolExecutionAttempt(
            executionId,
            companyId,
            agentId,
            "payments",
            ToolActionType.Execute,
            "finance.payments",
            taskId: taskId,
            workflowInstanceId: workflowInstanceId,
            correlationId: "corr-audit-detail",
            startedAtUtc: now);
        var approval = ApprovalRequest.CreateForTarget(
            approvalId,
            companyId,
            ApprovalTargetEntityType.Action,
            executionId,
            AuditActorTypes.Agent,
            agentId,
            "threshold",
            Payload(("amount", JsonValue.Create(25000))),
            "owner",
            null,
            []);
        execution.MarkAwaitingApproval(approvalId, Payload(("outcome", JsonValue.Create("require_approval"))), completedAtUtc: now.AddMinutes(1));
        dbContext.ToolExecutionAttempts.Add(execution);
        dbContext.ApprovalRequests.Add(approval);
        dbContext.ToolExecutionAttempts.Add(new ToolExecutionAttempt(
            Guid.NewGuid(),
            otherCompanyId,
            Guid.NewGuid(),
            "payments",
            ToolActionType.Execute,
            "finance.payments",
            taskId: taskId,
            workflowInstanceId: workflowInstanceId,
            correlationId: "corr-audit-detail",
            startedAtUtc: now));
        dbContext.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(
            Guid.NewGuid(),
            otherCompanyId,
            ApprovalTargetEntityType.Action,
            executionId,
            AuditActorTypes.Agent,
            Guid.NewGuid(),
            "threshold",
            Payload(("amount", JsonValue.Create(25000))),
            "owner",
            null,
            []));
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            AuditActorTypes.Agent,
            agentId,
            AuditEventActions.AgentToolExecutionApprovalRequested,
            AuditTargetTypes.AgentToolExecution,
            executionId.ToString(),
            AuditEventOutcomes.Pending,
            "The payment exceeded the approval threshold.",
            metadata: new Dictionary<string, string?>
            {
                ["taskId"] = taskId.ToString(),
                ["workflowInstanceId"] = workflowInstanceId.ToString(),
                ["approvalRequestId"] = approvalId.ToString()
            },
            correlationId: "corr-audit-detail",
            occurredUtc: now));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var detail = await service.GetAsync(companyId, auditEventId, CancellationToken.None);

        var linkedApproval = Assert.Single(detail.LinkedApprovals);
        Assert.Equal(approvalId, linkedApproval.Id);
        Assert.Equal("threshold", linkedApproval.ApprovalType);
        var linkedExecution = Assert.Single(detail.LinkedToolExecutions);
        Assert.Equal(executionId, linkedExecution.Id);
        Assert.Equal("payments", linkedExecution.ToolName);
        Assert.Equal("Finance Agent", linkedExecution.AgentLabel);
        Assert.Contains(detail.AffectedEntities, entity => entity.EntityType == AuditTargetTypes.WorkTask && entity.EntityId == taskId.ToString());
        Assert.Contains(detail.AffectedEntities, entity => entity.EntityType == AuditTargetTypes.AgentToolExecution && entity.EntityId == executionId.ToString());
        Assert.Contains(detail.AffectedEntities, entity => entity.EntityType == AuditTargetTypes.ApprovalRequest && entity.EntityId == approvalId.ToString());
    }

    [Fact]
    public async Task GetAsync_returns_human_readable_source_references_without_displaying_opaque_ids()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var toolExecutionId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        dbContext.Agents.Add(new Agent(agentId, companyId, "finance", "Finance Agent", "Finance", "Finance", null, AgentSeniority.Mid));
        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Employee Onboarding SOP",
            CompanyKnowledgeDocumentType.Policy,
            "tenant/acme/private/opaque-file.pdf",
            "s3://tenant/acme/private/opaque-file.pdf",
            "onboarding.pdf",
            "application/pdf",
            ".pdf",
            1200,
            accessScope: CompanyKnowledgeDocumentAccessScope.CompanyWide(companyId));
        var chunk = new CompanyKnowledgeChunk(
            chunkId,
            companyId,
            documentId,
            1,
            2,
            "New hire equipment requests require manager approval.",
            "embedding",
            "test",
            "test-model",
            null,
            3,
            sourceReference: "chunk:2");
        var task = new WorkTask(
            taskId,
            companyId,
            "operations",
            "Investigate failed invoice sync",
            null,
            WorkTaskPriority.Medium,
            agentId,
            null,
            AuditActorTypes.Agent,
            agentId,
            workflowInstanceId: workflowInstanceId);
        var workflowDefinition = new WorkflowDefinition(
            workflowDefinitionId,
            companyId,
            "FIN-DAILY",
            "Daily finance reconciliation",
            "Finance",
            WorkflowTriggerType.Manual,
            1,
            new Dictionary<string, JsonNode?> { ["steps"] = new JsonArray() });
        var workflow = new WorkflowInstance(workflowInstanceId, companyId, workflowDefinitionId, null);
        var approval = ApprovalRequest.CreateForTarget(
            approvalId,
            companyId,
            ApprovalTargetEntityType.Task,
            taskId,
            AuditActorTypes.Agent,
            agentId,
            "spend threshold override",
            Payload(("amount", JsonValue.Create(5000))),
            "owner",
            null,
            []);
        var execution = new ToolExecutionAttempt(
            toolExecutionId,
            companyId,
            agentId,
            "invoice_sync",
            ToolActionType.Execute,
            "finance.invoices",
            taskId: taskId,
            workflowInstanceId: workflowInstanceId);
        var conversation = new Conversation(conversationId, companyId, "support", "Support inbox", Guid.NewGuid(), agentId);

        dbContext.CompanyKnowledgeDocuments.Add(document);
        dbContext.CompanyKnowledgeChunks.Add(chunk);
        dbContext.WorkTasks.Add(task);
        dbContext.WorkflowDefinitions.Add(workflowDefinition);
        dbContext.WorkflowInstances.Add(workflow);
        dbContext.ApprovalRequests.Add(approval);
        dbContext.ToolExecutionAttempts.Add(execution);
        dbContext.Conversations.Add(conversation);
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            AuditActorTypes.Agent,
            agentId,
            AuditEventActions.SingleAgentTaskOrchestrationExecuted,
            AuditTargetTypes.WorkTask,
            taskId.ToString(),
            AuditEventOutcomes.Succeeded,
            "Completed using approved company sources.",
            dataSourcesUsed:
            [
                new AuditDataSourceUsed(GroundedContextSourceTypes.KnowledgeChunk, chunkId.ToString(), Reference: "s3://tenant/acme/private/chunk-vector-2"),
                new AuditDataSourceUsed(AuditTargetTypes.WorkTask, taskId.ToString()),
                new AuditDataSourceUsed(AuditTargetTypes.WorkflowInstance, workflowInstanceId.ToString()),
                new AuditDataSourceUsed(AuditTargetTypes.ApprovalRequest, approvalId.ToString()),
                new AuditDataSourceUsed(AuditTargetTypes.AgentToolExecution, toolExecutionId.ToString()),
                new AuditDataSourceUsed("conversation", conversationId.ToString())
            ]));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));

        var detail = await service.GetAsync(companyId, auditEventId, CancellationToken.None);

        Assert.Contains(detail.SourceReferences, source => source.Label == "Document: Employee Onboarding SOP - chunk 3");
        Assert.Contains(detail.SourceReferences, source => source.Label == "Task: Investigate failed invoice sync");
        Assert.Contains(detail.SourceReferences, source => source.Label == "Workflow: Daily finance reconciliation (started)");
        Assert.Contains(detail.SourceReferences, source => source.Label == "Approval: spend threshold override approval (pending)");
        Assert.Contains(detail.SourceReferences, source => source.Label == "Tool execution: invoice_sync execute (started)");
        Assert.Contains(detail.SourceReferences, source => source.Label == "Conversation: Support inbox");
        Assert.DoesNotContain(detail.SourceReferences, source => source.Label.Contains(chunkId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detail.SourceReferences, source => source.SecondaryText?.Contains("s3://", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(detail.Explanation.DataSources, source => source.Contains(chunkId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditSourceReferenceDisplayFormatter_uses_safe_fallback_for_missing_or_restricted_sources()
    {
        var missing = AuditSourceReferenceDisplayFormatter.Format(new AuditDataSourceUsed(
            GroundedContextSourceTypes.KnowledgeChunk,
            Guid.NewGuid().ToString(),
            Reference: "chunk:47"));

        Assert.Equal("Document: Document (deleted or inaccessible)", missing.Label);
        Assert.Equal("Section excerpt", missing.SecondaryText);
        Assert.DoesNotContain("chunk:47", missing.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_redacts_restricted_document_sources_when_reviewer_cannot_access_linked_knowledge()
    {
        var companyId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Acme"));
        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            "Employee-only HR Plan",
            CompanyKnowledgeDocumentType.Policy,
            "tenant/acme/hr-plan.pdf",
            "s3://tenant/acme/hr-plan.pdf",
            "hr-plan.pdf",
            "application/pdf",
            ".pdf",
            1200,
            accessScope: new CompanyKnowledgeDocumentAccessScope(
                companyId,
                CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
                new Dictionary<string, JsonNode?>
                {
                    ["allowed_roles"] = new JsonArray("employee")
                }));
        dbContext.CompanyKnowledgeDocuments.Add(document);
        dbContext.CompanyKnowledgeChunks.Add(new CompanyKnowledgeChunk(
            chunkId,
            companyId,
            documentId,
            1,
            0,
            "Restricted employee-only content.",
            "embedding",
            "test",
            "test-model",
            null,
            1,
            sourceReference: "chunk:1"));
        dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            AuditActorTypes.Agent,
            Guid.NewGuid(),
            AuditEventActions.SingleAgentTaskOrchestrationExecuted,
            AuditTargetTypes.WorkTask,
            Guid.NewGuid().ToString(),
            AuditEventOutcomes.Succeeded,
            "Completed using available company data.",
            dataSourcesUsed: [new AuditDataSourceUsed(GroundedContextSourceTypes.KnowledgeChunk, chunkId.ToString(), Reference: "s3://tenant/acme/hr-plan.pdf")]));
        await dbContext.SaveChangesAsync();

        var service = new CompanyAuditQueryService(dbContext, new TestCompanyContextAccessor(companyId, Guid.NewGuid(), CompanyMembershipRole.Manager));

        var detail = await service.GetAsync(companyId, auditEventId, CancellationToken.None);

        Assert.DoesNotContain(detail.SourceReferences, source => source.Label.Contains("Employee-only HR Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detail.Explanation.DataSources, source => source.Contains("Employee-only HR Plan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detail.SourceReferences, source => source.Label == "Document: Document (deleted or inaccessible)");
    }

    private static AuditEvent CreateAuditEvent(
        Guid id,
        Guid companyId,
        Guid agentId,
        string targetType,
        Guid targetId,
        DateTime occurredUtc,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(
            id,
            companyId,
            AuditActorTypes.Agent,
            agentId,
            AuditEventActions.SingleAgentTaskOrchestrationExecuted,
            targetType,
            targetId.ToString(),
            AuditEventOutcomes.Succeeded,
            "Concise operational summary.",
            metadata: metadata,
            occurredUtc: occurredUtc);

    private static VirtualCompanyDbContext CreateDbContext(Guid companyId)
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(companyId, Guid.NewGuid()));
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(
            Guid? companyId,
            Guid? userId,
            CompanyMembershipRole membershipRole = CompanyMembershipRole.Manager,
            CompanyMembershipStatus status = CompanyMembershipStatus.Active)
        {
            CompanyId = companyId;
            UserId = userId;
            Membership = companyId.HasValue && userId.HasValue
                ? new ResolvedCompanyMembershipContext(
                    Guid.NewGuid(),
                    companyId.Value,
                    userId.Value,
                    "Test Company",
                    membershipRole,
                    status)
                : null;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; }
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
        }
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
}