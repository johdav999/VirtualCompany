using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class OperatingCycle : ICompanyOwnedEntity
{
    private OperatingCycle() { }
    public OperatingCycle(Guid id, Guid companyId, string triggerType, string? triggerReference, Guid coordinatorAgentId,
        string correlationId, string idempotencyKey, int configurationVersion)
    {
        CompanyId = RequiredId(companyId, nameof(companyId)); CoordinatorAgentId = RequiredId(coordinatorAgentId, nameof(coordinatorAgentId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; TriggerType = Text(triggerType, nameof(triggerType), 64);
        TriggerReference = Optional(triggerReference, 256); CorrelationId = Text(correlationId, nameof(correlationId), 128);
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200); ConfigurationVersion = configurationVersion > 0 ? configurationVersion : throw new ArgumentOutOfRangeException(nameof(configurationVersion));
        Status = OperatingCycleStatus.Requested; RequestedUtc = CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public string? TriggerReference { get; private set; }
    public Guid CoordinatorAgentId { get; private set; }
    public OperatingCycleStatus Status { get; private set; }
    public int ConfigurationVersion { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? SnapshotId { get; private set; }
    public int ModelCallsUsed { get; private set; }
    public int ToolCallsUsed { get; private set; }
    public int TasksCreated { get; private set; }
    public decimal MonetaryBudgetUsed { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent CoordinatorAgent { get; private set; } = null!;
    public ICollection<OperatingPlan> Plans { get; } = new List<OperatingPlan>();

    public void MarkObserving() { Require(OperatingCycleStatus.Requested); Status = OperatingCycleStatus.Observing; StartedUtc ??= DateTime.UtcNow; Touch(); }
    public void MarkPlanning(Guid snapshotId) { Require(OperatingCycleStatus.Observing); SnapshotId = RequiredId(snapshotId, nameof(snapshotId)); Status = OperatingCycleStatus.Planning; Touch(); }
    public void MarkValidating() { Require(OperatingCycleStatus.Planning); Status = OperatingCycleStatus.Validating; Touch(); }
    public void MarkAwaitingReview() { Require(OperatingCycleStatus.Validating); Status = OperatingCycleStatus.AwaitingReview; Touch(); }
    public void Complete() { Require(OperatingCycleStatus.AwaitingReview); Status = OperatingCycleStatus.Completed; CompletedUtc = DateTime.UtcNow; Touch(); }
    public void Fail(string code, string summary) { if (Status is OperatingCycleStatus.Completed or OperatingCycleStatus.Cancelled) throw new InvalidOperationException("Terminal operating cycles cannot fail."); FailureCode = Text(code, nameof(code), 100); FailureSummary = Text(summary, nameof(summary), 2000); Status = OperatingCycleStatus.Failed; CompletedUtc = DateTime.UtcNow; Touch(); }
    public void RecordUsage(int modelCalls, int toolCalls, int tasksCreated, decimal monetaryBudget) { if (modelCalls < 0 || toolCalls < 0 || tasksCreated < 0 || monetaryBudget < 0) throw new ArgumentOutOfRangeException(nameof(modelCalls)); ModelCallsUsed += modelCalls; ToolCallsUsed += toolCalls; TasksCreated += tasksCreated; MonetaryBudgetUsed += monetaryBudget; Touch(); }
    private void Require(OperatingCycleStatus expected) { if (Status != expected) throw new InvalidOperationException($"Operating cycle cannot transition from {Status.ToStorageValue()}."); }
    private void Touch() => UpdatedUtc = DateTime.UtcNow;
    internal static Guid RequiredId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    internal static string Text(string value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); var text = value.Trim(); return text.Length <= max ? text : throw new ArgumentOutOfRangeException(name); }
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class OperatingPlan : ICompanyOwnedEntity
{
    private OperatingPlan() { }
    public OperatingPlan(Guid id, Guid companyId, Guid cycleId, int version, string objective, string rationaleSummary,
        Guid? supersedesPlanId = null, IDictionary<string, JsonNode?>? uncertainty = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); CycleId = OperatingCycle.RequiredId(cycleId, nameof(cycleId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; Version = version; Objective = OperatingCycle.Text(objective, nameof(objective), 2000);
        RationaleSummary = OperatingCycle.Text(rationaleSummary, nameof(rationaleSummary), 4000); SupersedesPlanId = supersedesPlanId;
        Uncertainty = Clone(uncertainty); Status = OperatingPlanStatus.Draft; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid? SupersedesPlanId { get; private set; }
    public int Version { get; private set; }
    public OperatingPlanStatus Status { get; private set; }
    public string Objective { get; private set; } = null!;
    public string RationaleSummary { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Uncertainty { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public DateTime? CommittedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public OperatingCycle Cycle { get; private set; } = null!;
    public OperatingPlan? SupersedesPlan { get; private set; }
    public ICollection<OperatingInitiative> Initiatives { get; } = new List<OperatingInitiative>();
    public ICollection<OperatingDecision> Decisions { get; } = new List<OperatingDecision>();
    public ICollection<OperatingValidationResult> ValidationResults { get; } = new List<OperatingValidationResult>();
    public void SubmitForReview() { Require(OperatingPlanStatus.Draft); Status = OperatingPlanStatus.AwaitingReview; ReviewedUtc = DateTime.UtcNow; Touch(); }
    public void Approve() { Require(OperatingPlanStatus.AwaitingReview); Status = OperatingPlanStatus.Approved; Touch(); }
    public void Reject() { Require(OperatingPlanStatus.AwaitingReview); Status = OperatingPlanStatus.Rejected; Touch(); }
    public void RequestChanges()
    {
        if (Status is not (OperatingPlanStatus.AwaitingReview or OperatingPlanStatus.Rejected))
            throw new InvalidOperationException($"Plan cannot request changes from {Status.ToStorageValue()}.");
        Status = OperatingPlanStatus.ChangesRequested;
        Touch();
    }
    public void BeginCommit() { Require(OperatingPlanStatus.Approved); Status = OperatingPlanStatus.Committing; Touch(); }
    public void MarkCommitted() { Require(OperatingPlanStatus.Committing); Status = OperatingPlanStatus.Committed; CommittedUtc = DateTime.UtcNow; Touch(); }
    public void MarkSuperseded() { if (Status is OperatingPlanStatus.Committed) throw new InvalidOperationException("Committed plans cannot be superseded."); Status = OperatingPlanStatus.Superseded; Touch(); }
    private void Require(OperatingPlanStatus expected) { if (Status != expected) throw new InvalidOperationException($"Plan cannot transition from {Status.ToStorageValue()}."); }
    private void Touch() => UpdatedUtc = DateTime.UtcNow;
    internal static Dictionary<string, JsonNode?> Clone(IDictionary<string, JsonNode?>? source) => source?.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OperatingInitiative : ICompanyOwnedEntity
{
    private OperatingInitiative() { }
    public OperatingInitiative(Guid id, Guid companyId, Guid planId, Guid goalId, string title, string desiredOutcome,
        CompanyGoalPriority priority, string completionEvidence, Guid? ownerAgentId, DateTime? targetUtc, decimal? budget)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); PlanId = OperatingCycle.RequiredId(planId, nameof(planId)); GoalId = OperatingCycle.RequiredId(goalId, nameof(goalId));
        if (ownerAgentId == Guid.Empty || budget is < 0) throw new ArgumentException("Initiative owner and budget must be valid.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; Title = OperatingCycle.Text(title, nameof(title), 200); DesiredOutcome = OperatingCycle.Text(desiredOutcome, nameof(desiredOutcome), 2000);
        Priority = priority; CompletionEvidence = OperatingCycle.Text(completionEvidence, nameof(completionEvidence), 2000); OwnerAgentId = ownerAgentId;
        TargetUtc = targetUtc?.ToUniversalTime(); Budget = budget; Status = OperatingInitiativeStatus.Proposed; CreatedUtc = UpdatedUtc = DateTime.UtcNow; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid GoalId { get; private set; }
    public string Title { get; private set; } = null!;
    public string DesiredOutcome { get; private set; } = null!;
    public CompanyGoalPriority Priority { get; private set; }
    public OperatingInitiativeStatus Status { get; private set; }
    public string CompletionEvidence { get; private set; } = null!;
    public Guid? OwnerAgentId { get; private set; }
    public DateTime? TargetUtc { get; private set; }
    public decimal? Budget { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? WorkflowInstanceId { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public OperatingPlan Plan { get; private set; } = null!;
    public CompanyGoal Goal { get; private set; } = null!;
    public Agent? OwnerAgent { get; private set; }
    public WorkTask? Task { get; private set; }
    public WorkflowInstance? WorkflowInstance { get; private set; }
    public void Approve() { Require(OperatingInitiativeStatus.Proposed); Status = OperatingInitiativeStatus.Approved; Touch(); }
    public void LinkWork(Guid? taskId, Guid? workflowInstanceId) { if (!taskId.HasValue && !workflowInstanceId.HasValue) throw new ArgumentException("A task or workflow is required."); TaskId = taskId; WorkflowInstanceId = workflowInstanceId; Status = OperatingInitiativeStatus.Active; Touch(); }
    public void Complete() { Require(OperatingInitiativeStatus.Active); Status = OperatingInitiativeStatus.Completed; Touch(); }
    public void Block() { if (Status is not (OperatingInitiativeStatus.Approved or OperatingInitiativeStatus.Active)) throw new InvalidOperationException("Only approved or active initiatives can be blocked."); Status = OperatingInitiativeStatus.Blocked; Touch(); }
    private void Require(OperatingInitiativeStatus expected) { if (Status != expected) throw new InvalidOperationException($"Initiative cannot transition from {Status.ToStorageValue()}."); }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class OperatingPlanDependency : ICompanyOwnedEntity
{
    private OperatingPlanDependency() { }
    public OperatingPlanDependency(Guid id, Guid companyId, Guid planId, Guid initiativeId, Guid dependsOnInitiativeId)
    { CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); PlanId = OperatingCycle.RequiredId(planId, nameof(planId)); InitiativeId = OperatingCycle.RequiredId(initiativeId, nameof(initiativeId)); DependsOnInitiativeId = OperatingCycle.RequiredId(dependsOnInitiativeId, nameof(dependsOnInitiativeId)); if (InitiativeId == DependsOnInitiativeId) throw new ArgumentException("An initiative cannot depend on itself."); Id = id == Guid.Empty ? Guid.NewGuid() : id; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid InitiativeId { get; private set; }
    public Guid DependsOnInitiativeId { get; private set; }
    public OperatingPlan Plan { get; private set; } = null!;
}

public sealed class OperatingDecision : ICompanyOwnedEntity
{
    private OperatingDecision() { }
    public OperatingDecision(Guid id, Guid companyId, Guid planId, Guid? initiativeId, OperatingActionClass actionClass,
        string actionType, string targetType, string targetId, Guid? proposedAgentId, string rationaleSummary,
        decimal confidence, string riskLevel, bool approvalRequired, string idempotencyKey, IDictionary<string, JsonNode?>? payload = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); PlanId = OperatingCycle.RequiredId(planId, nameof(planId));
        if (initiativeId == Guid.Empty || proposedAgentId == Guid.Empty || confidence is < 0 or > 1) throw new ArgumentException("Decision references or confidence are invalid.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; InitiativeId = initiativeId; ActionClass = actionClass; ActionType = OperatingCycle.Text(actionType, nameof(actionType), 128);
        TargetType = OperatingCycle.Text(targetType, nameof(targetType), 100); TargetId = OperatingCycle.Text(targetId, nameof(targetId), 200);
        ProposedAgentId = proposedAgentId; RationaleSummary = OperatingCycle.Text(rationaleSummary, nameof(rationaleSummary), 2000); Confidence = confidence;
        RiskLevel = OperatingCycle.Text(riskLevel, nameof(riskLevel), 32); ApprovalRequired = approvalRequired; IdempotencyKey = OperatingCycle.Text(idempotencyKey, nameof(idempotencyKey), 200);
        Payload = OperatingPlan.Clone(payload); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid? InitiativeId { get; private set; }
    public OperatingActionClass ActionClass { get; private set; }
    public string ActionType { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public Guid? ProposedAgentId { get; private set; }
    public string RationaleSummary { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string RiskLevel { get; private set; } = null!;
    public bool ApprovalRequired { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Payload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public OperatingPlan Plan { get; private set; } = null!;
}

public sealed class OperatingValidationResult : ICompanyOwnedEntity
{
    private OperatingValidationResult() { }
    public OperatingValidationResult(Guid id, Guid companyId, Guid planId, Guid? decisionId, string validator,
        string validatorVersion, OperatingValidationOutcome outcome, string reasonCode, string explanation,
        bool approvalRequired, int configurationVersion, IDictionary<string, JsonNode?>? evidence = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); PlanId = OperatingCycle.RequiredId(planId, nameof(planId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; DecisionId = decisionId; Validator = OperatingCycle.Text(validator, nameof(validator), 100);
        ValidatorVersion = OperatingCycle.Text(validatorVersion, nameof(validatorVersion), 32); Outcome = outcome; ReasonCode = OperatingCycle.Text(reasonCode, nameof(reasonCode), 100);
        Explanation = OperatingCycle.Text(explanation, nameof(explanation), 2000); ApprovalRequired = approvalRequired; ConfigurationVersion = configurationVersion;
        Evidence = OperatingPlan.Clone(evidence); EvaluatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid? DecisionId { get; private set; }
    public string Validator { get; private set; } = null!;
    public string ValidatorVersion { get; private set; } = null!;
    public OperatingValidationOutcome Outcome { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public bool ApprovalRequired { get; private set; }
    public int ConfigurationVersion { get; private set; }
    public Dictionary<string, JsonNode?> Evidence { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime EvaluatedUtc { get; private set; }
    public OperatingPlan Plan { get; private set; } = null!;
}

public sealed class OperatingReview : ICompanyOwnedEntity
{
    private OperatingReview() { }
    public OperatingReview(Guid id, Guid companyId, Guid planId, int planVersion, Guid initiativeId,
        OperatingReviewOutcome outcome, string summary, string expectedEvidence, string? actualEvidence,
        string nextAction, string evidenceVersion, decimal? confidence, Guid? reviewerRunId = null,
        IDictionary<string, JsonNode?>? uncertainty = null, IDictionary<string, JsonNode?>? evidence = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); PlanId = OperatingCycle.RequiredId(planId, nameof(planId));
        if (planVersion < 1) throw new ArgumentOutOfRangeException(nameof(planVersion)); InitiativeId = OperatingCycle.RequiredId(initiativeId, nameof(initiativeId));
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence)); Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Outcome = outcome; Summary = OperatingCycle.Text(summary, nameof(summary), 2000); EvidenceVersion = OperatingCycle.Text(evidenceVersion, nameof(evidenceVersion), 100);
        PlanVersion = planVersion; ExpectedEvidence = OperatingCycle.Text(expectedEvidence, nameof(expectedEvidence), 2000);
        ActualEvidence = OperatingCycle.Optional(actualEvidence, 4000); NextAction = OperatingCycle.Text(nextAction, nameof(nextAction), 1000);
        ReviewerRunId = reviewerRunId; Confidence = confidence; Uncertainty = OperatingPlan.Clone(uncertainty); Evidence = OperatingPlan.Clone(evidence); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PlanId { get; private set; }
    public int PlanVersion { get; private set; }
    public Guid InitiativeId { get; private set; }
    public OperatingReviewOutcome Outcome { get; private set; }
    public string Summary { get; private set; } = null!;
    public string ExpectedEvidence { get; private set; } = null!;
    public string? ActualEvidence { get; private set; }
    public string NextAction { get; private set; } = null!;
    public string EvidenceVersion { get; private set; } = null!;
    public decimal? Confidence { get; private set; }
    public Guid? ReviewerRunId { get; private set; }
    public Dictionary<string, JsonNode?> Uncertainty { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Evidence { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public OperatingPlan Plan { get; private set; } = null!;
}

public sealed class OperatingSnapshot : ICompanyOwnedEntity
{
    private OperatingSnapshot() { }
    public OperatingSnapshot(Guid id, Guid companyId, Guid cycleId, string schemaVersion, IDictionary<string, JsonNode?> payload,
        int sourceCount, int dataGapCount, bool truncated)
    { CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); CycleId = OperatingCycle.RequiredId(cycleId, nameof(cycleId)); Id = id == Guid.Empty ? Guid.NewGuid() : id; SchemaVersion = OperatingCycle.Text(schemaVersion, nameof(schemaVersion), 32); Payload = OperatingPlan.Clone(payload); SourceCount = Math.Max(0, sourceCount); DataGapCount = Math.Max(0, dataGapCount); IsTruncated = truncated; CreatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CycleId { get; private set; }
    public string SchemaVersion { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Payload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SourceCount { get; private set; }
    public int DataGapCount { get; private set; }
    public bool IsTruncated { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}
