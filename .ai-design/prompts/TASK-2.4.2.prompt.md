# Goal
Implement backlog task **TASK-2.4.2 — Emit audit events from generation pipeline and boundary enforcement middleware** for story **US-2.4 ST-A305 — Identity audit signals for traceability and consistency monitoring**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that ensures:

- every agent generation emits a business audit event with identity metadata
- boundary enforcement / policy middleware emits audit events for allow/deny/delegation outcomes
- fallback identity configuration and boundary delegation include **machine-readable reason codes**
- audit records are queryable by **agent id + time range** through an internal API endpoint
- automated tests cover:
  - normal generation
  - fallback identity usage
  - out-of-scope delegation

Keep the implementation aligned with the architecture principle that **auditability is a domain feature, not just logging**.

# Scope
In scope:

- Extend the audit domain/model to persist generation and boundary-enforcement audit events
- Capture and store identity metadata:
  - agent name
  - role
  - responsibility domain
  - prompt profile version
  - boundary decision outcome
- Capture machine-readable reason codes when:
  - fallback identity configuration is used
  - boundary delegation occurs
- Hook audit emission into:
  - shared generation/orchestration pipeline
  - boundary/policy enforcement middleware or equivalent guardrail component
- Add internal API query support for audit events filtered by:
  - agent id
  - time range
- Add automated tests for required scenarios

Out of scope unless required by existing patterns:

- UI work in Blazor or MAUI
- broad redesign of orchestration or policy engine
- external/public API exposure
- replacing existing logging/telemetry
- unrelated audit view UX

# Files to touch
Inspect the solution first and update the exact files that match existing conventions. Likely areas:

- `src/VirtualCompany.Domain/**`
  - audit event entity/value objects/enums
  - reason code and boundary outcome types
- `src/VirtualCompany.Application/**`
  - orchestration/generation application services
  - audit write service/command handlers
  - audit query DTOs and query handlers
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository implementation
  - migrations or persistence mappings
  - internal API data access
- `src/VirtualCompany.Api/**`
  - internal audit endpoint/controller/minimal API
  - DI registration if needed
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - integration tests for audit creation/querying
- potentially:
  - existing orchestration pipeline classes
  - policy guardrail / middleware classes
  - shared contracts in `src/VirtualCompany.Shared/**`

Before coding, locate:
- current `audit_events` persistence model
- orchestration pipeline entry point for generation
- policy/boundary enforcement middleware or guardrail engine
- existing internal API patterns for filtered queries
- existing test fixtures for API/integration tests

# Implementation plan
1. **Discover current implementation and map extension points**
   - Find the current audit event entity/table mapping and confirm whether `audit_events` already exists in code.
   - Find the generation pipeline entry point used by single-agent orchestration.
   - Find the boundary enforcement component that evaluates scope / deny / delegate / fallback behavior.
   - Identify whether “responsibility domain” and “prompt profile version” already exist in agent config/runtime context; if not, derive them from the closest authoritative source and document the fallback.

2. **Define audit event shape for this task**
   Add or extend a business audit record model to support generation/boundary events with structured metadata. Prefer explicit columns where already modeled, otherwise use a structured JSON payload consistent with current architecture.

   Required persisted fields for these events:
   - tenant/company id
   - actor type = `agent` where applicable
   - actor/agent id
   - action, e.g.:
     - `agent_generation`
     - `boundary_enforcement`
   - outcome
   - timestamp
   - identity metadata:
     - `agentName`
     - `agentRole`
     - `responsibilityDomain`
     - `promptProfileVersion`
     - `boundaryDecisionOutcome`
   - optional machine-readable reason code:
     - fallback identity reason
     - boundary delegation reason
   - correlation identifiers if already supported by the platform
   - target/task/workflow references if available from runtime context

   Use stable machine-readable enums/constants for:
   - boundary decision outcome
   - audit reason code

   Example reason code set, adjust to existing naming conventions:
   - `FallbackIdentityConfigurationUsed`
   - `MissingPromptProfileVersion`
   - `OutOfScopeDelegation`
   - `BoundaryDelegationRequired`
   - `PolicyScopeExceeded`

3. **Add domain types**
   Introduce strongly typed enums/value objects/constants for:
   - generation audit action names
   - boundary decision outcomes
   - audit reason codes

   Keep them reusable across application/infrastructure/tests. Avoid magic strings scattered through the codebase.

4. **Persist the new metadata**
   Update persistence so audit events can store the required identity metadata and reason code.
   - If the project already stores flexible audit metadata in JSON, extend that payload and mapping.
   - If the project uses explicit columns, add the necessary columns and create a migration.
   - Ensure queryability by `agent id` and time range is efficient; add indexes if needed, especially on:
     - `company_id`
     - `actor_id` or `agent_id`
     - `created_at` / event timestamp

5. **Emit audit event from generation pipeline**
   In the shared orchestration/generation flow:
   - after agent resolution and prompt profile resolution, but within the same business operation, create an audit event for generation
   - populate:
     - agent identity metadata
     - prompt profile version
     - boundary decision outcome from the current policy/boundary result
   - if fallback identity configuration is used, include the machine-readable reason code
   - ensure event creation happens for successful normal generation and fallback generation paths

   Do not emit only technical logs; persist business audit records.

6. **Emit audit event from boundary enforcement middleware/guardrail**
   In the policy guardrail / boundary enforcement component:
   - emit an audit event whenever a boundary decision is made that is relevant to traceability, especially:
     - allow
     - deny
     - delegate / out-of-scope delegation
     - fallback/delegated identity handling if applicable
   - include:
     - boundary decision outcome
     - reason code when delegation or fallback occurs
     - agent identity metadata available at decision time

   Ensure denied/delegated scenarios are auditable even if generation/tool execution does not proceed.

7. **Implement internal audit query API**
   Add an internal endpoint that supports querying audit records by:
   - `agentId`
   - `from`
   - `to`

   Requirements:
   - tenant/company scoping must be enforced
   - validate date range inputs
   - return only relevant audit records, ordered by timestamp descending unless existing conventions differ
   - include the structured identity metadata and reason code in the response DTO
   - follow existing internal API routing/auth conventions

   Prefer CQRS-lite:
   - query DTO
   - query handler/service
   - API endpoint/controller

8. **Add tests**
   Add automated tests that verify audit event creation and querying for:
   - **normal generation**
     - generation pipeline creates an audit event with all required identity metadata
   - **fallback identity usage**
     - audit event includes machine-readable fallback reason code
   - **out-of-scope delegation**
     - boundary enforcement emits an audit event with delegation outcome and reason code
   - **query endpoint**
     - can filter by agent id and time range
     - excludes records outside range or for other agents/tenants

   Prefer integration-style tests where possible so persistence + API behavior are both validated.

9. **Keep implementation safe and consistent**
   - Preserve tenant isolation in all reads/writes
   - Reuse existing clock abstraction if present
   - Reuse correlation ID propagation if present
   - Avoid exposing raw chain-of-thought or prompt internals beyond accepted metadata
   - Keep reason codes machine-readable and stable

10. **Document assumptions in code comments or PR notes**
   If “responsibility domain” or “prompt profile version” are not first-class fields today:
   - derive from existing agent config/template/runtime prompt profile
   - centralize the derivation logic
   - avoid duplicating inference logic in multiple places

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify persistence changes:
   - confirm audit event schema/mapping supports required metadata and reason code
   - confirm migration is included if schema changed

4. Verify scenario behavior manually via tests or local API execution:
   - normal generation creates an audit event with:
     - agent name
     - role
     - responsibility domain
     - prompt profile version
     - boundary decision outcome
   - fallback identity path creates an audit event with machine-readable fallback reason code
   - out-of-scope delegation creates an audit event with delegation outcome + reason code

5. Verify internal API:
   - query by `agentId`, `from`, `to`
   - confirm tenant scoping
   - confirm ordering and payload shape
   - confirm records outside the range are excluded

6. Verify no regressions:
   - existing orchestration tests still pass
   - existing policy/guardrail tests still pass
   - no duplicate audit events are emitted unintentionally for a single decision point unless explicitly designed

# Risks and follow-ups
- **Schema ambiguity risk:** the architecture excerpt shows `audit_events` only partially; inspect the actual code before deciding between explicit columns vs JSON metadata.
- **Source-of-truth risk:** `responsibility domain` and `prompt profile version` may not yet be modeled consistently. If missing, implement a single derivation strategy and note it.
- **Duplicate event risk:** generation and boundary enforcement may both emit events for the same request. Ensure event semantics are distinct and intentional.
- **Tenant isolation risk:** query endpoint must enforce company scoping rigorously.
- **Performance risk:** querying by agent id and time range may need indexes if audit volume is high.
- **Follow-up suggestion:** if not already present, standardize audit event action names/outcomes/reason codes in a shared contract for future stories.
- **Follow-up suggestion:** consider adding correlation-id-based audit tracing across task, tool execution, approval, and generation flows in a later task.