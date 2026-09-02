# Finance autonomy budgets and circuit breakers

Finance autonomy work reserves capacity before a durable step lease is committed. The reservation and lease share one database unit of work, so concurrent workers cannot both spend the same reviewed capacity. Completion, failure, reconciliation, cancellation, and supersession either reconcile actual usage or release capacity without deleting the historical record.

## Budget scopes and accounting

Policies may apply company-wide, to an agent, to a capability, or to an agent/capability pair. Every applicable scope must allow a claim. Company-wide policies use the operating configuration timezone and daily window, and their effective ceilings are always the lower of the policy and the authoritative company cycle/day limits.

The accounted dimensions are records evaluated, drafts or tasks created, execute attempts, amount exposure, generated object bytes, exports, model calls, tool calls, estimated cost, retries, and elapsed runtime. Limits can be set per run and per rolling/calendar window. Reservations count immediately; reconciliation replaces planned values with actual values. Failed and partially completed attempts remain consumed, retries add retry usage, and approval waits retain their reservation. A retry or a new correlation identifier does not reset the run or window totals.

An exact limit is allowed. The next unit is denied with a stable reason code. Large work split across several runs is still bounded by the shared scoped window.

## Circuit breakers

The service records bounded signals for repeated policy denials, invalid plans, provider ambiguity, error bursts, stale evidence, and audit/outbox failures. A reviewed threshold opens the agent/capability circuit, pauses that capability through the Finance autonomy control plane, creates a safe operator alert, and blocks subsequent reservations. Cooldown does not silently resume work: an owner, admin, or manager must review and reset the circuit. The next claim still rechecks current policy, evidence, company pause, and emergency-stop state.

## Operations and privacy

Authorized Finance viewers can query policies, active and historical windows, reserved/consumed/remaining capacity, recent reservations, circuits, and alerts. Only company managers can update policy or reset a circuit. Data is always company-scoped.

Metrics expose only bounded reason or signal codes. Alerts and audit entries contain safe summaries, identifiers, policy versions, and correlation references; raw evidence, provider payloads, prompts, credentials, and financial record contents are not included.

Stable denial codes include `finance_autonomy_budget_per_run_exceeded`, `finance_autonomy_budget_window_exceeded`, `finance_autonomy_company_budget_missing`, `finance_autonomy_budget_emergency_stopped`, and `finance_autonomy_circuit_open`.
