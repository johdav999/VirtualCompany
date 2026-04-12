# Goal
Implement backlog task **TASK-11.3.2** for **ST-503 Policy-enforced tool execution** so that **allowed tool executions are persisted in `tool_executions`** with:
- tenant/company context
- agent/task/workflow linkage where available
- tool request payload
- tool response payload
- execution status/timestamps
- structured **policy decision metadata**

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant isolation**, and keep tool execution flowing through **typed internal contracts** rather than direct database access.

# Scope
In scope:
- Add or complete the domain/application/infrastructure support needed to persist allowed tool executions.
- Ensure persistence happens for **allowed** executions in the orchestration/tool execution path.
- Persist both:
  - the **request/response**
  - the **policy decision metadata**
- Ensure the stored record includes the identifiers described by architecture where available:
  - `company_id`
  - `agent_id`
  - `task_id` nullable
  - `workflow_instance_id` nullable
  - `tool_name`
  - `action_type`
  - `status`
  - `started_at`
  - `completed_at`
- Add/update database migration(s) if the `tool_executions` table does not yet exist or does not match the architecture contract.
- Add tests covering persistence behavior.

Out of scope unless required to make this task work:
- Full denied-execution audit implementation beyond preserving existing behavior.
- New UI/screens.
- New external connector implementations.
- Broad refactors of the orchestration engine unrelated to execution persistence.
- Reworking the entire policy engine.

# Files to touch
Inspect the solution first and then touch the minimum necessary files in the relevant layers. Likely areas:

- `src/VirtualCompany.Domain/**`
  - tool execution entity/value objects/enums if missing
- `src/VirtualCompany.Application/**`
  - orchestration/tool execution contracts
  - command/service handlers for recording executions
  - DTOs for policy decision metadata
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / persistence mappings
  - repository implementation
  - migration(s)
  - JSONB converters/configuration if used
- `src/VirtualCompany.Api/**`
  - only if wiring/DI or endpoint composition needs adjustment
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if orchestration path is exposed there
- potentially additional test projects under `tests/**` if there are application/infrastructure tests

Also inspect:
- existing migration approach referenced by `docs/postgresql-migrations-archive/README.md`
- solution conventions from `README.md`

# Implementation plan
1. **Discover existing implementation and conventions**
   - Inspect current orchestration, policy, and tool execution code paths.
   - Find whether `tool_executions` already exists in:
     - domain model
     - DbContext
     - EF configurations
     - migrations
   - Identify where a tool execution becomes “allowed” and where the actual tool invocation occurs.
   - Reuse existing naming, result, error, and timestamp conventions.

2. **Model the persisted tool execution record**
   - Ensure there is a persistence model aligned to architecture:

   ```sql
   tool_executions (
     id uuid pk,
     company_id uuid fk,
     task_id uuid fk null,
     workflow_instance_id uuid fk null,
     agent_id uuid fk,
     tool_name text,
     action_type text,
     request_json jsonb,
     response_json jsonb,
     status text,
     policy_decision_json jsonb,
     started_at timestamptz,
     completed_at timestamptz null
   )
   ```

   - If an entity already exists, extend it rather than duplicating it.
   - Prefer strongly typed domain/application models for:
     - action type
     - status
     - policy decision payload
   - Store request/response/policy decision as structured JSON/JSONB, not flattened strings.

3. **Define policy decision metadata contract**
   - Introduce or reuse a structured object for policy decision metadata that captures the decision context for allowed executions.
   - Include enough metadata to satisfy auditability and future explainability, such as:
     - decision outcome (allowed)
     - evaluated action type
     - autonomy level considered
     - threshold evaluation summary if available
     - approval requirement/result if applicable
     - reasons/rules applied
   - Do not invent chain-of-thought fields.
   - Keep it serializable and stable for JSONB persistence.

4. **Add persistence wiring**
   - Add/update EF Core configuration for `tool_executions`.
   - Ensure PostgreSQL JSONB mapping is used for:
     - `request_json`
     - `response_json`
     - `policy_decision_json`
   - Add indexes that are sensible for expected access patterns if consistent with project conventions, likely on:
     - `company_id`
     - `agent_id`
     - `task_id`
     - `workflow_instance_id`
     - `started_at`
   - Add/update migration(s) accordingly.

5. **Persist allowed executions in the tool execution flow**
   - In the shared orchestration/tool executor path:
     - once policy evaluation returns **allowed**
     - create a `tool_executions` record with `started_at`
     - execute the tool
     - update the same record with:
       - `response_json`
       - final `status`
       - `completed_at`
   - If the existing architecture executes synchronously, persist in the same request flow.
   - If there is already a unit-of-work pattern, integrate cleanly with it.
   - Ensure the persisted record includes the current tenant/company and agent context.
   - Preserve nullable task/workflow linkage when not present.

6. **Status handling**
   - Use existing status conventions if present.
   - If missing, introduce minimal statuses needed for this task, e.g.:
     - `started` / `succeeded` / `failed`
   - Even though the backlog line is specifically about allowed executions, persist the record for allowed executions regardless of whether the downstream tool call succeeds or fails, so request/policy metadata is not lost.
   - Keep denied execution handling unchanged unless a small adjustment is necessary.

7. **Keep internal tool invocation typed**
   - Ensure internal tools continue to call domain/application services through typed contracts/interfaces.
   - Do not introduce direct DB access from tool handlers.
   - If current code violates this, make only the smallest correction necessary in this path.

8. **Testing**
   - Add tests that verify:
     - an allowed tool execution creates a `tool_executions` row
     - request payload is persisted
     - response payload is persisted
     - policy decision metadata is persisted
     - company/agent/task/workflow identifiers are correctly stored
     - timestamps/status are set
   - Prefer integration tests against the real persistence layer if the project already supports them.
   - If only unit tests are practical, add focused tests around the application service plus repository behavior.

9. **Keep implementation narrow**
   - Do not add UI work.
   - Do not redesign the policy engine.
   - Do not broaden schema beyond what is needed for this task and architectural consistency.

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration state:
   - confirm the new/updated migration is included in the infrastructure migration set
   - confirm schema matches the architecture contract for `tool_executions`

4. Manually validate via tests or a focused execution path:
   - trigger an **allowed** tool execution
   - confirm a row is written to `tool_executions`
   - confirm:
     - `company_id`
     - `agent_id`
     - `task_id` / `workflow_instance_id` when applicable
     - `tool_name`
     - `action_type`
     - `request_json`
     - `response_json`
     - `policy_decision_json`
     - `status`
     - `started_at`
     - `completed_at`
   - confirm JSON fields are structured and queryable, not opaque text blobs if project conventions support JSONB

5. Regression check:
   - ensure denied executions still return safe behavior
   - ensure no tenant leakage in queries or persistence
   - ensure no direct DB access was introduced into tool handlers

# Risks and follow-ups
- **Schema drift risk:** the current codebase may already have a partial `tool_executions` implementation with different names/types. Prefer extending existing structures over parallel models.
- **Serialization risk:** request/response/policy objects may contain non-serializable members or overly large payloads. Keep payloads structured and bounded; avoid runtime-only objects.
- **Transaction boundary risk:** if execution persistence and tool invocation happen across different transaction scopes, records may be partially written. Use the project’s existing unit-of-work/transaction conventions.
- **Status ambiguity:** if there is no established execution status enum, keep the introduced status set minimal and consistent.
- **Tenant isolation risk:** ensure `company_id` is always sourced from trusted execution context, not caller-provided payloads.
- **Follow-up likely needed:** a later task may need richer denied-execution persistence/audit linkage and query surfaces for ST-602 audit views.