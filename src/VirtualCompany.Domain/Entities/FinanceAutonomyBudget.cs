using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAutonomyUsageValues
{
    private FinanceAutonomyUsageValues() { }

    public FinanceAutonomyUsageValues(int recordsEvaluated, int draftsOrTasksCreated, int executeAttempts,
        decimal amountExposure, long objectBytes, int exportsCreated, int modelCalls, int toolCalls,
        decimal estimatedCost, int retries, int runtimeSeconds)
    {
        if (recordsEvaluated < 0 || draftsOrTasksCreated < 0 || executeAttempts < 0 || amountExposure < 0 ||
            objectBytes < 0 || exportsCreated < 0 || modelCalls < 0 || toolCalls < 0 || estimatedCost < 0 ||
            retries < 0 || runtimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(recordsEvaluated), "Finance autonomy usage cannot be negative.");
        RecordsEvaluated = recordsEvaluated;
        DraftsOrTasksCreated = draftsOrTasksCreated;
        ExecuteAttempts = executeAttempts;
        AmountExposure = amountExposure;
        ObjectBytes = objectBytes;
        ExportsCreated = exportsCreated;
        ModelCalls = modelCalls;
        ToolCalls = toolCalls;
        EstimatedCost = estimatedCost;
        Retries = retries;
        RuntimeSeconds = runtimeSeconds;
    }

    public int RecordsEvaluated { get; private set; }
    public int DraftsOrTasksCreated { get; private set; }
    public int ExecuteAttempts { get; private set; }
    public decimal AmountExposure { get; private set; }
    public long ObjectBytes { get; private set; }
    public int ExportsCreated { get; private set; }
    public int ModelCalls { get; private set; }
    public int ToolCalls { get; private set; }
    public decimal EstimatedCost { get; private set; }
    public int Retries { get; private set; }
    public int RuntimeSeconds { get; private set; }

    public static FinanceAutonomyUsageValues Zero() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public void Add(FinanceAutonomyUsageValues value)
    {
        RecordsEvaluated = checked(RecordsEvaluated + value.RecordsEvaluated);
        DraftsOrTasksCreated = checked(DraftsOrTasksCreated + value.DraftsOrTasksCreated);
        ExecuteAttempts = checked(ExecuteAttempts + value.ExecuteAttempts);
        AmountExposure += value.AmountExposure;
        ObjectBytes = checked(ObjectBytes + value.ObjectBytes);
        ExportsCreated = checked(ExportsCreated + value.ExportsCreated);
        ModelCalls = checked(ModelCalls + value.ModelCalls);
        ToolCalls = checked(ToolCalls + value.ToolCalls);
        EstimatedCost += value.EstimatedCost;
        Retries = checked(Retries + value.Retries);
        RuntimeSeconds = checked(RuntimeSeconds + value.RuntimeSeconds);
    }

    public void Subtract(FinanceAutonomyUsageValues value)
    {
        RecordsEvaluated = Math.Max(0, RecordsEvaluated - value.RecordsEvaluated);
        DraftsOrTasksCreated = Math.Max(0, DraftsOrTasksCreated - value.DraftsOrTasksCreated);
        ExecuteAttempts = Math.Max(0, ExecuteAttempts - value.ExecuteAttempts);
        AmountExposure = Math.Max(0, AmountExposure - value.AmountExposure);
        ObjectBytes = Math.Max(0, ObjectBytes - value.ObjectBytes);
        ExportsCreated = Math.Max(0, ExportsCreated - value.ExportsCreated);
        ModelCalls = Math.Max(0, ModelCalls - value.ModelCalls);
        ToolCalls = Math.Max(0, ToolCalls - value.ToolCalls);
        EstimatedCost = Math.Max(0, EstimatedCost - value.EstimatedCost);
        Retries = Math.Max(0, Retries - value.Retries);
        RuntimeSeconds = Math.Max(0, RuntimeSeconds - value.RuntimeSeconds);
    }

    public FinanceAutonomyUsageValues Copy() => new(RecordsEvaluated, DraftsOrTasksCreated, ExecuteAttempts,
        AmountExposure, ObjectBytes, ExportsCreated, ModelCalls, ToolCalls, EstimatedCost, Retries, RuntimeSeconds);
}

public sealed class FinanceAutonomyUsageLimits
{
    private FinanceAutonomyUsageLimits() { }

    public FinanceAutonomyUsageLimits(int? recordsEvaluated, int? draftsOrTasksCreated, int? executeAttempts,
        decimal? amountExposure, long? objectBytes, int? exportsCreated, int? modelCalls, int? toolCalls,
        decimal? estimatedCost, int? retries, int? runtimeSeconds)
    {
        if (new int?[] { recordsEvaluated, draftsOrTasksCreated, executeAttempts, exportsCreated, modelCalls,
                toolCalls, retries, runtimeSeconds }.Any(x => x < 0) || amountExposure < 0 || objectBytes < 0 || estimatedCost < 0)
            throw new ArgumentOutOfRangeException(nameof(recordsEvaluated), "Finance autonomy limits cannot be negative.");
        RecordsEvaluated = recordsEvaluated;
        DraftsOrTasksCreated = draftsOrTasksCreated;
        ExecuteAttempts = executeAttempts;
        AmountExposure = amountExposure;
        ObjectBytes = objectBytes;
        ExportsCreated = exportsCreated;
        ModelCalls = modelCalls;
        ToolCalls = toolCalls;
        EstimatedCost = estimatedCost;
        Retries = retries;
        RuntimeSeconds = runtimeSeconds;
    }

    public int? RecordsEvaluated { get; private set; }
    public int? DraftsOrTasksCreated { get; private set; }
    public int? ExecuteAttempts { get; private set; }
    public decimal? AmountExposure { get; private set; }
    public long? ObjectBytes { get; private set; }
    public int? ExportsCreated { get; private set; }
    public int? ModelCalls { get; private set; }
    public int? ToolCalls { get; private set; }
    public decimal? EstimatedCost { get; private set; }
    public int? Retries { get; private set; }
    public int? RuntimeSeconds { get; private set; }

    public string? FirstExceeded(FinanceAutonomyUsageValues current, FinanceAutonomyUsageValues addition)
    {
        if (RecordsEvaluated.HasValue && current.RecordsEvaluated + addition.RecordsEvaluated > RecordsEvaluated) return "records_evaluated";
        if (DraftsOrTasksCreated.HasValue && current.DraftsOrTasksCreated + addition.DraftsOrTasksCreated > DraftsOrTasksCreated) return "drafts_tasks";
        if (ExecuteAttempts.HasValue && current.ExecuteAttempts + addition.ExecuteAttempts > ExecuteAttempts) return "execute_attempts";
        if (AmountExposure.HasValue && current.AmountExposure + addition.AmountExposure > AmountExposure) return "amount_exposure";
        if (ObjectBytes.HasValue && current.ObjectBytes + addition.ObjectBytes > ObjectBytes) return "object_bytes";
        if (ExportsCreated.HasValue && current.ExportsCreated + addition.ExportsCreated > ExportsCreated) return "exports";
        if (ModelCalls.HasValue && current.ModelCalls + addition.ModelCalls > ModelCalls) return "model_calls";
        if (ToolCalls.HasValue && current.ToolCalls + addition.ToolCalls > ToolCalls) return "tool_calls";
        if (EstimatedCost.HasValue && current.EstimatedCost + addition.EstimatedCost > EstimatedCost) return "estimated_cost";
        if (Retries.HasValue && current.Retries + addition.Retries > Retries) return "retries";
        if (RuntimeSeconds.HasValue && current.RuntimeSeconds + addition.RuntimeSeconds > RuntimeSeconds) return "runtime_seconds";
        return null;
    }
}

public sealed class FinanceAutonomyBudgetPolicy : ICompanyOwnedEntity
{
    private FinanceAutonomyBudgetPolicy() { }

    public FinanceAutonomyBudgetPolicy(Guid id, Guid companyId, Guid? agentId, string? capabilityId,
        string timezone, int windowMinutes, FinanceAutonomyUsageLimits perRunLimits,
        FinanceAutonomyUsageLimits windowLimits, int policyDenialThreshold, int invalidPlanThreshold,
        int providerAmbiguityThreshold, int errorBurstThreshold, int staleEvidenceThreshold,
        int auditOutboxFailureThreshold, int circuitWindowMinutes, int circuitCooldownMinutes, DateTime nowUtc)
    {
        Id = BudgetValue.Id(id); CompanyId = BudgetValue.Required(companyId, nameof(companyId));
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId cannot be empty.", nameof(agentId));
        AgentId = agentId; CapabilityId = BudgetValue.Optional(capabilityId, 160)?.ToLowerInvariant();
        ScopeKey = CreateScopeKey(agentId, CapabilityId);
        Apply(timezone, windowMinutes, perRunLimits, windowLimits, policyDenialThreshold, invalidPlanThreshold,
            providerAmbiguityThreshold, errorBurstThreshold, staleEvidenceThreshold, auditOutboxFailureThreshold,
            circuitWindowMinutes, circuitCooldownMinutes);
        IsActive = true; CreatedUtc = UpdatedUtc = BudgetValue.Utc(nowUtc); Version = 1;
        RowVersion = BudgetValue.Token();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string? CapabilityId { get; private set; }
    public string ScopeKey { get; private set; } = null!;
    public string Timezone { get; private set; } = null!;
    public int WindowMinutes { get; private set; }
    public FinanceAutonomyUsageLimits PerRunLimits { get; private set; } = null!;
    public FinanceAutonomyUsageLimits WindowLimits { get; private set; } = null!;
    public int PolicyDenialThreshold { get; private set; }
    public int InvalidPlanThreshold { get; private set; }
    public int ProviderAmbiguityThreshold { get; private set; }
    public int ErrorBurstThreshold { get; private set; }
    public int StaleEvidenceThreshold { get; private set; }
    public int AuditOutboxFailureThreshold { get; private set; }
    public int CircuitWindowMinutes { get; private set; }
    public int CircuitCooldownMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;

    public void Update(string timezone, int windowMinutes, FinanceAutonomyUsageLimits perRunLimits,
        FinanceAutonomyUsageLimits windowLimits, int policyDenialThreshold, int invalidPlanThreshold,
        int providerAmbiguityThreshold, int errorBurstThreshold, int staleEvidenceThreshold,
        int auditOutboxFailureThreshold, int circuitWindowMinutes, int circuitCooldownMinutes,
        bool isActive, long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion > 0 && expectedVersion != Version) throw new InvalidOperationException("The Finance autonomy budget policy changed. Refresh and retry.");
        Apply(timezone, windowMinutes, perRunLimits, windowLimits, policyDenialThreshold, invalidPlanThreshold,
            providerAmbiguityThreshold, errorBurstThreshold, staleEvidenceThreshold, auditOutboxFailureThreshold,
            circuitWindowMinutes, circuitCooldownMinutes);
        IsActive = isActive; Touch(nowUtc);
    }

    public static string CreateScopeKey(Guid? agentId, string? capabilityId) =>
        agentId.HasValue && !string.IsNullOrWhiteSpace(capabilityId) ? $"agent:{agentId.Value:N}:capability:{capabilityId.Trim().ToLowerInvariant()}" :
        agentId.HasValue ? $"agent:{agentId.Value:N}" :
        !string.IsNullOrWhiteSpace(capabilityId) ? $"capability:{capabilityId.Trim().ToLowerInvariant()}" : "company";

    private void Apply(string timezone, int windowMinutes, FinanceAutonomyUsageLimits perRunLimits,
        FinanceAutonomyUsageLimits windowLimits, params int[] thresholds)
    {
        Timezone = BudgetValue.Text(timezone, nameof(timezone), 100);
        WindowMinutes = windowMinutes is >= 5 and <= 44640 ? windowMinutes : throw new ArgumentOutOfRangeException(nameof(windowMinutes));
        PerRunLimits = perRunLimits ?? throw new ArgumentNullException(nameof(perRunLimits));
        WindowLimits = windowLimits ?? throw new ArgumentNullException(nameof(windowLimits));
        if (thresholds.Length != 8 || thresholds.Any(x => x is < 1 or > 10000)) throw new ArgumentOutOfRangeException(nameof(thresholds));
        PolicyDenialThreshold = thresholds[0]; InvalidPlanThreshold = thresholds[1];
        ProviderAmbiguityThreshold = thresholds[2]; ErrorBurstThreshold = thresholds[3];
        StaleEvidenceThreshold = thresholds[4]; AuditOutboxFailureThreshold = thresholds[5];
        CircuitWindowMinutes = thresholds[6]; CircuitCooldownMinutes = thresholds[7];
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = BudgetValue.Utc(nowUtc); Version++; RowVersion = BudgetValue.Token(); }
}

public sealed class FinanceAutonomyBudgetWindow : ICompanyOwnedEntity
{
    private FinanceAutonomyBudgetWindow() { }
    public FinanceAutonomyBudgetWindow(Guid id, Guid companyId, Guid policyId, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        Id = BudgetValue.Id(id); CompanyId = BudgetValue.Required(companyId, nameof(companyId));
        PolicyId = BudgetValue.Required(policyId, nameof(policyId)); WindowStartUtc = BudgetValue.Utc(startUtc);
        WindowEndUtc = BudgetValue.Utc(endUtc); if (WindowEndUtc <= WindowStartUtc) throw new ArgumentException("Budget window end must follow start.");
        Reserved = FinanceAutonomyUsageValues.Zero(); Consumed = FinanceAutonomyUsageValues.Zero();
        UpdatedUtc = BudgetValue.Utc(nowUtc); Version = 1; RowVersion = BudgetValue.Token();
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PolicyId { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public FinanceAutonomyUsageValues Reserved { get; private set; } = null!;
    public FinanceAutonomyUsageValues Consumed { get; private set; } = null!;
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public FinanceAutonomyBudgetPolicy Policy { get; private set; } = null!;

    public string? Check(FinanceAutonomyUsageLimits limits, FinanceAutonomyUsageValues addition)
    {
        var total = Consumed.Copy(); total.Add(Reserved);
        return limits.FirstExceeded(total, addition);
    }
    public void Reserve(FinanceAutonomyUsageValues usage, DateTime nowUtc) { Reserved.Add(usage); Touch(nowUtc); }
    public void Reconcile(FinanceAutonomyUsageValues reserved, FinanceAutonomyUsageValues actual, DateTime nowUtc)
    { Reserved.Subtract(reserved); Consumed.Add(actual); Touch(nowUtc); }
    public void Release(FinanceAutonomyUsageValues reserved, DateTime nowUtc) { Reserved.Subtract(reserved); Touch(nowUtc); }
    private void Touch(DateTime nowUtc) { UpdatedUtc = BudgetValue.Utc(nowUtc); Version++; RowVersion = BudgetValue.Token(); }
}

public sealed class FinanceAutonomyBudgetReservation : ICompanyOwnedEntity
{
    private FinanceAutonomyBudgetReservation() { }
    public FinanceAutonomyBudgetReservation(Guid id, Guid companyId, Guid policyId, Guid windowId,
        Guid runId, Guid stepId, int attemptNumber, string reservationKey, FinanceAutonomyUsageValues planned,
        string correlationId, DateTime nowUtc)
    {
        Id = BudgetValue.Id(id); CompanyId = BudgetValue.Required(companyId, nameof(companyId));
        PolicyId = BudgetValue.Required(policyId, nameof(policyId)); WindowId = BudgetValue.Required(windowId, nameof(windowId));
        RunId = BudgetValue.Required(runId, nameof(runId)); StepId = BudgetValue.Required(stepId, nameof(stepId));
        AttemptNumber = attemptNumber > 0 ? attemptNumber : throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        ReservationKey = BudgetValue.Text(reservationKey, nameof(reservationKey), 200);
        Planned = planned.Copy(); Actual = FinanceAutonomyUsageValues.Zero();
        CorrelationId = BudgetValue.Text(correlationId, nameof(correlationId), 128);
        Status = FinanceAutonomyBudgetReservationStatus.Reserved; CreatedUtc = BudgetValue.Utc(nowUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PolicyId { get; private set; }
    public Guid WindowId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid StepId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string ReservationKey { get; private set; } = null!;
    public FinanceAutonomyUsageValues Planned { get; private set; } = null!;
    public FinanceAutonomyUsageValues Actual { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public FinanceAutonomyBudgetReservationStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ReconciledUtc { get; private set; }
    public FinanceAutonomyBudgetPolicy Policy { get; private set; } = null!;
    public FinanceAutonomyBudgetWindow Window { get; private set; } = null!;
    public FinanceAutonomyRun Run { get; private set; } = null!;
    public FinanceAutonomyRunStep Step { get; private set; } = null!;
    public void Reconcile(FinanceAutonomyUsageValues actual, DateTime nowUtc)
    { if (Status != FinanceAutonomyBudgetReservationStatus.Reserved) return; Actual = actual.Copy(); Status = FinanceAutonomyBudgetReservationStatus.Reconciled; ReconciledUtc = BudgetValue.Utc(nowUtc); }
    public void Release(DateTime nowUtc)
    { if (Status != FinanceAutonomyBudgetReservationStatus.Reserved) return; Status = FinanceAutonomyBudgetReservationStatus.Released; ReconciledUtc = BudgetValue.Utc(nowUtc); }
}

public sealed class FinanceAutonomyCircuitBreaker : ICompanyOwnedEntity
{
    private FinanceAutonomyCircuitBreaker() { }
    public FinanceAutonomyCircuitBreaker(Guid id, Guid companyId, Guid agentId, string capabilityId,
        DateTime windowStartUtc, DateTime windowEndUtc, DateTime nowUtc)
    {
        Id = BudgetValue.Id(id); CompanyId = BudgetValue.Required(companyId, nameof(companyId)); AgentId = BudgetValue.Required(agentId, nameof(agentId));
        CapabilityId = BudgetValue.Text(capabilityId, nameof(capabilityId), 160).ToLowerInvariant();
        ScopeKey = $"agent:{AgentId:N}:capability:{CapabilityId}"; WindowStartUtc = BudgetValue.Utc(windowStartUtc);
        WindowEndUtc = BudgetValue.Utc(windowEndUtc); Status = FinanceAutonomyCircuitStatus.Closed;
        UpdatedUtc = BudgetValue.Utc(nowUtc); Version = 1; RowVersion = BudgetValue.Token();
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string CapabilityId { get; private set; } = null!;
    public string ScopeKey { get; private set; } = null!;
    public FinanceAutonomyCircuitStatus Status { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public int PolicyDenials { get; private set; }
    public int InvalidPlans { get; private set; }
    public int ProviderAmbiguities { get; private set; }
    public int Errors { get; private set; }
    public int StaleEvidence { get; private set; }
    public int AuditOutboxFailures { get; private set; }
    public string? OpenReasonCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public DateTime? OpenedUtc { get; private set; }
    public DateTime? CooldownUntilUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Company Company { get; private set; } = null!;

    public void Record(string signalType, DateTime nowUtc)
    {
        switch (signalType)
        {
            case "policy_denial": PolicyDenials++; break; case "invalid_plan": InvalidPlans++; break;
            case "provider_ambiguity": ProviderAmbiguities++; break; case "error": Errors++; break;
            case "stale_evidence": StaleEvidence++; break; case "audit_outbox_failure": AuditOutboxFailures++; break;
            default: throw new ArgumentOutOfRangeException(nameof(signalType));
        }
        Touch(nowUtc);
    }
    public void Open(string reasonCode, string summary, DateTime cooldownUntilUtc, DateTime nowUtc)
    { Status = FinanceAutonomyCircuitStatus.Open; OpenReasonCode = BudgetValue.Text(reasonCode, nameof(reasonCode), 100); SafeSummary = BudgetValue.Text(summary, nameof(summary), 1000); OpenedUtc = BudgetValue.Utc(nowUtc); CooldownUntilUtc = BudgetValue.Utc(cooldownUntilUtc); Touch(nowUtc); }
    public void Reset(DateTime windowEndUtc, long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion > 0 && expectedVersion != Version) throw new InvalidOperationException("The Finance autonomy circuit changed. Refresh and retry.");
        Status = FinanceAutonomyCircuitStatus.Closed; WindowStartUtc = BudgetValue.Utc(nowUtc); WindowEndUtc = BudgetValue.Utc(windowEndUtc);
        PolicyDenials = InvalidPlans = ProviderAmbiguities = Errors = StaleEvidence = AuditOutboxFailures = 0;
        OpenReasonCode = SafeSummary = null; OpenedUtc = CooldownUntilUtc = null; Touch(nowUtc);
    }
    private void Touch(DateTime nowUtc) { UpdatedUtc = BudgetValue.Utc(nowUtc); Version++; RowVersion = BudgetValue.Token(); }
}

public sealed class FinanceAutonomyBudgetAlert : ICompanyOwnedEntity
{
    private FinanceAutonomyBudgetAlert() { }
    public FinanceAutonomyBudgetAlert(Guid id, Guid companyId, Guid circuitId, string reasonCode,
        string safeSummary, string correlationId, DateTime nowUtc)
    { Id = BudgetValue.Id(id); CompanyId = BudgetValue.Required(companyId, nameof(companyId)); CircuitId = BudgetValue.Required(circuitId, nameof(circuitId)); ReasonCode = BudgetValue.Text(reasonCode, nameof(reasonCode), 100); SafeSummary = BudgetValue.Text(safeSummary, nameof(safeSummary), 1000); CorrelationId = BudgetValue.Text(correlationId, nameof(correlationId), 128); Status = FinanceAutonomyBudgetAlertStatus.Open; CreatedUtc = BudgetValue.Utc(nowUtc); }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CircuitId { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string SafeSummary { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public FinanceAutonomyBudgetAlertStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public FinanceAutonomyCircuitBreaker Circuit { get; private set; } = null!;
    public void Resolve(DateTime nowUtc) { if (Status == FinanceAutonomyBudgetAlertStatus.Resolved) return; Status = FinanceAutonomyBudgetAlertStatus.Resolved; ResolvedUtc = BudgetValue.Utc(nowUtc); }
}

internal static class BudgetValue
{
    public static Guid Id(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Text(string? value, string name, int maximum) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= maximum ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string? Optional(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    public static byte[] Token() => Guid.NewGuid().ToByteArray();
}
