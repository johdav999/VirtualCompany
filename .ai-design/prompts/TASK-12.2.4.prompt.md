# Goal

Implement **TASK-12.2.4** for **ST-602 Audit trail and explainability views** so that all user-facing explanations in audit/explainability surfaces are **concise, operational, and safe**, explicitly **not exposing raw chain-of-thought**.

This task should establish a consistent backend and UI pattern for explanation content used in audit history and action detail views, aligned with the architecture note that rationale summaries are persisted as business data and with backlog guidance that raw reasoning must never be exposed.

# Scope

In scope:

- Define and enforce a **safe explanation contract** for audit/explainability views.
- Ensure explanation fields shown to users are limited to:
  - concise rationale summary
  - operational justification
  - relevant data sources / references
  - approvals / tool execution links where applicable
- Prevent any raw chain-of-thought or verbose internal reasoning from being returned by:
  - application queries
  - API responses
  - Blazor audit/explainability UI
- Add or update mapping/formatting logic so explanation text is normalized for display.
- Add tests covering:
  - safe explanation rendering
  - omission/redaction of raw reasoning fields if present internally
  - concise output behavior in audit detail/history responses

Out of scope unless required by existing code structure:

- Large redesign of the audit domain model
- New LLM prompting/orchestration behavior beyond what is needed to support safe summaries
- Full mobile implementation
- Broad refactor of unrelated audit logging or observability systems

# Files to touch

Inspect the solution first and then update the most relevant files in these areas.

Likely projects/modules:

- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Web`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

- Audit/explainability domain entities, value objects, or enums
- Application query/DTO/handler files for audit history and audit detail
- API endpoints/controllers for audit/explainability views
- Web pages/components for audit trail and action detail rendering
- Shared contracts/view models if audit DTOs are shared across API/Web
- Tests for API/application/UI-safe mapping behavior

If no audit/explainability implementation exists yet, create the minimal vertical slice needed for this task in the appropriate module boundaries.

# Implementation plan

1. **Discover current audit/explainability implementation**
   - Search for:
     - `audit`
     - `explain`
     - `rationale`
     - `tool_executions`
     - `approval`
     - `data sources`
   - Identify:
     - persisted explanation-related fields
     - DTOs returned to clients
     - any existing raw/internal reasoning fields
     - current audit history/detail UI

2. **Define a safe explanation model**
   - Introduce or standardize a user-facing explanation shape that is explicitly safe for display.
   - Prefer fields such as:
     - `Summary`
     - `WhyThisAction`
     - `Outcome`
     - `DataSources`
     - `ApprovalStatus` / linked approval references
   - Do **not** expose fields that imply hidden reasoning traces, scratchpads, internal deliberation, or chain-of-thought.
   - If internal records contain richer text, map them to a concise summary field only.

3. **Add explanation sanitization/normalization logic**
   - Implement a dedicated mapper/service in the application layer for converting stored audit/explainability data into safe display content.
   - Rules:
     - trim excessive verbosity
     - prefer short operational wording
     - remove or ignore internal reasoning fields
     - return fallback text when no safe summary exists, e.g. “Action completed using configured policy and available company data.”
   - Keep this deterministic and testable.

4. **Update audit history/detail queries**
   - Ensure audit list/detail query handlers return only safe explanation DTOs.
   - Include human-readable source references where available.
   - Include linked approvals/tool executions/affected entities as references, not raw internal payload dumps unless already intended and safe.
   - Preserve tenant scoping and role-based access behavior.

5. **Update API contracts**
   - Ensure API responses for audit history/detail expose only the safe explanation fields.
   - Remove any accidental serialization of internal/raw reasoning properties.
   - If needed, add explicit response DTOs rather than returning domain/infrastructure models directly.

6. **Update Blazor audit/explainability views**
   - Render explanation text as concise operational summaries.
   - Label clearly, e.g.:
     - “Summary”
     - “Why this happened”
     - “Data used”
   - Do not render hidden/debug/internal reasoning content even if present in backing models.
   - Keep the UI focused on trust/review/override use cases.

7. **Guard against future leakage**
   - Add comments/tests around the safe explanation mapper/DTOs stating that raw chain-of-thought must never be exposed.
   - Prefer explicit allow-list mapping over pass-through serialization.

8. **Add tests**
   - Unit tests for explanation mapping/sanitization:
     - internal verbose/raw reasoning present -> omitted from output
     - concise rationale summary preserved
     - missing summary -> safe fallback
   - API/integration tests:
     - audit history/detail responses do not contain raw reasoning fields
     - expected safe explanation fields are present
   - If practical, component/UI tests for rendering only safe explanation sections.

9. **Keep implementation aligned with architecture**
   - Business audit data remains separate from technical logs.
   - Explanations remain domain-facing summaries, not debug traces.
   - Respect tenant isolation and role-based access in all queries/endpoints.

# Validation steps

1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify audit/explainability responses:
   - Inspect audit history endpoint response
   - Inspect audit detail endpoint response
   - Confirm no raw/internal reasoning fields are serialized

4. Manually verify web UI:
   - Open audit trail/history view
   - Open action detail/explainability view
   - Confirm explanations are concise, operational, and source-linked where applicable
   - Confirm no chain-of-thought-like text appears

5. Regression check:
   - Ensure approvals/tool execution links still appear where expected
   - Ensure tenant-scoped access still works
   - Ensure role-restricted audit views remain restricted

# Risks and follow-ups

- **Risk: existing models may already store mixed-purpose explanation text**
  - Mitigation: introduce explicit safe display mapping and avoid direct entity serialization.

- **Risk: raw reasoning may be embedded in JSON payloads or debug fields**
  - Mitigation: use allow-list DTOs and add tests asserting absence of unsafe fields.

- **Risk: no current audit UI/API exists yet**
  - Mitigation: implement the smallest end-to-end slice necessary for this task without overbuilding unrelated ST-602 functionality.

- **Risk: concise formatting may become inconsistent across features**
  - Mitigation: centralize explanation formatting in one application-layer service/mapper.

Follow-ups after this task:
- Add explicit acceptance tests for ST-602 once broader audit views are complete.
- Standardize explanation generation upstream in orchestration/task completion flows so persisted summaries are safe by default.
- Consider adding admin-only internal diagnostics separately, with strict non-user-facing boundaries, if operational debugging later requires it.