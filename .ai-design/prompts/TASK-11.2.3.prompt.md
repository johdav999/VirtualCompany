# Goal
Implement backlog task **TASK-11.2.3** for **ST-502 Shared orchestration pipeline for single-agent tasks** so that the shared orchestration **tool executor**:

- **returns structured results only**
- **records execution metadata**
- aligns with the architecture requirement that tool execution is policy-enforced, tenant-scoped, and auditable
- supports downstream orchestration artifacts for tasks and audit trails

This task should produce a clean application/infrastructure implementation in the existing .NET solution, with tests, and without introducing UI-specific concerns into orchestration services.

# Scope
In scope:

- Identify the current orchestration/tool execution abstractions and implementation.
- Enforce a **single structured result contract** for tool execution responses.
- Prevent raw/unstructured/free-form tool outputs from being returned by the executor.
- Record execution metadata for every tool execution attempt, at minimum covering:
  - correlation/execution ID
  - company/tenant ID
  - agent ID
  - task ID and/or workflow instance ID when available
  - tool name
  - action type if available
  - request payload
  - structured response payload
  - status
  - started/completed timestamps
  - policy decision metadata if already available in the pipeline
- Persist metadata in a way consistent with the architecture and existing persistence patterns, ideally using the `tool_executions` model/table or the nearest existing equivalent.
- Add/adjust tests for structured result enforcement and metadata persistence.

Out of scope unless required by existing code structure:

- Building new UI screens
- Full approval workflow changes
- New external integrations
- Broad redesign of the orchestration engine beyond what is necessary for this task
- Large schema redesigns unrelated to tool execution metadata

If the codebase already has partial implementations for ST-502/ST-503, extend them rather than duplicating concepts.

# Files to touch
Inspect first, then update the smallest coherent set of files. Likely areas:

- `src/VirtualCompany.Application/**`
  - orchestration service interfaces and DTOs
  - tool executor contracts
  - command/query handlers if tool execution is application-driven
- `src/VirtualCompany.Domain/**`
  - domain models/value objects/enums for tool execution status/result if they belong in domain
- `src/VirtualCompany.Infrastructure/**`
  - tool executor implementation
  - persistence/repositories/EF configurations
  - database entity mappings for `tool_executions`
- `src/VirtualCompany.Api/**`
  - only if DI registration or API-facing contracts need wiring changes
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution already centralizes orchestration DTOs there
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if orchestration is exercised through API
- other relevant test projects if present under `tests/**`

Also inspect:

- existing migrations or persistence setup related to `tool_executions`
- any orchestration pipeline classes under names like:
  - `OrchestrationService`
  - `ToolExecutor`
  - `IToolExecutor`
  - `ToolExecution*`
  - `Policy*`
  - `AgentRuntime*`

Do **not** edit archived docs unless absolutely necessary.

# Implementation plan
1. **Discover the current implementation**
   - Find the shared orchestration pipeline and current tool execution path.
   - Identify:
     - current tool executor interface
     - current tool result shape
     - where correlation IDs and runtime context are carried
     - whether `tool_executions` persistence already exists
     - whether policy decision metadata is already available at execution time
   - Summarize findings in code comments only where helpful; do not add broad documentation files unless needed.

2. **Define a structured tool result contract**
   - Introduce or refine a single result DTO/model for tool execution responses.
   - The contract should be explicitly structured, for example including fields such as:
     - `Success`
     - `Status`
     - `ToolName`
     - `ActionType`
     - `Data` or `Payload` as structured object/JSON-compatible shape
     - `ErrorCode`
     - `ErrorMessage` as safe operational text
     - `Metadata` if needed
   - Ensure the executor returns this contract consistently for:
     - success
     - denied/blocked execution
     - validation failure
     - downstream tool failure
   - Avoid returning raw strings, arbitrary object graphs, or provider-specific response types directly.

3. **Enforce “structured results only”**
   - Refactor the executor so all tool implementations are normalized into the structured result contract before returning.
   - If existing tools return primitives or strings, wrap/transform them into the structured payload shape.
   - If any code path currently returns free-form text, replace it with a structured error/result object.
   - Add guard clauses so unsupported/unstructured tool responses fail safely and predictably.

4. **Capture execution metadata**
   - Create or refine a metadata model/entity for tool execution records aligned to the architecture’s `tool_executions` schema.
   - Ensure each execution attempt records:
     - tenant/company ID
     - agent ID
     - task/workflow linkage where available
     - tool name
     - action type
     - request JSON
     - response JSON
     - status
     - policy decision JSON if available
     - started/completed timestamps
   - Include denied/failed executions where feasible, not only successful ones, because auditability is a core architectural requirement.

5. **Persist metadata through the proper layer**
   - Use existing repository/unit-of-work/DbContext patterns in the solution.
   - If the `tool_executions` table/entity does not yet exist in code but is clearly intended by the architecture, add the minimal implementation needed:
     - entity
     - configuration
     - repository access
     - migration if this repo uses active migrations
   - Follow existing naming, module boundaries, and tenant-scoping conventions.

6. **Thread runtime context through the executor**
   - Ensure the executor receives enough context to record metadata without reaching into HTTP concerns.
   - Prefer an explicit execution context object containing:
     - company ID
     - agent ID
     - task ID
     - workflow instance ID
     - correlation ID
     - actor/runtime info as already modeled
   - Keep orchestration service separate from controllers/endpoints.

7. **Handle policy metadata cleanly**
   - If policy checks already happen before execution, capture the policy decision object or a serialized subset in the execution record.
   - If policy is not yet integrated in this path, do not invent a large new policy engine here; instead:
     - store null/empty policy metadata where unavailable
     - leave a clear extension point for ST-503 alignment

8. **Update orchestration consumers**
   - Adjust any orchestration pipeline code that consumes tool results so it expects the structured contract.
   - Ensure downstream task/audit artifact generation can rely on structured payloads rather than parsing text.

9. **Add tests**
   - Add unit and/or integration tests covering:
     - executor returns structured result on success
     - executor returns structured result on failure
     - executor does not return raw string/untyped output
     - metadata record is created with expected fields
     - timestamps/status are set correctly
     - tenant/agent/task correlation is preserved
   - Prefer deterministic tests with fake tools and in-memory/test persistence where appropriate.

10. **Keep changes production-safe**
   - Preserve backward compatibility where practical, but prioritize the backlog requirement.
   - If a breaking contract change is unavoidable internally, update all call sites in the same change.
   - Keep serialization safe and avoid storing sensitive secrets in request/response metadata.

# Validation steps
Run the relevant validation locally after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test suites for orchestration/tool execution, run those specifically as well.

4. Manually verify in code that:
   - the tool executor interface returns a structured result type only
   - all execution paths populate structured response payloads
   - metadata persistence occurs for execution attempts
   - persisted records include tenant and runtime linkage fields where available
   - no HTTP/controller types leak into application/infrastructure orchestration code

5. If migrations are part of the repo workflow:
   - ensure any new schema changes compile and are wired correctly
   - do not modify archived migration docs as a substitute for real implementation

# Risks and follow-ups
- **Schema mismatch risk:** The architecture defines `tool_executions`, but the current codebase may not yet model it. Add only the minimal persistence needed and follow existing migration conventions.
- **Contract ripple risk:** Changing tool result types may affect multiple orchestration components. Update all internal consumers in the same task.
- **Serialization risk:** Request/response payloads may contain non-serializable or overly large objects. Normalize to JSON-safe structured DTOs.
- **Sensitive data risk:** Do not persist secrets, tokens, or raw provider internals in execution metadata.
- **Policy integration gap:** If policy decision metadata is not yet available in this path, leave a clean extension point rather than overbuilding.
- **Audit alignment follow-up:** A later task may need to fan tool execution outcomes into business audit events for ST-503/ST-602 if not already present.
- **Correlation follow-up:** If correlation IDs are inconsistently propagated today, note and tighten propagation in adjacent orchestration tasks, but keep this task focused on executor result structure and metadata recording.