# Goal
Implement backlog task **TASK-2.4.3 — Build internal audit query API for filtering by agent and time range** for story **US-2.4 ST-A305 — Identity audit signals for traceability and consistency monitoring**.

Deliver an internal ASP.NET Core API and supporting domain/application/infrastructure changes so that:
- agent generation events persist identity metadata:
  - agent name
  - role
  - responsibility domain
  - prompt profile version
  - boundary decision outcome
- audit records are queryable by **agent id** and **time range**
- fallback identity configuration and boundary delegation scenarios include a **machine-readable reason code**
- automated tests cover:
  - normal generation
  - fallback identity usage
  - out-of-scope delegation

Keep the implementation aligned with the modular monolith, CQRS-lite, PostgreSQL-backed auditability approach, and tenant-scoped access patterns already described in the architecture.

# Scope
In scope:
- Extend the audit event model/schema to support identity audit metadata for generation events.
- Add machine-readable reason code support for fallback identity and boundary delegation outcomes.
- Ensure generation/orchestration flow writes audit events with the required metadata.
- Add an internal query endpoint to retrieve audit events filtered by:
  - company/tenant context
  - agent id
  - start time
  - end time
- Add application-layer query handling and infrastructure persistence/query logic.
- Add automated tests for creation and querying behavior.

Out of scope:
- End-user audit UI pages.
- Broad audit search across task/workflow/approval dimensions beyond this task.
- Refactoring unrelated orchestration flows unless required to hook audit creation cleanly.
- Mobile/web consumption changes.
- Full compliance/export bundles.

Assumptions to validate in code before implementing:
- There is already an audit module or audit event persistence path.
- There is an orchestration/generation service where agent generation events can be intercepted.
- Tenant/company context is already resolved in API requests.
- Internal API authorization conventions already exist; follow existing internal endpoint patterns.

# Files to touch
Inspect first, then update the most relevant existing files rather than creating parallel patterns.

Likely areas:

- **Domain**
  - `src/VirtualCompany.Domain/.../AuditEvent*.cs`
  - `src/VirtualCompany.Domain/.../Agent*.cs`
  - any enums/value objects for audit outcome/reason codes

- **Application**
  - `src/VirtualCompany.Application/.../Audit/...`
  - query DTOs/handlers for internal audit retrieval
  - orchestration/generation command handlers/services that emit audit events
  - contracts for audit event creation

- **Infrastructure**
  - `src/VirtualCompany.Infrastructure/.../Persistence/...`
  - EF Core entity configurations for `audit_events`
  - repositories/query services
  - migrations for schema changes

- **API**
  - `src/VirtualCompany.Api/.../Controllers/...`
  - or minimal API endpoint registration for internal audit queries
  - request/response contracts
  - authorization/policy wiring if needed

- **Tests**
  - `tests/VirtualCompany.Api.Tests/...`
  - add integration/API tests for query endpoint
  - add tests for audit event creation scenarios

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration conventions and local validation expectations.

# Implementation plan
1. **Discover existing audit and orchestration patterns**
   - Find the current audit event entity, persistence model, and any existing audit write path.
   - Find where agent generation events are created or where orchestration responses are finalized.
   - Reuse existing CQRS, repository, and endpoint conventions.

2. **Design the audit event shape for this task**
   - Add fields needed for identity traceability, preferably in a way consistent with the architecture’s flexible audit model.
   - If the existing schema already has JSONB metadata/details columns, prefer extending those over introducing many rigid columns unless queryability requires otherwise.
   - Ensure the persisted event can represent:
     - `agentId`
     - `agentName`
     - `agentRole`
     - `responsibilityDomain`
     - `promptProfileVersion`
     - `boundaryDecisionOutcome`
     - `reasonCode` for fallback/delegation cases
     - event timestamp
     - tenant/company id
   - Use explicit enums/constants for machine-readable values, not free-form strings scattered across code.

3. **Define machine-readable reason codes**
   - Introduce a small, stable set of reason codes for this task, for example:
     - `fallback_identity_configuration_used`
     - `boundary_out_of_scope_delegated`
     - `boundary_policy_delegated`
   - Only add codes actually supported by current behavior.
   - Keep names consistent and future-safe.
   - Add outcome values for boundary decisions if not already modeled, such as:
     - `allowed`
     - `fallback_used`
     - `delegated_out_of_scope`
     - `denied`
   - Prefer central constants/enums in Domain or Application contracts.

4. **Persist audit events during generation**
   - Update the generation/orchestration flow so every agent generation event writes an audit event containing the required identity metadata.
   - Cover these scenarios:
     - **normal generation**: standard identity metadata, no fallback reason code unless applicable
     - **fallback identity usage**: include fallback outcome and reason code
     - **out-of-scope delegation**: include delegation outcome and reason code
   - Keep audit creation separate from HTTP concerns.
   - Preserve tenant scoping and correlation identifiers if already present.

5. **Implement internal audit query API**
   - Add an internal endpoint following existing API conventions.
   - Suggested shape:
     - `GET /internal/audit-events?agentId={id}&from={iso}&to={iso}`
   - Validate:
     - `agentId` required
     - `from` and `to` required
     - `from <= to`
     - reasonable max range if the codebase already enforces such limits
   - Ensure tenant/company scoping is enforced in the query.
   - Return a concise response model containing the fields needed for internal consumers, including identity metadata and reason code.

6. **Add application query handler**
   - Implement a query object and handler/service for filtered audit retrieval.
   - Query by:
     - company id
     - agent id
     - timestamp range
   - Sort descending by event timestamp unless an existing convention says otherwise.
   - Keep the query read-only and efficient.

7. **Update persistence and migration**
   - Add/adjust EF Core configuration and database migration for any new columns/indexes.
   - If querying by `agent_id` and time range, ensure indexing supports it.
   - Recommended index shape if schema supports direct columns:
     - `(company_id, agent_id, created_at)` or equivalent timestamp column
   - If metadata is stored in JSONB, be careful not to degrade queryability; prefer direct columns for filter keys.

8. **Add tests**
   - Automated tests must verify:
     - audit event creation for normal generation
     - audit event creation for fallback identity usage with machine-readable reason code
     - audit event creation for out-of-scope delegation with machine-readable reason code
     - internal API query returns only records for the specified agent and time range
     - tenant isolation is preserved
   - Prefer integration tests where practical, especially for API + persistence behavior.

9. **Keep implementation minimal and aligned**
   - Do not invent a large generic audit framework if a smaller extension solves the task.
   - Reuse existing naming, folder structure, and endpoint style.
   - Add brief code comments only where intent is not obvious.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow, generate/apply them using the repository’s established convention after inspecting existing migration guidance in:
   - `docs/postgresql-migrations-archive/README.md`

4. Manually verify the endpoint behavior with test data:
   - create or use audit events for multiple agents and timestamps
   - query with one `agentId`
   - query with a bounded `from/to`
   - confirm only matching tenant-scoped records are returned

5. Confirm acceptance criteria explicitly:
   - generation events store all required identity metadata
   - query by agent id and time range works through internal API
   - fallback/delegation events include machine-readable reason code
   - automated tests cover all required scenarios

# Risks and follow-ups
- **Unknown existing audit schema**: if the current model is incomplete or inconsistent, avoid broad redesign; extend it surgically.
- **Unknown orchestration hook points**: generation events may be emitted in multiple places; ensure the chosen hook covers all relevant generation paths.
- **Queryability vs flexibility tradeoff**: storing everything in JSONB is flexible but may make agent/time filtering inefficient; use direct columns for filter keys if needed.
- **Tenant isolation risk**: internal endpoints must still enforce company scoping.
- **Naming drift**: reason codes and boundary outcomes must be centralized to avoid inconsistent string literals.

Follow-ups after this task, if not already covered elsewhere:
- add broader audit filtering by task/workflow/outcome
- expose audit history in manager-facing UI
- add pagination/cursor support if audit volume grows
- consider exported audit bundles and retention policies