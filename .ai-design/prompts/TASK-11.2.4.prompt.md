# Goal
Implement **TASK-11.2.4 / ST-502** so the shared single-agent orchestration pipeline returns a **composite final result** containing:
1. **user-facing output** for chat/task consumers, and
2. **structured task/audit artifacts** suitable for persistence, downstream processing, and explainability.

The implementation should fit the existing **.NET modular monolith** architecture, keep orchestration logic separate from HTTP/UI concerns, and preserve correlation across orchestration, task, tool, and audit records.

# Scope
In scope:
- Add or refine a **structured orchestration result contract** in the application/domain layer.
- Ensure the orchestration pipeline produces:
  - user-visible response text/content
  - rationale/explanation summary
  - task output payload
  - audit/event artifact payload(s)
  - source/data reference metadata if already available in pipeline context
  - correlation identifiers
- Update the single-agent orchestration service to populate this result consistently for both:
  - pure conversational responses
  - task/action-oriented responses
- Wire persistence-facing artifacts so callers can store task output and audit records without reconstructing them from free text.
- Add/adjust tests covering the final response shape and expected behavior.

Out of scope:
- Building full UI rendering changes unless required by compile-time contracts.
- Implementing new external tools/integrations.
- Multi-agent coordination behavior from ST-504.
- Large schema redesigns unless a minimal persistence contract change is required.

Implementation constraints:
- Keep the shared orchestration engine generic across agents.
- Do **not** expose raw chain-of-thought.
- Prefer concise rationale summaries and structured metadata.
- Preserve tenant/correlation context where available.
- Follow existing project layering and naming conventions already present in the repo.

# Files to touch
Inspect first, then modify the minimum necessary set in these likely areas:

- `src/VirtualCompany.Application/**`
  - orchestration service interfaces/contracts
  - DTOs/result models
  - task/audit application models
  - command/query handlers that consume orchestration output
- `src/VirtualCompany.Domain/**`
  - value objects/entities if structured artifacts belong in domain contracts
- `src/VirtualCompany.Infrastructure/**`
  - persistence mapping/adapters if orchestration result is persisted here
  - LLM/tool execution orchestration implementation
- `src/VirtualCompany.Api/**`
  - API response mapping only if endpoint contracts must surface the new composite result
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this repo already centralizes cross-layer DTOs there
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- potentially other test projects under `tests/**` if application-layer tests exist

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, locate the current implementations for:
- orchestration pipeline / orchestration service
- prompt builder result handling
- tool execution result handling
- task creation/update persistence
- audit event creation/persistence
- API/chat/task response DTOs

# Implementation plan
1. **Discover existing orchestration contracts**
   - Find the current single-agent orchestration entry point and all result types it returns.
   - Identify where final LLM output is transformed into:
     - API response
     - task output payload
     - rationale summary
     - audit event(s)
   - Document the current flow in code comments only where helpful; avoid broad refactors.

2. **Design a composite final result model**
   - Introduce or refine a single result contract that clearly separates:
     - `UserOutput` / `DisplayMessage`
     - `TaskArtifact` or `TaskUpdate`
     - `AuditArtifacts` / `AuditEvents`
     - `RationaleSummary`
     - `DataSources` / `SourceReferences`
     - `ToolExecutionReferences` if available
     - `CorrelationId`
   - Keep naming aligned with existing conventions.
   - Make the contract serialization-friendly and deterministic for tests.
   - Prefer immutable records if consistent with the codebase.

3. **Update orchestration pipeline assembly**
   - Modify the shared orchestration service so the final step explicitly builds the composite result rather than returning only text or an ad hoc payload.
   - Ensure the user-facing output is concise and safe.
   - Ensure structured artifacts are derived from actual orchestration state, not reparsed from generated prose.
   - If the pipeline already has intermediate structured outputs, reuse them directly.

4. **Map to task artifacts**
   - Ensure the final result includes a task-oriented artifact payload suitable for persistence into task fields such as:
     - output payload
     - rationale summary
     - confidence score if available
   - If task persistence is handled outside the orchestrator, provide a clean artifact object for the caller to persist.
   - Avoid coupling the orchestrator directly to HTTP or EF concerns unless that pattern already exists.

5. **Map to audit artifacts**
   - Ensure the final result includes structured audit-ready data such as:
     - actor type/id if available
     - action
     - target type/id if available
     - outcome
     - rationale summary
     - data sources used
     - correlation id
   - If audit persistence is performed by another layer, return audit artifact DTOs rather than writing directly from the orchestrator unless existing architecture already does so.

6. **Preserve explainability boundaries**
   - Return concise rationale summaries only.
   - Do not store or expose raw chain-of-thought.
   - If source references exist from retrieval/tooling, include human-usable references in structured form.

7. **Update API/application mappings**
   - If current endpoints return only plain text, update mappings so they can expose the user-facing portion while still retaining structured artifacts internally.
   - If an endpoint is expected to return the full composite result for internal consumers, ensure backward compatibility where practical.
   - Minimize breaking changes; if needed, adapt response DTOs carefully.

8. **Add tests**
   - Add/adjust unit and/or integration tests to verify:
     - orchestration returns both user-facing output and structured artifacts
     - rationale summary is present and not raw reasoning
     - task artifact is populated for task-oriented flows
     - audit artifact(s) are populated with expected metadata
     - correlation id flows through result
     - null/empty handling for optional fields is stable
   - Prefer tests at the application/service boundary plus one API-level test if endpoint contracts changed.

9. **Keep implementation incremental**
   - Avoid broad architectural cleanup unrelated to this task.
   - If you discover missing prerequisites, implement the smallest viable supporting change and note follow-ups.

# Validation steps
1. Restore/build:
   - `dotnet build VirtualCompany.sln`

2. Run tests:
   - `dotnet test`

3. If API contracts changed, run relevant API tests and verify:
   - final response includes user-facing content
   - structured task artifact is present or internally mapped
   - structured audit artifact is present or internally mapped

4. Manually inspect serialization/output shape in tests or debug output for:
   - stable property names
   - no raw chain-of-thought leakage
   - correlation id present
   - rationale summary concise
   - source references included when available

5. Confirm layering:
   - orchestration logic remains outside controllers/UI
   - persistence concerns are not unnecessarily duplicated
   - no tenant/correlation context is dropped in the new result path

# Risks and follow-ups
- **Risk: existing contracts are already consumed widely.**
  - Mitigate by extending result models compatibly or adding adapter mappings rather than replacing public contracts abruptly.

- **Risk: task and audit persistence responsibilities may be split across layers.**
  - Mitigate by returning structured artifacts from orchestration and letting existing handlers persist them.

- **Risk: rationale fields may accidentally capture raw model reasoning.**
  - Mitigate by explicitly using summary/explanation fields only and reviewing tests for leakage.

- **Risk: correlation/source metadata may not currently be threaded end-to-end.**
  - Mitigate by propagating existing IDs first; if gaps remain, add minimal plumbing and note broader observability follow-up.

- **Risk: endpoint shape changes could break clients.**
  - Mitigate by preserving existing user-facing fields and adding structured fields non-destructively where possible.

Follow-ups to note if not completed here:
- standardize orchestration result contracts across chat, task, and workflow entry points
- align audit artifact schema with ST-602 explainability views
- ensure tool execution references and retrieval source references are consistently attached across all orchestration paths
- add explicit contract documentation/examples for downstream consumers