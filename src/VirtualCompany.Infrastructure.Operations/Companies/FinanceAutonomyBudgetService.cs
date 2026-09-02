using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyBudgetService : IFinanceAutonomyBudgetService
{
    private const int MaximumTake = 200;
    private static readonly Meter Meter = new("VirtualCompany.FinanceAutonomy.Budgets");
    private static readonly Counter<long> ReservationDecisions = Meter.CreateCounter<long>("finance_autonomy_budget_reservation_decisions");
    private static readonly Counter<long> CircuitSignals = Meter.CreateCounter<long>("finance_autonomy_circuit_signals");
    private static readonly Counter<long> CircuitsOpened = Meter.CreateCounter<long>("finance_autonomy_circuits_opened");
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IFinanceAgentCoverageCatalogue _coverage;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;

    public FinanceAutonomyBudgetService(VirtualCompanyDbContext db,
        ICompanyMembershipContextResolver memberships, IFinanceAgentCoverageCatalogue coverage,
        IAuditEventWriter audit, TimeProvider clock)
    { _db = db; _memberships = memberships; _coverage = coverage; _audit = audit; _clock = clock; }

    public async Task<FinanceAutonomyBudgetQueryResult> GetAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(companyId, false, cancellationToken);
        var bounded = Math.Clamp(take, 1, MaximumTake);
        var policies = await _db.FinanceAutonomyBudgetPolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.ScopeKey).ToListAsync(cancellationToken);
        var policyIds = policies.Select(x => x.Id).ToArray();
        var windows = await _db.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && policyIds.Contains(x.PolicyId))
            .OrderByDescending(x => x.WindowStartUtc).Take(bounded).ToListAsync(cancellationToken);
        var reservations = await _db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc).Take(bounded).ToListAsync(cancellationToken);
        var circuits = await _db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc).Take(bounded).ToListAsync(cancellationToken);
        var alerts = await _db.FinanceAutonomyBudgetAlerts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc).Take(bounded).ToListAsync(cancellationToken);
        var byPolicy = policies.ToDictionary(x => x.Id);
        return new(policies.Select(Map).ToArray(), windows.Where(x => byPolicy.ContainsKey(x.PolicyId))
                .Select(x => Map(x, byPolicy[x.PolicyId])).ToArray(), reservations.Select(Map).ToArray(),
            circuits.Select(Map).ToArray(), alerts.Select(Map).ToArray(), UtcNow());
    }

    public async Task<FinanceAutonomyBudgetPolicyDto> UpsertPolicyAsync(Guid companyId,
        UpsertFinanceAutonomyBudgetPolicyCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, true, cancellationToken);
        ValidatePolicyCommand(command);
        var operating = await _db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw Validation(nameof(companyId), "The authoritative company operating budget is unavailable.");
        if (!command.AgentId.HasValue && string.IsNullOrWhiteSpace(command.CapabilityId) &&
            (command.WindowMinutes != 1440 || !string.Equals(command.Timezone, operating.Timezone, StringComparison.OrdinalIgnoreCase)))
            throw Validation(nameof(command.WindowMinutes),
                "The company-wide budget uses the authoritative operating timezone and daily window; use a narrower scope for other windows.");
        if (command.AgentId.HasValue && !await _db.Agents.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.AgentId, cancellationToken))
            throw Validation(nameof(command.AgentId), "The budget policy agent does not belong to this company.");
        if (!string.IsNullOrWhiteSpace(command.CapabilityId) && !_coverage.ListManifests().Any(x =>
                string.Equals(x.Id, command.CapabilityId.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw Validation(nameof(command.CapabilityId), "The budget policy capability is not in the Finance coverage catalogue.");

        var now = UtcNow(); var perRun = DomainLimits(command.PerRunLimits); var window = DomainLimits(command.WindowLimits);
        FinanceAutonomyBudgetPolicy policy;
        if (command.PolicyId.HasValue)
        {
            policy = await _db.FinanceAutonomyBudgetPolicies.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.Id == command.PolicyId, cancellationToken)
                ?? throw new KeyNotFoundException("Finance autonomy budget policy was not found.");
            if (policy.AgentId != command.AgentId || !string.Equals(policy.CapabilityId, Normalize(command.CapabilityId), StringComparison.Ordinal))
                throw Validation(nameof(command.PolicyId), "A budget policy scope is immutable; create another scoped policy.");
            policy.Update(command.Timezone, command.WindowMinutes, perRun, window,
                command.PolicyDenialThreshold, command.InvalidPlanThreshold, command.ProviderAmbiguityThreshold,
                command.ErrorBurstThreshold, command.StaleEvidenceThreshold, command.AuditOutboxFailureThreshold,
                command.CircuitWindowMinutes, command.CircuitCooldownMinutes, command.IsActive,
                command.ExpectedVersion, now);
        }
        else
        {
            var scopeKey = FinanceAutonomyBudgetPolicy.CreateScopeKey(command.AgentId, command.CapabilityId);
            if (await _db.FinanceAutonomyBudgetPolicies.IgnoreQueryFilters().AnyAsync(x =>
                    x.CompanyId == companyId && x.ScopeKey == scopeKey, cancellationToken))
                throw Validation(nameof(command.PolicyId), "A Finance autonomy budget policy already exists for this scope.");
            policy = new FinanceAutonomyBudgetPolicy(Guid.NewGuid(), companyId, command.AgentId,
                command.CapabilityId, command.Timezone, command.WindowMinutes, perRun, window,
                command.PolicyDenialThreshold, command.InvalidPlanThreshold, command.ProviderAmbiguityThreshold,
                command.ErrorBurstThreshold, command.StaleEvidenceThreshold, command.AuditOutboxFailureThreshold,
                command.CircuitWindowMinutes, command.CircuitCooldownMinutes, now);
            if (!command.IsActive) policy.Update(command.Timezone, command.WindowMinutes, perRun, window,
                command.PolicyDenialThreshold, command.InvalidPlanThreshold, command.ProviderAmbiguityThreshold,
                command.ErrorBurstThreshold, command.StaleEvidenceThreshold, command.AuditOutboxFailureThreshold,
                command.CircuitWindowMinutes, command.CircuitCooldownMinutes, false, policy.Version, now);
            _db.FinanceAutonomyBudgetPolicies.Add(policy);
        }
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            AuditEventActions.FinanceAutonomyBudgetPolicyUpdated, AuditTargetTypes.FinanceAutonomyBudget,
            policy.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "A scoped Finance autonomy budget policy was reviewed and updated.",
            Metadata: new Dictionary<string, string?> { ["scopeKey"] = policy.ScopeKey, ["active"] = policy.IsActive.ToString() },
            CorrelationId: $"finance-autonomy-budget-policy:{policy.Id:N}:{policy.Version}"), cancellationToken);
        await SaveAsync(cancellationToken); return Map(policy);
    }

    public async Task<FinanceAutonomyCircuitBreakerDto> ResetCircuitAsync(Guid companyId, Guid circuitId,
        long expectedVersion, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, true, cancellationToken);
        var circuit = await _db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == circuitId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy circuit was not found.");
        var policies = await ApplicablePoliciesAsync(companyId, circuit.AgentId, circuit.CapabilityId, cancellationToken);
        if (policies.Count == 0) throw Validation(nameof(circuitId), "No active budget policy remains for this circuit.");
        var now = UtcNow(); circuit.Reset(now.AddMinutes(policies.Min(x => x.CircuitWindowMinutes)), expectedVersion, now);
        var alerts = await _db.FinanceAutonomyBudgetAlerts.IgnoreQueryFilters().Where(x =>
            x.CompanyId == companyId && x.CircuitId == circuitId && x.Status == FinanceAutonomyBudgetAlertStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var alert in alerts) alert.Resolve(now);
        var control = await _db.FinanceAutonomyControls.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.ScopeKey == FinanceAutonomyControl.CreateScopeKey(
                FinanceAutonomyControlScope.Capability, null, circuit.CapabilityId), cancellationToken);
        if (control?.Reason?.StartsWith("Circuit breaker:", StringComparison.Ordinal) == true)
            control.Change(FinanceAutonomyControlState.Active, "Circuit breaker reset after operator review.", member.UserId, now, control.Version);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            AuditEventActions.FinanceAutonomyCircuitReset, AuditTargetTypes.FinanceAutonomyCircuit,
            circuit.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "An authorized operator reset the Finance autonomy circuit after review.",
            CorrelationId: $"finance-autonomy-circuit-reset:{circuit.Id:N}:{circuit.Version}"), cancellationToken);
        await SaveAsync(cancellationToken); return Map(circuit);
    }

    public async Task<FinanceAutonomyBudgetReservationDecision> ReserveForClaimAsync(Guid companyId,
        Guid runId, Guid stepId, int attemptNumber, FinanceAutonomyUsageDefinition planned,
        CancellationToken cancellationToken)
    {
        ValidateUsage(planned); var now = UtcNow();
        var run = await _db.FinanceAutonomyRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == runId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy run was not found.");
        if (!await _db.FinanceAutonomyRunSteps.IgnoreQueryFilters().AnyAsync(x =>
                x.CompanyId == companyId && x.RunId == runId && x.Id == stepId, cancellationToken))
            throw new KeyNotFoundException("Finance autonomy run step was not found.");
        var operating = await _db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (operating is null)
            return Deny(FinanceAutonomyBudgetReasonCodes.CompanyBudgetMissing,
                "The authoritative company operating budget is unavailable.");
        if (operating.EmergencyStopped || operating.IsPaused)
            return Deny(FinanceAutonomyBudgetReasonCodes.EmergencyStopped,
                "Company operations are paused or emergency-stopped.");
        if (await _db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.AgentId == run.AgentId && x.CapabilityId == run.CapabilityId &&
                x.Status == FinanceAutonomyCircuitStatus.Open, cancellationToken))
            return Deny(FinanceAutonomyBudgetReasonCodes.CircuitOpen,
                "The Finance autonomy capability circuit is open and requires operator review.");

        var policies = await ApplicablePoliciesAsync(companyId, run.AgentId, run.CapabilityId, cancellationToken);
        if (policies.Count == 0)
        {
            var defaultPolicy = CreateDefaultCompanyPolicy(companyId, operating, now);
            _db.FinanceAutonomyBudgetPolicies.Add(defaultPolicy); policies = [defaultPolicy];
        }
        var usage = DomainUsage(planned with { Retries = Math.Max(planned.Retries, attemptNumber > 1 ? 1 : 0) });
        var allRunReservations = await _db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.RunId == runId).ToListAsync(cancellationToken);
        var runUsage = FinanceAutonomyUsageValues.Zero();
        foreach (var attempt in allRunReservations.Where(x => x.Status != FinanceAutonomyBudgetReservationStatus.Released)
                     .GroupBy(x => x.ReservationKey, StringComparer.Ordinal).Select(x => x.First()))
            runUsage.Add(attempt.Status == FinanceAutonomyBudgetReservationStatus.Reconciled ? attempt.Actual : attempt.Planned);
        var grantExceeded = CheckRunSnapshot(run.BudgetSnapshotJson, runUsage, usage);
        if (grantExceeded is not null) return Deny(FinanceAutonomyBudgetReasonCodes.PerRunExceeded,
            $"The planned {grantExceeded} exceeds the immutable run/grant budget snapshot.");

        var key = $"{runId:N}:{stepId:N}:{attemptNumber}";
        var existingReservations = await _db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ReservationKey == key)
            .ToDictionaryAsync(x => x.PolicyId, cancellationToken);
        var staged = new List<(FinanceAutonomyBudgetPolicy Policy, FinanceAutonomyBudgetWindow Window)>();
        foreach (var policy in policies)
        {
            if (existingReservations.ContainsKey(policy.Id)) continue;
            var perRunLimits = EffectivePerRunLimits(policy, operating);
            var scopedRunUsage = FinanceAutonomyUsageValues.Zero();
            foreach (var prior in allRunReservations.Where(x => x.PolicyId == policy.Id &&
                         x.Status != FinanceAutonomyBudgetReservationStatus.Released))
                scopedRunUsage.Add(prior.Status == FinanceAutonomyBudgetReservationStatus.Reconciled
                    ? prior.Actual : prior.Planned);
            var perRunExceeded = perRunLimits.FirstExceeded(scopedRunUsage, usage);
            if (perRunExceeded is not null) return Deny(FinanceAutonomyBudgetReasonCodes.PerRunExceeded,
                $"The planned {perRunExceeded} exceeds the reviewed per-run budget for {policy.ScopeKey}.");
            var bounds = WindowBounds(now, policy.Timezone, policy.WindowMinutes);
            var window = await _db.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.PolicyId == policy.Id && x.WindowStartUtc == bounds.StartUtc &&
                x.WindowEndUtc == bounds.EndUtc, cancellationToken);
            if (window is null)
            {
                window = new FinanceAutonomyBudgetWindow(Guid.NewGuid(), companyId, policy.Id,
                    bounds.StartUtc, bounds.EndUtc, now); _db.FinanceAutonomyBudgetWindows.Add(window);
            }
            var exceeded = window.Check(EffectiveWindowLimits(policy, operating), usage);
            if (exceeded is not null) return Deny(FinanceAutonomyBudgetReasonCodes.WindowExceeded,
                $"The planned {exceeded} exceeds the remaining reviewed budget for {policy.ScopeKey}.");
            staged.Add((policy, window));
        }

        var ids = existingReservations.Values.Select(x => x.Id).ToList();
        foreach (var pair in staged)
        {
            pair.Window.Reserve(usage, now);
            var reservation = new FinanceAutonomyBudgetReservation(Guid.NewGuid(), companyId, pair.Policy.Id,
                pair.Window.Id, runId, stepId, attemptNumber, key, usage, run.CorrelationId, now);
            _db.FinanceAutonomyBudgetReservations.Add(reservation); ids.Add(reservation.Id);
        }
        ReservationDecisions.Add(1, new KeyValuePair<string, object?>("reason_code", FinanceAutonomyBudgetReasonCodes.Reserved));
        return new(true, FinanceAutonomyBudgetReasonCodes.Reserved,
            "Capacity was reserved atomically with the Finance autonomy step claim.", ids);
    }

    public async Task ReconcileForAttemptAsync(Guid companyId, Guid runId, Guid stepId, int attemptNumber,
        FinanceAutonomyUsageDefinition actual, bool releaseOnly, CancellationToken cancellationToken)
    {
        ValidateUsage(actual); var now = UtcNow();
        var usage = DomainUsage(actual with { Retries = Math.Max(actual.Retries, attemptNumber > 1 ? 1 : 0) });
        var reservations = await _db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters()
            .Include(x => x.Window).Where(x => x.CompanyId == companyId && x.RunId == runId &&
                x.StepId == stepId && x.AttemptNumber == attemptNumber &&
                x.Status == FinanceAutonomyBudgetReservationStatus.Reserved).ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            if (releaseOnly) { reservation.Window.Release(reservation.Planned, now); reservation.Release(now); }
            else { reservation.Window.Reconcile(reservation.Planned, usage, now); reservation.Reconcile(usage, now); }
        }
    }

    public async Task ReleaseForRunAsync(Guid companyId, Guid runId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var reservations = await _db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters()
            .Include(x => x.Window).Where(x => x.CompanyId == companyId && x.RunId == runId &&
                x.Status == FinanceAutonomyBudgetReservationStatus.Reserved).ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        { reservation.Window.Release(reservation.Planned, now); reservation.Release(now); }
    }

    public async Task RecordCircuitSignalAsync(Guid companyId, RecordFinanceAutonomyCircuitSignalCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedSignal = Normalize(command.SignalType);
        if (command.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(command.CapabilityId) ||
            normalizedSignal is null || !FinanceAutonomyCircuitSignals.All.Contains(normalizedSignal))
            throw Validation(nameof(command), "A valid agent, capability, and circuit signal are required.");
        var policies = await ApplicablePoliciesAsync(companyId, command.AgentId, command.CapabilityId, cancellationToken);
        if (policies.Count == 0) return;
        var now = UtcNow(); var circuitWindow = policies.Min(x => x.CircuitWindowMinutes);
        var circuit = await _db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.AgentId == command.AgentId && x.CapabilityId == Normalize(command.CapabilityId), cancellationToken);
        if (circuit is null)
        {
            circuit = new FinanceAutonomyCircuitBreaker(Guid.NewGuid(), companyId, command.AgentId,
                command.CapabilityId, now, now.AddMinutes(circuitWindow), now);
            _db.FinanceAutonomyCircuitBreakers.Add(circuit);
        }
        else if (circuit.Status == FinanceAutonomyCircuitStatus.Closed && circuit.WindowEndUtc <= now)
            circuit.Reset(now.AddMinutes(circuitWindow), circuit.Version, now);
        if (circuit.Status == FinanceAutonomyCircuitStatus.Open) return;
        var signal = normalizedSignal; circuit.Record(signal, now);
        CircuitSignals.Add(1, new KeyValuePair<string, object?>("signal_type", signal));
        var threshold = signal switch
        {
            FinanceAutonomyCircuitSignals.PolicyDenial => policies.Min(x => x.PolicyDenialThreshold),
            FinanceAutonomyCircuitSignals.InvalidPlan => policies.Min(x => x.InvalidPlanThreshold),
            FinanceAutonomyCircuitSignals.ProviderAmbiguity => policies.Min(x => x.ProviderAmbiguityThreshold),
            FinanceAutonomyCircuitSignals.Error => policies.Min(x => x.ErrorBurstThreshold),
            FinanceAutonomyCircuitSignals.StaleEvidence => policies.Min(x => x.StaleEvidenceThreshold),
            _ => policies.Min(x => x.AuditOutboxFailureThreshold)
        };
        var count = signal switch
        {
            FinanceAutonomyCircuitSignals.PolicyDenial => circuit.PolicyDenials,
            FinanceAutonomyCircuitSignals.InvalidPlan => circuit.InvalidPlans,
            FinanceAutonomyCircuitSignals.ProviderAmbiguity => circuit.ProviderAmbiguities,
            FinanceAutonomyCircuitSignals.Error => circuit.Errors,
            FinanceAutonomyCircuitSignals.StaleEvidence => circuit.StaleEvidence,
            _ => circuit.AuditOutboxFailures
        };
        if (count >= threshold)
        {
            var reason = $"finance_autonomy_circuit_{signal}";
            var summary = command.SafeSummary is { Length: > 0 }
                ? command.SafeSummary[..Math.Min(command.SafeSummary.Length, 1000)]
                : "Repeated bounded Finance autonomy failures require operator review.";
            circuit.Open(reason, summary, now.AddMinutes(policies.Max(x => x.CircuitCooldownMinutes)), now);
            var controlKey = FinanceAutonomyControl.CreateScopeKey(FinanceAutonomyControlScope.Capability, null, command.CapabilityId);
            var control = await _db.FinanceAutonomyControls.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.ScopeKey == controlKey, cancellationToken);
            if (control is null)
            {
                control = new FinanceAutonomyControl(companyId, FinanceAutonomyControlScope.Capability, null,
                    command.CapabilityId, now); _db.FinanceAutonomyControls.Add(control);
            }
            control.ChangeBySystem(FinanceAutonomyControlState.Paused, $"Circuit breaker: {reason}", now);
            var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
                ? $"finance-autonomy-circuit:{circuit.Id:N}:{circuit.Version}"
                : command.CorrelationId;
            _db.FinanceAutonomyBudgetAlerts.Add(new FinanceAutonomyBudgetAlert(Guid.NewGuid(), companyId,
                circuit.Id, reason, summary, correlationId, now));
            CircuitsOpened.Add(1, new KeyValuePair<string, object?>("reason_code", reason));
        }
        await SaveAsync(cancellationToken);
        if (circuit.Status == FinanceAutonomyCircuitStatus.Open)
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null,
                AuditEventActions.FinanceAutonomyCircuitOpened, AuditTargetTypes.FinanceAutonomyCircuit,
                circuit.Id.ToString("N"), AuditEventOutcomes.Blocked,
                circuit.SafeSummary ?? "Finance autonomy circuit opened.",
                Metadata: new Dictionary<string, string?> { ["capabilityId"] = circuit.CapabilityId, ["reasonCode"] = circuit.OpenReasonCode },
                CorrelationId: command.CorrelationId), cancellationToken);
    }

    private async Task<IReadOnlyList<FinanceAutonomyBudgetPolicy>> ApplicablePoliciesAsync(Guid companyId,
        Guid agentId, string capabilityId, CancellationToken ct) =>
        await _db.FinanceAutonomyBudgetPolicies.IgnoreQueryFilters().Where(x => x.CompanyId == companyId &&
            x.IsActive && (!x.AgentId.HasValue || x.AgentId == agentId) &&
            (x.CapabilityId == null || x.CapabilityId == Normalize(capabilityId))).OrderBy(x => x.ScopeKey).ToListAsync(ct);

    private static FinanceAutonomyBudgetPolicy CreateDefaultCompanyPolicy(Guid companyId,
        CompanyOperatingConfiguration operating, DateTime now) => new(Guid.NewGuid(), companyId, null, null,
        operating.Timezone, 1440,
        new(1000, operating.MaximumTasksPerCycle, 100, null, 50_000_000, 10,
            operating.MaximumModelCallsPerCycle, operating.MaximumToolCallsPerCycle,
            operating.MaximumMonetaryBudgetPerCycle, 20, operating.MaximumRuntimeSeconds),
        new(5000, operating.MaximumTasksPerDay, 500, null, 250_000_000, 50,
            operating.MaximumModelCallsPerDay, operating.MaximumToolCallsPerDay,
            operating.MaximumMonetaryBudgetPerDay, 100, 86400),
        3, 3, 2, 5, 3, 2, 60, 60, now);

    private static FinanceAutonomyUsageLimits EffectivePerRunLimits(
        FinanceAutonomyBudgetPolicy policy, CompanyOperatingConfiguration operating)
    {
        if (policy.AgentId.HasValue || policy.CapabilityId is not null) return policy.PerRunLimits;
        return WithOperatingCeilings(policy.PerRunLimits, operating.MaximumTasksPerCycle,
            operating.MaximumModelCallsPerCycle, operating.MaximumToolCallsPerCycle,
            operating.MaximumMonetaryBudgetPerCycle, operating.MaximumRuntimeSeconds);
    }

    private static FinanceAutonomyUsageLimits EffectiveWindowLimits(
        FinanceAutonomyBudgetPolicy policy, CompanyOperatingConfiguration operating)
    {
        if (policy.AgentId.HasValue || policy.CapabilityId is not null) return policy.WindowLimits;
        return WithOperatingCeilings(policy.WindowLimits, operating.MaximumTasksPerDay,
            operating.MaximumModelCallsPerDay, operating.MaximumToolCallsPerDay,
            operating.MaximumMonetaryBudgetPerDay, null);
    }

    private static FinanceAutonomyUsageLimits WithOperatingCeilings(FinanceAutonomyUsageLimits limits,
        int tasks, int modelCalls, int toolCalls, decimal? cost, int? runtimeSeconds) => new(
        limits.RecordsEvaluated, Min(limits.DraftsOrTasksCreated, tasks), limits.ExecuteAttempts,
        limits.AmountExposure, limits.ObjectBytes, limits.ExportsCreated, Min(limits.ModelCalls, modelCalls),
        Min(limits.ToolCalls, toolCalls), Min(limits.EstimatedCost, cost), limits.Retries,
        Min(limits.RuntimeSeconds, runtimeSeconds));

    private static int? Min(int? left, int? right) => left.HasValue && right.HasValue
        ? Math.Min(left.Value, right.Value) : left ?? right;
    private static decimal? Min(decimal? left, decimal? right) => left.HasValue && right.HasValue
        ? Math.Min(left.Value, right.Value) : left ?? right;

    private static string? CheckRunSnapshot(string json, FinanceAutonomyUsageValues current,
        FinanceAutonomyUsageValues addition)
    {
        try
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? [];
            if (values.TryGetValue("maximumRecords", out var records) && current.RecordsEvaluated + addition.RecordsEvaluated > records) return "records_evaluated";
            if (values.TryGetValue("maximumActions", out var actions) && current.ExecuteAttempts + addition.ExecuteAttempts > actions) return "execute_attempts";
            if (values.TryGetValue("maximumAmount", out var amount) && amount > 0 && current.AmountExposure + addition.AmountExposure > amount) return "amount_exposure";
            return null;
        }
        catch (System.Text.Json.JsonException) { return "budget_snapshot"; }
    }

    private static (DateTime StartUtc, DateTime EndUtc) WindowBounds(DateTime nowUtc, string timezone, int minutes)
    {
        var zone = CronosScheduleExpressionValidator.ResolveTimeZone(timezone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        var startLocal = new DateTime(local.Ticks / ticks * ticks, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddMinutes(minutes);
        return (LocalToUtc(startLocal, zone), LocalToUtc(endLocal, zone));
    }

    private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset).UtcDateTime;
        }
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, bool manager, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var member = await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (manager && member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");
        return member;
    }

    private static void ValidatePolicyCommand(UpsertFinanceAutonomyBudgetPolicyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command); _ = CronosScheduleExpressionValidator.ResolveTimeZone(command.Timezone);
        if (command.WindowMinutes is < 5 or > 44640) throw Validation(nameof(command.WindowMinutes), "Budget window minutes must be between 5 and 44640.");
        if (command.PolicyDenialThreshold is < 1 or > 10000 || command.InvalidPlanThreshold is < 1 or > 10000 ||
            command.ProviderAmbiguityThreshold is < 1 or > 10000 || command.ErrorBurstThreshold is < 1 or > 10000 ||
            command.StaleEvidenceThreshold is < 1 or > 10000 || command.AuditOutboxFailureThreshold is < 1 or > 10000 ||
            command.CircuitWindowMinutes is < 1 or > 10000 || command.CircuitCooldownMinutes is < 1 or > 10000)
            throw Validation(nameof(command.PolicyDenialThreshold), "Circuit thresholds and windows must be between 1 and 10000.");
        _ = DomainLimits(command.PerRunLimits); _ = DomainLimits(command.WindowLimits);
    }
    private static void ValidateUsage(FinanceAutonomyUsageDefinition x) { _ = DomainUsage(x); }
    private static FinanceAutonomyUsageValues DomainUsage(FinanceAutonomyUsageDefinition x) => new(
        x.RecordsEvaluated, x.DraftsOrTasksCreated, x.ExecuteAttempts, x.AmountExposure, x.ObjectBytes,
        x.ExportsCreated, x.ModelCalls, x.ToolCalls, x.EstimatedCost, x.Retries, x.RuntimeSeconds);
    private static FinanceAutonomyUsageLimits DomainLimits(FinanceAutonomyUsageLimitDefinition x) => new(
        x.RecordsEvaluated, x.DraftsOrTasksCreated, x.ExecuteAttempts, x.AmountExposure, x.ObjectBytes,
        x.ExportsCreated, x.ModelCalls, x.ToolCalls, x.EstimatedCost, x.Retries, x.RuntimeSeconds);
    private static FinanceAutonomyUsageDefinition Contract(FinanceAutonomyUsageValues x) => new(
        x.RecordsEvaluated, x.DraftsOrTasksCreated, x.ExecuteAttempts, x.AmountExposure, x.ObjectBytes,
        x.ExportsCreated, x.ModelCalls, x.ToolCalls, x.EstimatedCost, x.Retries, x.RuntimeSeconds);
    private static FinanceAutonomyUsageLimitDefinition Contract(FinanceAutonomyUsageLimits x) => new(
        x.RecordsEvaluated, x.DraftsOrTasksCreated, x.ExecuteAttempts, x.AmountExposure, x.ObjectBytes,
        x.ExportsCreated, x.ModelCalls, x.ToolCalls, x.EstimatedCost, x.Retries, x.RuntimeSeconds);
    private static FinanceAutonomyUsageLimitDefinition Remaining(FinanceAutonomyUsageLimits l,
        FinanceAutonomyUsageValues reserved, FinanceAutonomyUsageValues consumed) => new(
        Rem(l.RecordsEvaluated, reserved.RecordsEvaluated, consumed.RecordsEvaluated),
        Rem(l.DraftsOrTasksCreated, reserved.DraftsOrTasksCreated, consumed.DraftsOrTasksCreated),
        Rem(l.ExecuteAttempts, reserved.ExecuteAttempts, consumed.ExecuteAttempts),
        Rem(l.AmountExposure, reserved.AmountExposure, consumed.AmountExposure),
        Rem(l.ObjectBytes, reserved.ObjectBytes, consumed.ObjectBytes), Rem(l.ExportsCreated, reserved.ExportsCreated, consumed.ExportsCreated),
        Rem(l.ModelCalls, reserved.ModelCalls, consumed.ModelCalls), Rem(l.ToolCalls, reserved.ToolCalls, consumed.ToolCalls),
        Rem(l.EstimatedCost, reserved.EstimatedCost, consumed.EstimatedCost), Rem(l.Retries, reserved.Retries, consumed.Retries),
        Rem(l.RuntimeSeconds, reserved.RuntimeSeconds, consumed.RuntimeSeconds));
    private static int? Rem(int? limit, int reserved, int consumed) => limit.HasValue ? Math.Max(0, limit.Value - reserved - consumed) : null;
    private static long? Rem(long? limit, long reserved, long consumed) => limit.HasValue ? Math.Max(0, limit.Value - reserved - consumed) : null;
    private static decimal? Rem(decimal? limit, decimal reserved, decimal consumed) => limit.HasValue ? Math.Max(0, limit.Value - reserved - consumed) : null;

    private static FinanceAutonomyBudgetPolicyDto Map(FinanceAutonomyBudgetPolicy x) => new(x.Id, x.AgentId, x.CapabilityId,
        x.ScopeKey, x.Timezone, x.WindowMinutes, Contract(x.PerRunLimits), Contract(x.WindowLimits),
        x.PolicyDenialThreshold, x.InvalidPlanThreshold, x.ProviderAmbiguityThreshold, x.ErrorBurstThreshold,
        x.StaleEvidenceThreshold, x.AuditOutboxFailureThreshold, x.CircuitWindowMinutes, x.CircuitCooldownMinutes,
        x.IsActive, x.Version, x.UpdatedUtc);
    private static FinanceAutonomyBudgetWindowDto Map(FinanceAutonomyBudgetWindow x, FinanceAutonomyBudgetPolicy p) => new(
        x.Id, x.PolicyId, p.ScopeKey, x.WindowStartUtc, x.WindowEndUtc, Contract(x.Reserved), Contract(x.Consumed),
        Contract(p.WindowLimits), Remaining(p.WindowLimits, x.Reserved, x.Consumed), x.Version);
    private static FinanceAutonomyBudgetReservationDto Map(FinanceAutonomyBudgetReservation x) => new(x.Id, x.PolicyId,
        x.WindowId, x.RunId, x.StepId, x.AttemptNumber, x.Status.ToStorageValue(), Contract(x.Planned), Contract(x.Actual), x.CreatedUtc, x.ReconciledUtc);
    private static FinanceAutonomyCircuitBreakerDto Map(FinanceAutonomyCircuitBreaker x) => new(x.Id, x.AgentId,
        x.CapabilityId, x.Status.ToStorageValue(), x.WindowStartUtc, x.WindowEndUtc, x.PolicyDenials, x.InvalidPlans,
        x.ProviderAmbiguities, x.Errors, x.StaleEvidence, x.AuditOutboxFailures, x.OpenReasonCode, x.SafeSummary,
        x.OpenedUtc, x.CooldownUntilUtc, x.Version);
    private static FinanceAutonomyBudgetAlertDto Map(FinanceAutonomyBudgetAlert x) => new(x.Id, x.CircuitId,
        x.ReasonCode, x.SafeSummary, x.Status.ToStorageValue(), x.CreatedUtc, x.ResolvedUtc);
    private static FinanceAutonomyBudgetReservationDecision Deny(string code, string summary)
    {
        ReservationDecisions.Add(1, new KeyValuePair<string, object?>("reason_code", code));
        return new(false, code, summary, []);
    }
    private static FinanceAutonomyBudgetValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw new FinanceAutonomyBudgetConcurrencyException($"Finance autonomy budget changed concurrently: {ex.Message}"); }
    }
    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
