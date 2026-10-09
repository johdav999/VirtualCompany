using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class ActionApprovalTargetHandler
{
    private static Dictionary<string, JsonNode?> BuildBlockedApprovalResultPayload(ApprovalRequest approval, ToolExecutionAttempt attempt)
    {
        var reasonCode = approval.ExecutionBlockReasonCode ?? PolicyDecisionReasonCodes.ApprovalCancelled;
        var status = approval.Status == ApprovalRequestStatus.Rejected ? ToolExecutionStatus.Rejected.ToStorageValue() : ToolExecutionStatus.Denied.ToStorageValue();
        return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(ToolExecutionResult.SchemaVersion),
            ["success"] = JsonValue.Create(false),
            ["status"] = JsonValue.Create(status),
            ["toolName"] = JsonValue.Create(attempt.ToolName),
            ["actionType"] = JsonValue.Create(attempt.ActionType.ToStorageValue()),
            ["errorCode"] = JsonValue.Create(reasonCode),
            ["errorMessage"] = JsonValue.Create(approval.DecisionSummary ?? "The approval request did not authorize execution."),
            ["approvalRequestId"] = JsonValue.Create(approval.Id),
            ["executionId"] = JsonValue.Create(attempt.Id),
            ["taskId"] = attempt.TaskId.HasValue ? JsonValue.Create(attempt.TaskId.Value) : null,
            ["workflowInstanceId"] = attempt.WorkflowInstanceId.HasValue ? JsonValue.Create(attempt.WorkflowInstanceId.Value) : null
        };
    }

    private static Dictionary<string, JsonNode?> BuildApprovedApprovalPolicyDecision(ApprovalRequest approval)
    {
        var policyDecision = CloneNodes(approval.PolicyDecision);
        var approvalStatus = approval.Status.ToStorageValue();
        policyDecision["outcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Allow);
        policyDecision["approvalRequired"] = JsonValue.Create(false);
        policyDecision["approvalStatus"] = JsonValue.Create(approvalStatus);
        JsonObject metadata;
        if (policyDecision.TryGetValue("metadata", out var metadataNode) && metadataNode is JsonObject existingMetadata)
        {
            metadata = existingMetadata;
        }
        else
        {
            metadata = [];
            policyDecision["metadata"] = metadata;
        }

        metadata["approvalRequestId"] = JsonValue.Create(approval.Id);
        metadata["approvalStatus"] = JsonValue.Create(approvalStatus);
        metadata["executionBlocked"] = JsonValue.Create(false);
        metadata["blockedPendingApproval"] = JsonValue.Create(false);
        metadata["executionState"] = JsonValue.Create(ToolExecutionStatus.Executed.ToStorageValue());
        if (!string.IsNullOrWhiteSpace(approval.DecisionSummary))
        {
            metadata["approvalDecisionSummary"] = JsonValue.Create(approval.DecisionSummary);
        }

        return policyDecision;
    }

    private static Dictionary<string, JsonNode?> BuildBlockedApprovalPolicyDecision(ApprovalRequest approval)
    {
        var policyDecision = CloneNodes(approval.PolicyDecision);
        var reasonCode = approval.ExecutionBlockReasonCode ?? "approval_not_executable";
        var approvalStatus = approval.Status.ToStorageValue();
        policyDecision["outcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Deny);
        policyDecision["approvalStatus"] = JsonValue.Create(approvalStatus);
        JsonObject metadata;
        if (policyDecision.TryGetValue("metadata", out var metadataNode) && metadataNode is JsonObject existingMetadata)
        {
            metadata = existingMetadata;
        }
        else
        {
            metadata = [];
            policyDecision["metadata"] = metadata;
        }

        metadata["approvalRequestId"] = approval.Id;
        metadata["approvalStatus"] = approvalStatus;
        metadata["rejectionComment"] = GetRejectionComment(approval);
        metadata["executionBlockedReason"] = reasonCode;
        JsonArray reasons;
        if (policyDecision.TryGetValue("reasons", out var reasonsNode) && reasonsNode is JsonArray existingReasons)
        {
            reasons = existingReasons;
        }
        else
        {
            reasons = [];
            policyDecision["reasons"] = reasons;
        }

        reasons.Add(new JsonObject { ["code"] = reasonCode, ["category"] = "approval", ["message"] = $"Approval is {approvalStatus} and cannot execute the guarded action." });
        return policyDecision;
    }
}
