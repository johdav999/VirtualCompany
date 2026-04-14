# Goal
Implement backlog task **TASK-1.3.1 — Implement policy evaluation engine for threshold- and rule-based escalations** for **US-1.3 ST-A203 — Escalation policies** in the existing .NET modular monolith.

Deliver a tenant-aware, auditable escalation policy evaluation capability that:

- evaluates escalation policies against alert and task events
- supports configurable thresholds, timers, and rule conditions
- creates escalation records when conditions are met
- guarantees one escalation per policy level per source entity unless the entity is resolved and later re-opened
- writes evaluation outcomes and escalation actions to the audit log with `correlationId` traceability

The implementation must fit the current architecture:
- ASP.NET Core modular monolith
- Domain/Application/Infrastructure separation
- PostgreSQL persistence
- background-worker-friendly design
- auditability as a first-class business concern

# Scope
In scope:

- Add/complete domain model(s) for escalation policy evaluation and escalation records
- Add persistence for escalation records and any supporting state needed for idempotent per-level execution
- Implement an application-layer policy evaluation engine/service for:
  - threshold-based conditions
  - timer-based conditions
  - rule-based conditions over alert/task event payloads
- Support evaluation inputs for at least:
  - task events
  - alert events
- Ensure escalation creation includes:
  - `policyId`
  - `sourceEntityId`
  - `escalationLevel`
  - `reason`
  - `triggeredAt`
- Enforce “execute only once per policy level for same source entity unless resolved and re-opened”
- Write audit events for:
  - evaluation started/completed
  - evaluation result
  - escalation created
  - escalation skipped due to prior execution/idempotency
- Ensure all audit records include or can be traced by `correlationId`
- Add tests covering acceptance criteria and edge cases

Out of scope unless required by existing code patterns:

- Full UI for managing escalation policies
- Notification delivery/fan-out after escalation creation
- New external integrations
- Broad workflow engine redesign
- Nonessential refactors unrelated to escalation policy evaluation

# Files to touch
Inspect the solution first and then update the exact files that align with existing conventions. Expected areas:

- `src/VirtualCompany.Domain/**`
  - add/update escalation-related entities, value objects, enums, specifications, or domain services
- `src/VirtualCompany.Application/**`
  - add policy evaluation service/handler(s)
  - add commands/events/contracts for evaluating task/alert events
  - add DTOs/models for evaluation input and result
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations/repositories
  - persistence implementation for escalation records and audit writes
  - migration support if migrations are stored/generated here
- `src/VirtualCompany.Api/**`
  - only if an endpoint or event ingestion hook already exists and must be wired
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if this project is the established test location
- possibly `tests/**` adjacent application/domain test projects if present in solution

Also inspect for existing equivalents before creating new types:

- audit event abstractions
- correlation ID propagation
- task/workflow/alert event contracts
- approval/policy engine patterns
- tenant context abstractions
- outbox/event dispatcher patterns

If schema migrations are part of the repo workflow, add the appropriate migration for escalation persistence.

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect Domain/Application/Infrastructure boundaries and naming conventions.
   - Find any existing:
     - policy/guardrail engine
     - audit event writer
     - correlation ID accessor/middleware
     - task entities and status transitions
     - alert models/events
     - workflow exception/escalation placeholders
   - Reuse existing abstractions instead of introducing parallel ones.

2. **Design the escalation domain model**
   - Introduce or complete an `Escalation` aggregate/entity with fields matching acceptance criteria:
     - tenant/company scope
     - `PolicyId`
     - `SourceEntityId`
     - `SourceEntityType` if needed
     - `EscalationLevel`
     - `Reason`
     - `TriggeredAt`
     - status/metadata fields if consistent with existing model
     - correlation identifier if business entities store it directly
   - Add supporting policy evaluation models, likely:
     - `EscalationPolicy`
     - `EscalationLevelDefinition`
     - `ThresholdCondition`
     - `TimerCondition`
     - `RuleCondition`
     - `PolicyEvaluationResult`
   - Keep policy configuration compatible with architecture guidance favoring flexible JSON-backed config where appropriate.
   - If policies already live in agent/task/workflow config JSON, do not invent a separate policy store unless necessary; instead create typed evaluation models over existing config.

3. **Model event inputs for evaluation**
   - Define a normalized evaluation input contract for alert/task events, e.g.:
     - tenant/company id
     - source entity id/type
     - event type
     - event timestamp
     - current entity status
     - severity/priority/age/attempt counts/SLA timing fields as available
     - arbitrary payload for rule evaluation
     - `correlationId`
   - Ensure the engine can evaluate both:
     - immediate event-driven thresholds/rules
     - time-based/timer conditions based on timestamps and current state

4. **Implement the policy evaluation engine in Application**
   - Create an application service/domain service responsible for:
     - loading applicable policies
     - evaluating each policy level in order
     - determining whether conditions are met
     - checking whether the same policy level already escalated for the same source entity
     - creating escalation records when newly triggered
     - returning structured evaluation results
   - Support at minimum:
     - **thresholds**: numeric or ordinal comparisons such as severity, retry count, age, overdue duration, failure count
     - **timers**: elapsed time since created/updated/blocked/opened
     - **rules**: boolean conditions over normalized event fields/payload
   - Prefer deterministic typed evaluation over dynamic scripting.
   - If rule config is JSON-based, implement a constrained operator set such as:
     - `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `in`, `contains`
     - logical `all` / `any`
   - Keep default behavior safe:
     - invalid/ambiguous policy config should fail closed for execution and produce audit evidence
     - do not create duplicate escalations

5. **Implement once-per-level idempotency**
   - Enforce the rule:
     - one escalation per `policyId + sourceEntityId + escalationLevel`
     - unless the source entity has been resolved and later re-opened
   - Implement this robustly in both application logic and persistence where possible.
   - Recommended approach:
     - persist escalation records with enough state to distinguish lifecycle cycles
     - derive or store a “resolution cycle” / “reopen sequence” / “entity lifecycle version”
   - If the domain already tracks reopen/resolution transitions, reuse that source of truth.
   - Add a unique constraint/index if feasible for active lifecycle uniqueness.
   - Ensure retries/concurrent evaluations do not create duplicates.

6. **Persist escalation records**
   - Add EF Core mappings/configurations for escalation entities.
   - Add PostgreSQL migration(s) for new table(s)/index(es), likely including:
     - tenant/company id
     - policy id
     - source entity id
     - source entity type
     - escalation level
     - reason
     - triggered at
     - correlation id
     - lifecycle/reopen discriminator if needed
     - created/updated timestamps if standard
   - Add indexes for:
     - tenant + source entity
     - tenant + policy + source entity + level
     - correlation id lookup if audit tracing requires it

7. **Write audit events**
   - Use the existing Audit & Explainability pattern if present.
   - Emit business audit events for:
     - policy evaluation requested
     - policy evaluation completed with result summary
     - escalation created
     - escalation skipped because already executed for current lifecycle
     - invalid policy configuration / evaluation failure
   - Include:
     - actor type = system unless another actor is appropriate
     - action
     - target type/id
     - outcome
     - rationale/reason summary
     - `correlationId`
     - policy identifiers and level in metadata if supported
   - Keep audit entries concise and operational; do not log chain-of-thought.

8. **Wire into event flow**
   - Connect the engine to the existing task/alert event handling path.
   - If there is already an internal event bus, command handler, workflow runner, or background worker hook, integrate there.
   - Avoid introducing HTTP-only orchestration for something that should work from background processing too.
   - Ensure tenant context and correlation ID flow through the invocation path.

9. **Handle resolved and re-opened lifecycle**
   - Identify how tasks/alerts represent:
     - open/new/in-progress/blocked
     - resolved/completed/closed
     - reopened
   - Implement lifecycle reset logic so a previously escalated policy level can trigger again only after a genuine resolve-and-reopen cycle.
   - Add tests for:
     - same open lifecycle => no duplicate escalation
     - resolved then reopened => escalation allowed again

10. **Testing**
   - Add unit tests for evaluation logic:
     - threshold met/not met
     - timer met/not met
     - rule all/any combinations
     - invalid config handling
     - once-per-level behavior
     - resolved/reopened reset
   - Add integration tests for persistence/audit behavior:
     - escalation record created with required fields
     - duplicate prevention under repeated evaluation
     - audit events written and traceable by `correlationId`
   - If API/event ingestion tests exist, add one end-to-end path from event to escalation creation.

11. **Keep implementation production-safe**
   - Make methods async and cancellation-aware where appropriate.
   - Respect tenant isolation in all queries.
   - Avoid direct SQL unless already standard.
   - Keep code modular and extensible for future approval/escalation workflows.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in normal workflow:
   - generate/apply the migration according to repo conventions
   - verify schema includes escalation table/indexes

4. Validate acceptance criteria explicitly:
   - **Policy evaluation**
     - confirm task and alert events can be evaluated against thresholds, timers, and rules
   - **Escalation record creation**
     - confirm created record contains `policyId`, `sourceEntityId`, `escalationLevel`, `reason`, `triggeredAt`
   - **Only once per level**
     - evaluate same event/source repeatedly and verify only one escalation per level is created in same lifecycle
     - mark entity resolved, reopen it, re-evaluate, and verify escalation can trigger again
   - **Auditability**
     - verify evaluation results and escalation actions are written to audit log
     - verify records are queryable/traceable by `correlationId`

5. Add/confirm automated tests for at least:
   - threshold-based escalation
   - timer-based escalation
   - rule-based escalation
   - duplicate suppression
   - resolved/reopened reset
   - audit log correlation

# Risks and follow-ups
- **Unknown existing policy model**  
  The repo may already store escalation rules inside agent/workflow/task JSON config. Reuse that model instead of creating a conflicting standalone policy system.

- **Alert model may not yet exist or may be partial**  
  If alert entities/events are not implemented, create the smallest normalized input abstraction needed without overbuilding alert infrastructure.

- **Resolved/reopened semantics may be ambiguous**  
  Be careful not to infer lifecycle resets from unrelated status changes. Reuse explicit domain transitions if available.

- **Concurrency/idempotency risk**  
  Repeated worker retries or concurrent event handling can create duplicates unless protected by both application checks and database constraints.

- **Audit schema uncertainty**  
  The architecture excerpt shows `audit_events` but the exact implemented schema may differ. Adapt to the real schema and ensure `correlationId` is persisted in a traceable way, whether as a first-class column or metadata.

- **Rule engine overcomplexity**  
  Do not build a generic scripting engine. Keep rule evaluation constrained, typed, and testable.

- **Follow-up candidates after this task**
  - notification fan-out for escalations
  - escalation inbox/dashboard surfacing
  - policy management UI
  - richer SLA/time-window conditions
  - outbox publication of escalation-created domain events