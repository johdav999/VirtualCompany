using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ToolExecutionAttempt : ICompanyOwnedEntity
{
    private const int ToolNameMaxLength = 100;
    private const int ToolVersionMaxLength = 32;
    private const int ScopeMaxLength = 100;
    private const int DenialReasonMaxLength = 512;

    private ToolExecutionAttempt()
    {
    }

    public ToolExecutionAttempt(
        Guid id,
        Guid companyId,
        Guid agentId,
        string toolName,
        ToolActionType actionType,
        string? scope,
        IDictionary<string, JsonNode?>? requestPayload = null,
        Guid? taskId = null,
        Guid? workflowInstanceId = null,
        string? correlationId = null,
        DateTime? startedAtUtc = null,
        DateTime? completedAtUtc = null,
        string? toolVersion = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));
        }

        if (workflowInstanceId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowInstanceId cannot be empty.", nameof(workflowInstanceId));
        }

        ToolActionTypeValues.EnsureSupported(actionType, nameof(actionType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        ToolName = NormalizeRequired(toolName, nameof(toolName), ToolNameMaxLength);
        ToolVersion = NormalizeOptional(toolVersion, nameof(toolVersion), ToolVersionMaxLength) ?? "1.0.0";
        ActionType = actionType;
        Scope = NormalizeOptional(scope, nameof(scope), ScopeMaxLength);
        TaskId = taskId;
        WorkflowInstanceId = workflowInstanceId;
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        Status = ToolExecutionStatus.Started;
        RequestPayload = CloneNodes(requestPayload);
        PolicyDecision = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        ResultPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        StartedUtc = startedAtUtc ?? DateTime.UtcNow;
        CompletedUtc = completedAtUtc;
        CreatedUtc = StartedUtc;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string ToolName { get; private set; } = null!;
    public string ToolVersion { get; private set; } = "1.0.0";
    public ToolActionType ActionType { get; private set; }
    public string? Scope { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public string? CorrelationId { get; private set; }
    public ToolExecutionStatus Status { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Dictionary<string, JsonNode?> RequestPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ResultPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DenialReason { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void MarkDenied(IDictionary<string, JsonNode?>? policyDecision, IDictionary<string, JsonNode?>? resultPayload = null, DateTime? completedAtUtc = null, string? denialReason = null)
    {
        Status = ToolExecutionStatus.Denied;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        DenialReason = NormalizeOptional(denialReason, nameof(denialReason), DenialReasonMaxLength);
        ApprovalRequestId = null;
        CompletedUtc = completedAtUtc ?? DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkRejected(IDictionary<string, JsonNode?>? policyDecision, IDictionary<string, JsonNode?>? resultPayload = null, DateTime? completedAtUtc = null, string? denialReason = null)
    {
        Status = ToolExecutionStatus.Rejected;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        DenialReason = NormalizeOptional(denialReason, nameof(denialReason), DenialReasonMaxLength);
        CompletedUtc = completedAtUtc ?? DateTime.UtcNow;
        ExecutedUtc = null;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkAwaitingApproval(
        Guid approvalRequestId,
        IDictionary<string, JsonNode?>? policyDecision,
        IDictionary<string, JsonNode?>? resultPayload = null,
        DateTime? completedAtUtc = null)
    {
        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        }

        Status = ToolExecutionStatus.AwaitingApproval;
        ApprovalRequestId = approvalRequestId;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        DenialReason = null;
        CompletedUtc = completedAtUtc ?? DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkExecuted(
        IDictionary<string, JsonNode?>? policyDecision,
        IDictionary<string, JsonNode?>? resultPayload,
        DateTime? completedAtUtc = null)
    {
        Status = ToolExecutionStatus.Executed;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        DenialReason = null;
        CompletedUtc = completedAtUtc ?? DateTime.UtcNow;
        ExecutedUtc = CompletedUtc;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(
        IDictionary<string, JsonNode?>? policyDecision,
        IDictionary<string, JsonNode?>? resultPayload,
        DateTime? completedAtUtc = null)
    {
        Status = ToolExecutionStatus.Failed;
        PolicyDecision = CloneNodes(policyDecision);
        ResultPayload = CloneNodes(resultPayload);
        ApprovalRequestId = null;
        CompletedUtc = completedAtUtc ?? DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneRequiredNodes(IDictionary<string, JsonNode?>? nodes, string name)
    {
        var cloned = CloneNodes(nodes);
        if (cloned.Count == 0)
        {
            throw new ArgumentException("Threshold context is required.", name);
        }

        return cloned;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class ApprovalRequest : ICompanyOwnedEntity
{
    private const int ActorTypeMaxLength = 64;
    private const int ApprovalTypeMaxLength = 100;
    private const int RequiredRoleMaxLength = 100;
    private const int DecisionSummaryMaxLength = 2000;
    private const int ToolNameMaxLength = 100;
    private const int ApprovalTargetMaxLength = 100;
    private const string DefaultApprovalType = "threshold";
    private const string LegacyTargetEntityType = "action";
    private const string LegacyRequestedByActorType = "user";
    private const string LegacyRequiredApproverRole = "owner";
    private readonly List<ApprovalStep> _steps = [];

    private ApprovalRequest()
    {
    }

    public ApprovalRequest(
        Guid id,
        Guid companyId,
        Guid agentId,
        Guid toolExecutionAttemptId,
        Guid requestedByUserId,
        string toolName,
        ToolActionType actionType,
        string? approvalTarget,
        IDictionary<string, JsonNode?>? thresholdContext = null,
        IDictionary<string, JsonNode?>? policyDecision = null,
        IDictionary<string, JsonNode?>? decisionChain = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId));
        }

        ToolActionTypeValues.EnsureSupported(actionType, nameof(actionType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        ToolExecutionAttemptId = toolExecutionAttemptId;
        RequestedByUserId = requestedByUserId;
        ToolName = NormalizeRequired(toolName, nameof(toolName), ToolNameMaxLength);
        ActionType = actionType;
        TargetEntityType = LegacyTargetEntityType;
        TargetEntityId = toolExecutionAttemptId;
        RequestedByActorType = LegacyRequestedByActorType;
        RequestedByActorId = requestedByUserId;
        ApprovalType = DefaultApprovalType;
        ApprovalTarget = NormalizeOptional(approvalTarget, nameof(approvalTarget), ApprovalTargetMaxLength);
        Status = ApprovalRequestStatus.Pending;
        ThresholdContext = CloneNodes(thresholdContext);
        if (ThresholdContext.Count == 0)
        {
            throw new ArgumentException("ThresholdContext is required.", nameof(thresholdContext));
        }

        PolicyDecision = CloneNodes(policyDecision);
        DecisionChain = CloneNodes(decisionChain);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
        _steps.Add(new ApprovalStep(Guid.NewGuid(), Id, 1, ApprovalStepApproverType.Role, NormalizeLegacyApprovalRole(approvalTarget)));
    }

    public static ApprovalRequest CreateForTarget(
        Guid id,
        Guid companyId,
        ApprovalTargetEntityType targetEntityType,
        Guid targetEntityId,
        string requestedByActorType,
        Guid requestedByActorId,
        string approvalType,
        IDictionary<string, JsonNode?> thresholdContext,
        string? requiredRole,
        Guid? requiredUserId,
        IEnumerable<ApprovalStepDefinition> steps)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (targetEntityId == Guid.Empty)
        {
            throw new ArgumentException("TargetEntityId is required.", nameof(targetEntityId));
        }

        if (requestedByActorId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByActorId is required.", nameof(requestedByActorId));
        }

        var normalizedSteps = NormalizeSteps(requiredRole, requiredUserId, steps);
        var approval = new ApprovalRequest
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            CompanyId = companyId,
            AgentId = Guid.Empty,
            ToolExecutionAttemptId = targetEntityType == ApprovalTargetEntityType.Action ? targetEntityId : null,
            RequestedByUserId = string.Equals(requestedByActorType, LegacyRequestedByActorType, StringComparison.OrdinalIgnoreCase) ? requestedByActorId : Guid.Empty,
            ToolName = "approval_request",
            ActionType = ToolActionType.Execute,
            TargetEntityType = targetEntityType.ToStorageValue(),
            TargetEntityId = targetEntityId,
            RequestedByActorType = NormalizeRequired(requestedByActorType, nameof(requestedByActorType), ActorTypeMaxLength),
            RequestedByActorId = requestedByActorId,
            ApprovalType = NormalizeRequired(approvalType, nameof(approvalType), ApprovalTypeMaxLength),
            ApprovalTarget = $"{targetEntityType.ToStorageValue()}:{targetEntityId:N}",
            RequiredRole = NormalizeOptional(requiredRole, nameof(requiredRole), RequiredRoleMaxLength),
            RequiredUserId = requiredUserId,
            Status = ApprovalRequestStatus.Pending,
            ThresholdContext = CloneRequiredNodes(thresholdContext, nameof(thresholdContext)),
            PolicyDecision = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            DecisionChain = BuildInitialDecisionChain(targetEntityType, targetEntityId, normalizedSteps),
            CreatedUtc = DateTime.UtcNow
        };
        approval.UpdatedUtc = approval.CreatedUtc;
        approval._steps.AddRange(normalizedSteps.Select(step => new ApprovalStep(Guid.NewGuid(), approval.Id, step.SequenceNo, step.ApproverType, step.ApproverRef)));
        return approval;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid? ToolExecutionAttemptId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string TargetEntityType { get; private set; } = null!;
    public Guid TargetEntityId { get; private set; }
    public string RequestedByActorType { get; private set; } = null!;
    public Guid RequestedByActorId { get; private set; }
    public string ApprovalType { get; private set; } = null!;
    public string ToolName { get; private set; } = null!;
    public ToolActionType ActionType { get; private set; }
    public string? ApprovalTarget { get; private set; }
    public string? RequiredRole { get; private set; }
    public Guid? RequiredUserId { get; private set; }
    public ApprovalRequestStatus Status { get; private set; }
    public string? DecisionSummary { get; private set; }
    public Dictionary<string, JsonNode?> ThresholdContext { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> DecisionChain { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public IReadOnlyCollection<ApprovalStep> Steps => _steps;

    public ApprovalStep? CurrentActionableStep =>
        Status == ApprovalRequestStatus.Pending
            ? _steps.OrderBy(step => step.SequenceNo).FirstOrDefault(step => step.Status == ApprovalStepStatus.Pending)
            : null;

    public bool IsTerminal =>
        Status is ApprovalRequestStatus.Approved
            or ApprovalRequestStatus.Rejected
            or ApprovalRequestStatus.Expired
            or ApprovalRequestStatus.Cancelled;

    public bool CanExecuteGuardedAction => Status == ApprovalRequestStatus.Approved;

    public string? ExecutionBlockReasonCode => Status switch
    {
        ApprovalRequestStatus.Pending => "approval_pending",
        ApprovalRequestStatus.Rejected => "approval_rejected",
        ApprovalRequestStatus.Expired => "approval_expired",
        ApprovalRequestStatus.Cancelled => "approval_cancelled",
        _ => null
    };

    public void SetDecisionChain(IDictionary<string, JsonNode?>? decisionChain)
    {
        DecisionChain = CloneNodes(decisionChain);
        UpdatedUtc = DateTime.UtcNow;
    }

    public ApprovalStep ApproveCurrentStep(Guid stepId, Guid decidedByUserId, string? comment)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approvals can be decided.");
        }

        var currentStep = CurrentActionableStep ?? throw new InvalidOperationException("Approval has no current actionable step.");
        if (currentStep.Id != stepId)
        {
            throw new InvalidOperationException("Only the current approval step can be decided.");
        }

        currentStep.MarkApproved(decidedByUserId, comment);
        if (_steps.All(step => step.Status == ApprovalStepStatus.Approved))
        {
            Status = ApprovalRequestStatus.Approved;
            DecisionSummary = "Approval chain completed.";
            DecidedUtc = DateTime.UtcNow;
        }

        RebuildDecisionChain();
        UpdatedUtc = DateTime.UtcNow;
        return currentStep;
    }

    public ApprovalStep RejectCurrentStep(Guid stepId, Guid decidedByUserId, string? comment)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approvals can be decided.");
        }

        var currentStep = CurrentActionableStep ?? throw new InvalidOperationException("Approval has no current actionable step.");
        if (currentStep.Id != stepId)
        {
            throw new InvalidOperationException("Only the current approval step can be decided.");
        }

        currentStep.MarkRejected(decidedByUserId, comment);
        Status = ApprovalRequestStatus.Rejected;
        DecisionSummary = string.IsNullOrWhiteSpace(comment) ? "Approval chain rejected." : comment.Trim();
        DecidedUtc = DateTime.UtcNow;
        RebuildDecisionChain();
        UpdatedUtc = DateTime.UtcNow;
        return currentStep;
    }

    public void MarkExpired(string? decisionSummary = null)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approvals can expire.");
        }

        Status = ApprovalRequestStatus.Expired;
        DecisionSummary = string.IsNullOrWhiteSpace(decisionSummary) ? "Approval request expired." : decisionSummary.Trim();
        DecidedUtc = DateTime.UtcNow;
        RebuildDecisionChain();
        UpdatedUtc = DecidedUtc.Value;
    }

    public void MarkCancelled(string? decisionSummary = null)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approvals can be cancelled.");
        }

        Status = ApprovalRequestStatus.Cancelled;
        DecisionSummary = string.IsNullOrWhiteSpace(decisionSummary) ? "Approval request cancelled." : decisionSummary.Trim();
        DecidedUtc = DateTime.UtcNow;
        RebuildDecisionChain();
        UpdatedUtc = DecidedUtc.Value;
    }

    private static IReadOnlyList<ApprovalStepDefinition> NormalizeSteps(
        string? requiredRole,
        Guid? requiredUserId,
        IEnumerable<ApprovalStepDefinition> steps)
    {
        if (requiredUserId == Guid.Empty)
        {
            throw new ArgumentException("RequiredUserId cannot be empty.", nameof(requiredUserId));
        }

        var suppliedSteps = steps.ToList();
        if (suppliedSteps.Count > 0 && (!string.IsNullOrWhiteSpace(requiredRole) || requiredUserId.HasValue))
        {
            throw new ArgumentException("Use either top-level required approver fields or ordered approval steps, not both.", nameof(steps));
        }

        if (suppliedSteps.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(requiredRole))
            {
                return [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, requiredRole.Trim())];
            }

            if (requiredUserId.HasValue)
            {
                return [new ApprovalStepDefinition(1, ApprovalStepApproverType.User, requiredUserId.Value.ToString("N"))];
            }

            throw new ArgumentException("At least one approver target is required.", nameof(steps));
        }

        if (suppliedSteps.Any(x => x.SequenceNo <= 0))
        {
            throw new ArgumentException("Approval step sequence numbers must be positive.", nameof(steps));
        }

        if (suppliedSteps.Select(x => x.SequenceNo).Distinct().Count() != suppliedSteps.Count)
        {
            throw new ArgumentException("Approval step sequence numbers must be unique.", nameof(steps));
        }

        return suppliedSteps
            .OrderBy(x => x.SequenceNo)
            .Select(x => new ApprovalStepDefinition(x.SequenceNo, x.ApproverType, NormalizeRequired(x.ApproverRef, nameof(x.ApproverRef), 200)))
            .ToList();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string NormalizeLegacyApprovalRole(string? approvalTarget) =>
        !string.IsNullOrWhiteSpace(approvalTarget) &&
        CompanyMembershipRoles.TryParse(approvalTarget, out var role)
            ? role.ToStorageValue()
            : LegacyRequiredApproverRole;

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneRequiredNodes(IDictionary<string, JsonNode?>? nodes, string name)
    {
        var cloned = CloneNodes(nodes);
        if (cloned.Count == 0)
        {
            throw new ArgumentException("Threshold context is required.", name);
        }

        return cloned;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, JsonNode?> BuildInitialDecisionChain(
        ApprovalTargetEntityType targetEntityType,
        Guid targetEntityId,
        IReadOnlyList<ApprovalStepDefinition> steps) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-12"),
            ["status"] = JsonValue.Create("pending"),
            ["targetEntityType"] = JsonValue.Create(targetEntityType.ToStorageValue()),
            ["targetEntityId"] = JsonValue.Create(targetEntityId),
            ["steps"] = new JsonArray(steps
                .Select(step => new JsonObject
                {
                    ["sequenceNo"] = step.SequenceNo,
                    ["approverType"] = step.ApproverType.ToStorageValue(),
                    ["approverRef"] = step.ApproverRef,
                    ["status"] = ApprovalStepStatus.Pending.ToStorageValue()
                })
                .ToArray<JsonNode?>())
        };

    private void RebuildDecisionChain()
    {
        DecisionChain = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-12"),
            ["status"] = JsonValue.Create(Status.ToStorageValue()),
            ["targetEntityType"] = JsonValue.Create(TargetEntityType),
            ["targetEntityId"] = JsonValue.Create(TargetEntityId),
            ["steps"] = new JsonArray(_steps.OrderBy(step => step.SequenceNo)
                .Select(step => new JsonObject
                {
                    ["sequenceNo"] = step.SequenceNo,
                    ["approverType"] = step.ApproverType.ToStorageValue(),
                    ["approverRef"] = step.ApproverRef,
                    ["status"] = step.Status.ToStorageValue(),
                    ["decidedByUserId"] = step.DecidedByUserId,
                    ["decidedAt"] = step.DecidedUtc,
                    ["comment"] = step.Comment
                })
                .ToArray<JsonNode?>())
        };
    }
}

public sealed record ApprovalStepDefinition(int SequenceNo, ApprovalStepApproverType ApproverType, string ApproverRef);

public sealed class ApprovalStep
{
    private const int ApproverRefMaxLength = 200;
    private const int CommentMaxLength = 2000;

    private ApprovalStep()
    {
    }

    public ApprovalStep(
        Guid id,
        Guid approvalId,
        int sequenceNo,
        ApprovalStepApproverType approverType,
        string approverRef)
    {
        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalId is required.", nameof(approvalId));
        }

        if (sequenceNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNo), "SequenceNo must be positive.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApprovalId = approvalId;
        SequenceNo = sequenceNo;
        ApproverType = approverType;
        ApproverRef = NormalizeRequired(approverRef, nameof(approverRef), ApproverRefMaxLength);
        Status = ApprovalStepStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid ApprovalId { get; private set; }
    public int SequenceNo { get; private set; }
    public ApprovalStepApproverType ApproverType { get; private set; }
    public string ApproverRef { get; private set; } = null!;
    public ApprovalStepStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public string? Comment { get; private set; }
    public ApprovalRequest Approval { get; private set; } = null!;

    public void MarkApproved(Guid decidedByUserId, string? comment)
    {
        MarkDecided(ApprovalStepStatus.Approved, decidedByUserId, comment);
    }

    public void MarkRejected(Guid decidedByUserId, string? comment)
    {
        MarkDecided(ApprovalStepStatus.Rejected, decidedByUserId, comment);
    }

    private void MarkDecided(ApprovalStepStatus status, Guid decidedByUserId, string? comment)
    {
        if (decidedByUserId == Guid.Empty)
        {
            throw new ArgumentException("DecidedByUserId is required.", nameof(decidedByUserId));
        }

        if (Status != ApprovalStepStatus.Pending)
        {
            throw new InvalidOperationException("Approval step has already been decided.");
        }

        Status = status;
        DecidedByUserId = decidedByUserId;
        DecidedUtc = DateTime.UtcNow;
        Comment = NormalizeOptional(comment, nameof(comment), CommentMaxLength);
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}
