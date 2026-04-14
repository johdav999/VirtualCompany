# Goal
Implement backlog task **TASK-2.4.1 — Extend audit event schema to capture agent identity and behavior decision metadata** for **US-2.4 ST-A305 — Identity audit signals for traceability and consistency monitoring**.

Deliver a production-ready change in the existing .NET solution that:
- extends persisted audit event data for agent generation events,
- records agent identity and behavior decision metadata,
- exposes an internal query API filtered by **agent id** and **time range**,
- includes machine-readable reason codes for fallback identity and boundary delegation cases,
- and adds automated tests covering normal generation, fallback identity usage, and out-of-scope delegation.

Use the existing architecture and code conventions in the repository. Keep the implementation aligned with:
- modular monolith boundaries,
- PostgreSQL as source of truth,
- ASP.NET Core API,
- clean separation between Domain, Application, Infrastructure, and API layers,
- tenant-scoped access patterns.

# Scope
In scope:
- Extend the audit event domain/persistence model to support:
  - agent name,
  - agent role,
  - responsibility domain,
  - prompt profile version,
  - boundary decision outcome,
  - machine-readable reason code for fallback identity configuration,
  - machine-readable reason code for boundary delegation / out-of-scope delegation.
- Ensure **each agent generation event** persists the above metadata when applicable.
- Add or extend an **internal API endpoint** to query audit records by:
  - agent id,
  - time range.
- Add automated tests for:
  - normal generation event,
  - fallback identity usage,
  - out-of-scope delegation scenario.
- Add/adjust database migration(s) as needed.

Out of scope:
- UI work in Blazor or MAUI.
- Broad redesign of the audit subsystem.
- New external integrations.
- Non-audit observability/logging changes.
- Exposing raw chain-of-thought or sensitive prompt internals.

Implementation constraints:
- Preserve tenant isolation.
- Prefer additive, backward-compatible schema changes.
- Keep reason codes machine-readable and stable, ideally enum/value-object backed.
- Keep API internal-focused and minimal.
- Do not break existing audit consumers.

# Files to touch
Inspect the solution first and update the exact files that match the current implementation. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - audit event entity/value objects/enums
  - agent identity or orchestration-related domain types
- `src/VirtualCompany.Application/**`
  - commands/handlers/services that create audit events during generation
  - query handlers for audit retrieval
  - DTOs/contracts for internal API responses
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / persistence mappings
  - repository/query implementations
  - migrations for PostgreSQL schema updates
- `src/VirtualCompany.Api/**`
  - internal audit endpoint/controller/minimal API registration
  - request/response contracts if API-owned
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for query endpoint
  - audit creation tests through public/internal API surface
- `README.md` or relevant docs only if the repo already documents internal endpoints or migration workflow

Also inspect:
- existing migration patterns under `docs/postgresql-migrations-archive/README.md`
- any existing audit module files before introducing new abstractions

# Implementation plan
1. **Discover current audit implementation**
   - Find the current `audit_events` model, mappings, repository/query code, and any existing audit creation flow.
   - Identify where “agent generation events” are emitted today.
   - Identify whether audit metadata is stored in columns, JSONB, or both.
   - Reuse existing patterns instead of inventing parallel infrastructure.

2. **Design the schema extension**
   - Add fields needed for acceptance criteria in the most consistent way with the current model.
   - Prefer explicit columns for query-relevant fields and stable metadata, with JSON only if the current design already relies on it.
   - At minimum support:
     - `agent_id` if not already queryable for generation events,
     - `agent_name`,
     - `agent_role`,
     - `responsibility_domain`,
     - `prompt_profile_version`,
     - `boundary_decision_outcome`,
     - `reason_code` or separate reason code fields if clearer.
   - If the current schema already has generic actor fields, do not duplicate unnecessarily; extend carefully.
   - Add indexes to support querying by `agent_id` and time range efficiently.

3. **Introduce stable domain types**
   - Add enums/constants/value objects for:
     - boundary decision outcome,
     - audit reason codes.
   - Reason codes should be machine-readable and explicit, e.g. patterns like:
     - `identity_fallback_missing_config`
     - `identity_fallback_incomplete_profile`
     - `boundary_delegate_out_of_scope`
     - `boundary_delegate_policy_restriction`
   - Final names should fit existing naming conventions in the codebase.
   - Avoid magic strings scattered across handlers/tests.

4. **Update audit event creation flow**
   - Modify the application/orchestration path that records agent generation events so every generation audit event includes:
     - identity metadata,
     - prompt profile version,
     - boundary decision outcome,
     - reason code when fallback identity or delegation occurs.
   - Ensure normal generation stores a valid boundary decision outcome even when no reason code applies.
   - Ensure fallback identity usage records the fallback reason code.
   - Ensure out-of-scope delegation records the delegation outcome and reason code.
   - Keep rationale summaries concise and operational.

5. **Handle fallback and delegation scenarios explicitly**
   - Identify where agent identity resolution occurs.
   - If fallback identity configuration is used, propagate that fact into the audit creation call.
   - Identify where boundary/policy/delegation decisions are made.
   - Propagate the decision outcome and reason code into the audit event.
   - If needed, introduce a small internal result object to carry:
     - resolved identity source,
     - prompt profile version,
     - boundary decision outcome,
     - reason code.

6. **Add internal audit query API**
   - Implement or extend an internal endpoint to query audit records by:
     - `agentId`,
     - `from`,
     - `to`.
   - Enforce tenant scoping and validate inputs.
   - Return only appropriate audit fields; do not expose sensitive internals.
   - Include the new metadata in the response contract.
   - If a CQRS-lite query layer exists, add:
     - query object,
     - handler,
     - repository method.

7. **Persistence and migration**
   - Add EF Core configuration updates and a migration.
   - Ensure PostgreSQL types/indexes are correct.
   - Keep migration additive and safe for existing data.
   - For existing rows, allow nulls where necessary unless the current generation flow guarantees backfill.
   - If the repo uses SQL migration scripts instead of generated EF migrations, follow the established pattern exactly.

8. **Automated tests**
   - Add tests that verify audit event creation for:
     - **normal generation**
       - event exists,
       - identity metadata populated,
       - prompt profile version populated,
       - boundary decision outcome populated,
       - no inappropriate reason code.
     - **fallback identity usage**
       - event exists,
       - fallback identity metadata persisted,
       - machine-readable fallback reason code persisted.
     - **out-of-scope delegation**
       - event exists,
       - boundary decision outcome reflects delegation/denial path,
       - machine-readable reason code persisted.
   - Add API-level test for querying by agent id and time range.
   - Verify tenant isolation in query behavior if test infrastructure already supports it.

9. **Quality pass**
   - Remove duplication.
   - Ensure naming is consistent across domain, persistence, API, and tests.
   - Confirm no raw reasoning or sensitive prompt content is exposed.
   - Keep code comments minimal and useful.

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Specifically verify:
   - migration applies cleanly in the test/dev environment,
   - normal generation creates an audit event with:
     - agent name,
     - role,
     - responsibility domain,
     - prompt profile version,
     - boundary decision outcome.
   - fallback identity scenario creates an audit event with a machine-readable fallback reason code.
   - out-of-scope delegation scenario creates an audit event with a machine-readable delegation reason code.
   - internal API can query audit records by:
     - agent id,
     - time range.
   - query results are tenant-scoped and ordered consistently with existing conventions.

4. If the repo has endpoint tests or manual verification support, validate an example request such as:
   - `GET /internal/audit-events?agentId={agentId}&from={isoUtc}&to={isoUtc}`
   - Adjust route shape to match existing API conventions.

# Risks and follow-ups
- **Unknown current audit schema shape**: the repo may already store flexible metadata in JSONB; if so, prefer minimal disruption while still making agent/time-range querying efficient.
- **Generation event source may be indirect**: audit creation could happen in orchestration, task handling, or messaging layers; trace carefully to avoid missing scenarios.
- **Reason code semantics may overlap with policy engine**: align with existing policy/guardrail reason codes if they already exist instead of creating conflicting vocabularies.
- **Migration compatibility**: existing audit rows likely lack these fields; keep schema backward-compatible.
- **API authorization**: ensure the endpoint is internal and tenant-safe; do not accidentally expose cross-tenant audit data.
- **Test realism**: prefer integration-style tests through actual application flows over unit-only tests so persistence and API behavior are both verified.

Follow-ups to note in code comments or PR notes if not completed here:
- backfill strategy for historical audit rows, if needed later,
- richer audit filtering beyond agent/time range,
- standardized shared reason-code catalog across audit and policy modules,
- dashboard/audit UI consumption in later stories.