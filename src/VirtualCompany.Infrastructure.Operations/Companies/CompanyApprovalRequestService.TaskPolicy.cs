using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

internal sealed record ReviewedTaskPolicyMessage(Guid CompanyId,Guid ApprovalId,Guid AttemptId)
{
    public const string Topic="task_policy.reviewed_execution_requested";
}

public sealed partial class CompanyApprovalRequestService
{
    internal async Task DispatchReviewedTaskPolicyAsync(ReviewedTaskPolicyMessage message,CancellationToken ct)
    {
        var approval=await _dbContext.ApprovalRequests.SingleOrDefaultAsync(x=>x.CompanyId==message.CompanyId&&x.Id==message.ApprovalId&&x.TargetEntityType=="action"&&x.TargetEntityId==message.AttemptId,ct)
            ??throw new CompanyOutboxPermanentException("The exact reviewed action is unavailable.");
        var attempt=await _dbContext.ToolExecutionAttempts.SingleAsync(x=>x.CompanyId==message.CompanyId&&x.Id==message.AttemptId,ct);
        if(attempt.Status!=ToolExecutionStatus.AwaitingApproval)return;
        var policy=BuildApprovedApprovalPolicyDecision(approval);
        try
        {
            if(!approval.CanExecuteGuardedAction)throw new UnauthorizedAccessException("The approval is no longer executable.");
            var task=await _dbContext.WorkTasks.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==message.CompanyId&&x.Id==attempt.TaskId,ct)
                ??throw new UnauthorizedAccessException("The retained task is unavailable.");
            if(!TaskTypePolicyCatalogue.All.Any(x=>!x.Finance&&x.Code==task.Type&&x.ToolName==attempt.ToolName)||!task.InputPayload.ContainsKey("taskPolicyVersion"))
                throw new UnauthorizedAccessException("The task no longer matches its reviewed catalogue action.");
            if(task.Status!=WorkTaskStatus.New||task.OutputPayload.GetValueOrDefault("reviewedActionQueue") is not JsonObject queue||
                queue["approvalId"]?.ToString()!=approval.Id.ToString("D")||queue["attemptId"]?.ToString()!=attempt.Id.ToString("D"))
                throw new UnauthorizedAccessException("The retained task no longer waits for this exact approved action.");
            var reviewed=approval.DecisionChain.GetValueOrDefault("lastReviewedMaterialHash")?.ToString();
            if(string.IsNullOrWhiteSpace(reviewed)||reviewed!=await MaterialHashAsync(approval,ct))throw new UnauthorizedAccessException("The action material changed after review.");
            var execution=_serviceProvider.GetRequiredService<CompanyAgentToolExecutionService>();
            await execution.RequirePersistedTaskActorAsync(message.CompanyId,attempt.AgentId,attempt.TaskId,approval.RequestedByUserId,ct);
            var authority=await _serviceProvider.GetRequiredService<IAgentEffectiveAuthorityResolver>().ResolveAsync(message.CompanyId,attempt.AgentId,ct);
            var tool=authority.Find(attempt.ToolName,attempt.ActionType,attempt.Scope);
            if(tool is null||tool.State is not (AgentCapabilityStates.Available or AgentCapabilityStates.ApprovalRequired))
                throw new UnauthorizedAccessException("Current agent tool permissions no longer authorize the reviewed action.");
            var result=await _serviceProvider.GetRequiredService<ICompanyToolExecutor>().ExecuteAsync(new ToolExecutionRequest(message.CompanyId,attempt.AgentId,attempt.ToolName,attempt.ActionType,attempt.Scope,
                CloneNodes(attempt.RequestPayload),attempt.TaskId,attempt.WorkflowInstanceId,attempt.CorrelationId,attempt.Id,attempt.ToolVersion,approval.RequestedByUserId),ct);
            // A draft-success schema can wrap a native refusal lacking a draft as a
            // schema denial. Keep its exact admission conflict truthful as well.
            var admissionConflict=result.ErrorCode=="execution_already_admitted"||
                result.ErrorCode=="output_payload_schema_validation_failed"&&
                result.Payload.GetValueOrDefault("responsePayload") is JsonObject response&&response["errorCode"]?.ToString()=="execution_already_admitted";
            if(admissionConflict)attempt.MarkReconciliationRequired(policy,result.ToStructuredPayload(),"The admitted internal step has no safe replay. Review its owning output.");
            else if(result.Status=="denied")attempt.MarkDenied(policy,result.ToStructuredPayload(),denialReason:result.ErrorCode);
            else if(!result.Success)attempt.MarkFailed(policy,result.ToStructuredPayload());
            else attempt.MarkExecuted(policy,result.ToStructuredPayload());
        }
        catch(UnauthorizedAccessException ex)
        {
            var denied=ToolExecutionResult.Failed(attempt.ToolName,attempt.ActionType,"denied","reviewed_task_authority_changed",ex.Message);
            attempt.MarkDenied(policy,denied.ToStructuredPayload(),denialReason:denied.ErrorCode);
        }
        await ReconcileReviewedTaskPolicyAsync(approval,ct);
        await _auditEventWriter.WriteAsync(new(message.CompanyId,"system",null,"task_policy.reviewed_execution", "tool_execution_attempt",attempt.Id.ToString("N"),attempt.Status.ToStorageValue(),RationaleSummary:"Current material, originating actor, tool permissions, task policy and admission were rechecked after approval committed."),ct);
        await _dbContext.SaveChangesAsync(ct);
    }
}
