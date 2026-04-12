# Goal
Implement backlog task **TASK-10.3.6 — Approval UX should show concise rationale and affected data** for story **ST-403 Approval requests and decision chains**.

The coding agent should enhance the approval experience so approvers can quickly understand:
- **why** the approval is being requested, via a short rationale summary
- **what data or entities are affected**, via a concise affected-data section

This should align with the architecture’s approval, task/workflow, and audit/explainability modules, while keeping explanations operational and concise rather than exposing raw reasoning.

# Scope
In scope:
- Add or complete backend query/view-model support for approval detail/list surfaces to include:
  - concise rationale summary
  - affected data/entities summary
  - threshold context if already available and useful to display
- Update web approval UX to render this information clearly in approval list/detail views
- Ensure tenant scoping and authorization remain enforced
- Reuse existing domain data where possible from approvals, tasks, workflows, tool executions, and audit/explainability records
- If missing, add a minimal application-layer composition step that derives a user-facing affected-data summary from linked entities

Out of scope:
- Redesigning the full approval workflow engine
- Adding new approval decision semantics
- Exposing chain-of-thought or verbose internal model reasoning
- Building full mobile parity unless the same shared DTOs require harmless updates
- Large schema redesigns unless absolutely necessary for this task

Assumptions to validate in the codebase:
- There is already an approval list/detail page or inbox surface in `VirtualCompany.Web`
- Approval entities and queries already exist in application/infrastructure layers
- Some rationale/threshold context may already be stored on tasks, approvals, audit events, or tool executions

# Files to touch
Inspect first, then update only the necessary files. Likely areas:

- `src/VirtualCompany.Web/**`
  - Approval inbox/list/detail Razor components/pages
  - Shared approval view components
  - Any approval-specific DTO consumption or rendering logic

- `src/VirtualCompany.Application/**`
  - Approval queries/handlers
  - Approval DTOs/view models
  - Mapping/composition logic for rationale and affected data summaries

- `src/VirtualCompany.Domain/**`
  - Only if a small domain addition is required for a stable summary representation

- `src/VirtualCompany.Infrastructure/**`
  - Query implementations / EF projections / repository methods
  - Data access for linked task/workflow/action/tool execution/audit records

- `src/VirtualCompany.Shared/**`
  - Shared contracts if approval DTOs are shared across web/mobile/API boundaries

- `src/VirtualCompany.Api/**`
  - Approval endpoints if response contracts need to be expanded

- `tests/**`
  - Application query tests
  - API tests for approval payloads
  - UI/component tests if present in the solution

Also review:
- `README.md`
- any architecture or conventions docs in the repo
- existing migrations only if a persistence change is unavoidable

# Implementation plan
1. **Discover the current approval flow**
   - Locate approval domain model, API endpoints, application queries, and web pages/components.
   - Identify where approval list/detail data is assembled today.
   - Confirm what fields already exist for:
     - `decision_summary`
     - `threshold_context_json`
     - linked `entity_type` / `entity_id`
     - task `rationale_summary`
     - audit event rationale/data source fields
     - tool execution request/response metadata

2. **Define the UX data contract**
   - Extend the approval read model/DTO used by the web UI with fields such as:
     - `RationaleSummary`
     - `AffectedDataSummary`
     - optional structured `AffectedEntities` collection if the UI already supports chips/lists
   - Keep the contract concise and presentation-friendly.
   - Prefer additive changes to avoid breaking existing consumers.

3. **Compose concise rationale**
   - Populate rationale using the best available source in this order, unless the codebase suggests a better existing pattern:
     1. approval-specific rationale/summary if present
     2. linked task `rationale_summary`
     3. linked audit/explainability summary
     4. safe fallback text like “This action exceeded a configured approval threshold.”
   - Ensure the result is short, human-readable, and non-sensitive.

4. **Compose affected data summary**
   - Derive a concise summary of impacted entities/data from linked records.
   - Examples of acceptable outputs:
     - “Task: Vendor payment run for April”
     - “Workflow: Customer refund escalation”
     - “Action affects 3 customer records and 1 invoice”
     - “Agent proposes updating CRM opportunity stage for 2 deals”
   - Prefer deterministic server-side composition from known entity metadata rather than free-form generation.
   - If structured data exists in `threshold_context_json`, tool execution payloads, or audit metadata, use it.
   - If only linked entity identity is available, show a minimal but useful summary rather than inventing detail.

5. **Update approval queries/endpoints**
   - Modify the application query handler(s) and API endpoint(s) that feed approval inbox/detail screens.
   - Ensure projections fetch enough linked data efficiently.
   - Avoid N+1 query patterns; batch or join where practical.

6. **Update the web UX**
   - In approval list items/cards:
     - show a short rationale line
     - show a compact affected-data line or badge group
   - In approval detail view:
     - show rationale in a dedicated section
     - show affected data/entities in a dedicated section
     - preserve existing approve/reject actions and comments
   - Keep the UI concise and scannable for executive/operator workflows.

7. **Handle empty and fallback states**
   - If rationale is unavailable, show a safe fallback message.
   - If affected data is unavailable, show a neutral fallback like “Affected data details unavailable.”
   - Do not block approval actions solely because summary metadata is incomplete.

8. **Preserve security and tenancy**
   - Ensure all approval-related reads remain company-scoped.
   - Do not expose linked entity details the current user is not authorized to view.
   - If necessary, degrade to a generic affected-data summary when access is restricted.

9. **Add tests**
   - Cover application-layer composition logic for rationale and affected data.
   - Cover API contract changes if endpoints are involved.
   - Add UI/component coverage if the repo already uses a test pattern for Blazor components.

10. **Keep implementation minimal and aligned**
   - Favor query/view-model composition over schema changes.
   - Only add persistence fields if there is no reliable way to derive the required UX data from existing records.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify approval UX in the web app:
   - Open approval inbox/list
   - Confirm each approval shows:
     - concise rationale
     - affected data summary
   - Open approval detail
   - Confirm rationale and affected data sections render correctly
   - Confirm approve/reject actions still work

4. Validate fallback behavior:
   - Approval linked to a task with rationale summary
   - Approval with only threshold context
   - Approval with minimal linked metadata
   - Confirm the UI still shows useful concise text in each case

5. Validate security/tenant behavior:
   - Confirm approval data is scoped to the active company
   - Confirm restricted linked details are not overexposed

6. If API contracts changed, verify serialized responses include the new fields and existing consumers still function.

# Risks and follow-ups
- **Risk: missing source data**
  - Existing approvals may not store enough metadata to produce rich affected-data summaries.
  - Mitigation: implement deterministic fallbacks and avoid blocking the UX.

- **Risk: overexposing sensitive details**
  - Linked task/tool/audit data may contain more detail than approvers should see.
  - Mitigation: summarize conservatively and respect authorization boundaries.

- **Risk: N+1 query regressions**
  - Pulling linked task/workflow/action data per approval could hurt inbox performance.
  - Mitigation: use efficient projections and batched lookups.

- **Risk: inconsistent summaries across approval types**
  - Tasks, workflows, and actions may each have different metadata quality.
  - Mitigation: centralize summary composition logic in the application layer.

Follow-ups to note in code comments or task notes if not completed now:
- Standardize approval explanation payloads at creation time so rationale/affected data are first-class and consistent
- Reuse the same summary model in mobile approval surfaces
- Consider linking affected-data summaries with audit/explainability views for drill-down
- Add explicit acceptance tests for approval inbox/detail rendering once the product UX stabilizes