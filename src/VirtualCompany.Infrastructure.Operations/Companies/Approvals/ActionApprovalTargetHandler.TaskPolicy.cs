using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class ActionApprovalTargetHandler
{
    private async Task ReconcileReviewedTaskPolicyAsync(ApprovalRequest approval, CancellationToken ct)
    {
        if (approval.TargetEntityType != "action" || approval.Status == ApprovalRequestStatus.Pending)
            return;
        var attempt = await _dbContext.ToolExecutionAttempts.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        if (attempt.TaskId is not Guid taskId)
            return;
        var task = await _dbContext.WorkTasks.SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.Id == taskId, ct);
        if (task is null || task.Status != WorkTaskStatus.AwaitingApproval && !(task.Status == WorkTaskStatus.New && task.OutputPayload.GetValueOrDefault("reviewedActionQueue")is JsonObject queued && queued["approvalId"]?.ToString() == approval.Id.ToString("D")))
            return;
        var type = TaskTypePolicyCatalogue.All.SingleOrDefault(x => !x.Finance && x.Code == task.Type && x.ToolName == attempt.ToolName);
        // Only the bounded catalogue's single retained action can settle this task.
        // A reassigned queued task still owns this exact receipt. Revalidation denies
        // transfer before execution; settle that denial instead of leaving a false queue.
        if (type is null || !task.InputPayload.ContainsKey("taskPolicyVersion"))
            return;
        if (attempt.Status is not (ToolExecutionStatus.Executed or ToolExecutionStatus.Denied or ToolExecutionStatus.Failed or ToolExecutionStatus.Rejected or ToolExecutionStatus.ReconciliationRequired))
            return;
        var executed = attempt.Status == ToolExecutionStatus.Executed && approval.Status == ApprovalRequestStatus.Approved;
        var retainedOutput = CloneNodes(task.OutputPayload);
        retainedOutput["reviewedAction"] = JsonSerializer.SerializeToNode(attempt.ResultPayload);
        task.UpdateStatus(executed ? WorkTaskStatus.Completed : WorkTaskStatus.Blocked, retainedOutput, executed ? "The reviewed internal action executed and its output is retained. Customer delivery remains separate." : attempt.Status == ToolExecutionStatus.ReconciliationRequired ? "The admitted internal action has an uncertain outcome. Reconcile the owning output before recovery." : "The reviewed internal action did not execute. Review the current policy and retained decision.");
        var dispatch = await _dbContext.OperatingDispatches.SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.TaskId == taskId && x.Status == OperatingDispatchStatus.AwaitingApproval, ct);
        dispatch?.ResolveInternalTaskReview(attempt.Status == ToolExecutionStatus.ReconciliationRequired ? null : executed, DateTime.UtcNow);
    }

    internal async Task DispatchReviewedTaskPolicyAsync(ReviewedTaskPolicyMessage message, CancellationToken ct)
    {
        var approval = await _dbContext.ApprovalRequests.SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.ApprovalId && x.TargetEntityType == "action" && x.TargetEntityId == message.AttemptId, ct) ?? throw new CompanyOutboxPermanentException("The exact reviewed action is unavailable.");
        var attempt = await _dbContext.ToolExecutionAttempts.SingleAsync(x => x.CompanyId == message.CompanyId && x.Id == message.AttemptId, ct);
        if (attempt.Status != ToolExecutionStatus.AwaitingApproval)
            return;
        var policy = BuildApprovedApprovalPolicyDecision(approval);
        try
        {
            if (!approval.CanExecuteGuardedAction)
                throw new UnauthorizedAccessException("The approval is no longer executable.");
            var task = await _dbContext.WorkTasks.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == attempt.TaskId, ct) ?? throw new UnauthorizedAccessException("The retained task is unavailable.");
            if (!TaskTypePolicyCatalogue.All.Any(x => !x.Finance && x.Code == task.Type && x.ToolName == attempt.ToolName) || !task.InputPayload.ContainsKey("taskPolicyVersion"))
                throw new UnauthorizedAccessException("The task no longer matches its reviewed catalogue action.");
            if (task.Status != WorkTaskStatus.New || task.OutputPayload.GetValueOrDefault("reviewedActionQueue")is not JsonObject queue || queue["approvalId"]?.ToString() != approval.Id.ToString("D") || queue["attemptId"]?.ToString() != attempt.Id.ToString("D"))
                throw new UnauthorizedAccessException("The retained task no longer waits for this exact approved action.");
            var reviewed = approval.DecisionChain.GetValueOrDefault("lastReviewedMaterialHash")?.ToString();
            if (string.IsNullOrWhiteSpace(reviewed) || reviewed != await _materialHasher.ComputeHashAsync(approval, ct))
                throw new UnauthorizedAccessException("The action material changed after review.");
            var execution = _serviceProvider.GetRequiredService<CompanyAgentToolExecutionService>();
            await execution.RequirePersistedTaskActorAsync(message.CompanyId, attempt.AgentId, attempt.TaskId, approval.RequestedByUserId, ct);
            var authority = await _serviceProvider.GetRequiredService<IAgentEffectiveAuthorityResolver>().ResolveAsync(message.CompanyId, attempt.AgentId, ct);
            var tool = authority.Find(attempt.ToolName, attempt.ActionType, attempt.Scope);
            if (tool is null || tool.State is not (AgentCapabilityStates.Available or AgentCapabilityStates.ApprovalRequired))
                throw new UnauthorizedAccessException("Current agent tool permissions no longer authorize the reviewed action.");
            var result = await _serviceProvider.GetRequiredService<ICompanyToolExecutor>().ExecuteAsync(new ToolExecutionRequest(message.CompanyId, attempt.AgentId, attempt.ToolName, attempt.ActionType, attempt.Scope, CloneNodes(attempt.RequestPayload), attempt.TaskId, attempt.WorkflowInstanceId, attempt.CorrelationId, attempt.Id, attempt.ToolVersion, approval.RequestedByUserId), ct);
            // A draft-success schema can wrap a native refusal lacking a draft as a
            // schema denial. Keep its exact admission conflict truthful as well.
            var admissionConflict = result.ErrorCode == "execution_already_admitted" || result.ErrorCode == "output_payload_schema_validation_failed" && result.Payload.GetValueOrDefault("responsePayload")is JsonObject response && response["errorCode"]?.ToString() == "execution_already_admitted";
            if (admissionConflict)
                attempt.MarkReconciliationRequired(policy, result.ToStructuredPayload(), "The admitted internal step has no safe replay. Review its owning output.");
            else if (result.Status == "denied")
                attempt.MarkDenied(policy, result.ToStructuredPayload(), denialReason: result.ErrorCode);
            else if (!result.Success)
                attempt.MarkFailed(policy, result.ToStructuredPayload());
            else
                attempt.MarkExecuted(policy, result.ToStructuredPayload());
        }
        catch (UnauthorizedAccessException ex)
        {
            var denied = ToolExecutionResult.Failed(attempt.ToolName, attempt.ActionType, "denied", "reviewed_task_authority_changed", ex.Message);
            attempt.MarkDenied(policy, denied.ToStructuredPayload(), denialReason: denied.ErrorCode);
        }

        await ReconcileReviewedTaskPolicyAsync(approval, ct);
        await _auditEventWriter.WriteAsync(new(message.CompanyId, "system", null, "task_policy.reviewed_execution", "tool_execution_attempt", attempt.Id.ToString("N"), attempt.Status.ToStorageValue(), RationaleSummary: "Current material, originating actor, tool permissions, task policy and admission were rechecked after approval committed."), ct);
        await _dbContext.SaveChangesAsync(ct);
    }
}
