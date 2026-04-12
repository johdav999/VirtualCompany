# Goal
Implement **TASK-12.1.4** for **ST-601 Executive cockpit dashboard** by adding dashboard empty states that guide initial setup when a company has **no agents, no workflows, and/or no knowledge documents** yet.

The result should make the executive cockpit useful for first-time tenants by:
- Detecting missing setup areas from tenant-scoped dashboard data
- Rendering clear, actionable empty-state UI in the web dashboard
- Guiding users toward the next setup actions for agents, workflows, and knowledge
- Preserving existing dashboard behavior when data exists

No explicit acceptance criteria were provided for the task, so align implementation to the story-level requirement:

> “Empty states guide setup when no agents, workflows, or knowledge exist yet.”

# Scope
In scope:
- Executive cockpit/dashboard empty-state UX in the **Blazor Web App**
- Tenant-scoped detection of whether the current company has:
  - any agents
  - any workflows/workflow definitions or instances, depending on current dashboard model
  - any knowledge/documents
- Conditional rendering for:
  - fully empty dashboard state
  - partial empty states for missing setup areas
- Reuse existing navigation/routes where possible for setup CTAs
- Minimal query/model updates needed to support empty-state detection
- Tests for the empty-state logic and/or rendered output where practical

Out of scope:
- Building full onboarding flows for agents, workflows, or knowledge from scratch
- New backend modules or schema changes unless absolutely required
- Mobile companion changes
- Large dashboard redesign unrelated to empty states
- New analytics/KPI calculations beyond what is needed to determine emptiness

# Files to touch
Inspect the solution first and then update the actual files that own dashboard queries and rendering. Likely areas include:

- `src/VirtualCompany.Web/**`
  - Executive cockpit/dashboard page/component
  - Shared dashboard widgets/components
  - Navigation/link helpers if needed
  - Any view models used by the dashboard
- `src/VirtualCompany.Application/**`
  - Dashboard query/handler
  - DTO/view model returned to the web app
- `src/VirtualCompany.Infrastructure/**`
  - Query implementation/repositories if dashboard data is assembled there
- `src/VirtualCompany.Domain/**`
  - Only if a shared dashboard summary model exists and must be extended
- `tests/VirtualCompany.Api.Tests/**` and/or other relevant test projects
  - Add tests for tenant-scoped empty-state detection and response shape
- `README.md` only if there is a documented dashboard behavior section that should mention empty states

Before editing, locate:
- the executive cockpit route/page
- the dashboard query/endpoint powering it
- existing models for approvals, alerts, KPI cards, and recent activity
- existing routes/pages for:
  - agent roster/hiring/setup
  - workflow definitions/setup
  - knowledge/document upload

# Implementation plan
1. **Discover current dashboard flow**
   - Find the executive cockpit page/component in `src/VirtualCompany.Web`.
   - Trace how it gets data:
     - direct application service
     - API endpoint
     - MediatR/CQRS query
     - server-side component model
   - Identify the current dashboard DTO/view model and where tenant scoping is enforced.

2. **Add explicit empty-state signals to the dashboard model**
   - Extend the dashboard response/view model with simple booleans and counts, preferring explicit fields over UI inference. Example shape:
     - `HasAgents`
     - `HasWorkflows`
     - `HasKnowledge`
     - optional counts like `AgentCount`, `WorkflowCount`, `KnowledgeDocumentCount`
     - optional convenience flag `IsInitialSetupEmpty`
   - Keep naming consistent with existing conventions in the codebase.

3. **Populate empty-state data in a tenant-scoped query**
   - Update the dashboard query/handler/repository to compute the above values for the current company only.
   - Use efficient existence/count queries rather than loading full collections.
   - Prefer `Any(...)`/lightweight aggregate queries over expensive joins.
   - For workflows, use the source that best matches “setup exists” in the current architecture:
     - likely `workflow_definitions` if setup means configured workflows
     - or `workflow_instances` if the dashboard currently centers on active workflow usage
   - For knowledge, use `knowledge_documents` as the primary signal.
   - For agents, use `agents` excluding archived if that matches current UX expectations; otherwise follow current roster semantics consistently.

4. **Design the empty-state UX**
   - Implement a clear hierarchy:
     - **Full empty state** when all three are missing
     - **Section-level empty prompts** when only one or two setup areas are missing
   - Suggested behavior:
     - Full empty state at top of dashboard with concise explanation and 2–3 CTAs
     - Inline cards/banners for missing sections while still showing available dashboard data
   - Keep copy concise, executive-friendly, and action-oriented.
   - Example CTA intents:
     - “Hire your first agent”
     - “Set up a workflow”
     - “Upload company knowledge”
   - Reuse existing design system/components/styles if present.

5. **Wire CTAs to existing routes**
   - Link to existing pages only; do not invent dead-end routes.
   - If setup pages exist:
     - agents → roster/hiring/create page
     - workflows → workflow list/template/setup page
     - knowledge → document upload/library page
   - If a direct create page does not exist, link to the nearest relevant landing page.

6. **Render conditional dashboard states**
   - Update the dashboard page/component to:
     - show the full empty state when all setup areas are absent
     - suppress irrelevant widgets if they would be blank/noisy
     - show partial empty-state guidance alongside existing widgets when some data exists
   - Ensure the dashboard still renders approvals/alerts/activity if those exist, even if one setup area is missing.
   - Avoid showing contradictory UI such as empty KPI cards with no explanation.

7. **Handle edge cases**
   - Tenant has agents but no workflows or knowledge
   - Tenant has knowledge but no agents
   - Tenant has workflows configured but no activity yet
   - Tenant has approvals/alerts but setup counts are sparse
   - Null/empty collections from existing query models
   - Unauthorized or cross-tenant access must remain unchanged

8. **Add tests**
   - Add application/API tests for dashboard summary flags:
     - no agents/workflows/knowledge → all false / initial empty true
     - only agents exist
     - only workflows exist
     - only knowledge exists
     - mixed states
     - tenant isolation: one company’s data must not affect another’s dashboard flags
   - Add UI/component tests if the project already uses them; otherwise keep tests at query/response level.
   - If there are snapshot or rendering tests for Blazor components, add coverage for:
     - full empty state
     - partial empty state

9. **Keep implementation minimal and consistent**
   - Do not introduce a new dashboard subsystem.
   - Do not duplicate query logic across web and API layers.
   - Keep empty-state copy centralized if the project already centralizes UI strings/constants.
   - Follow existing naming, folder structure, and CQRS-lite patterns.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app for at least these tenant scenarios:
   - brand-new company with no agents, no workflows, no knowledge
   - company with agents only
   - company with knowledge only
   - company with workflows only
   - company with all three configured

4. Confirm dashboard behavior:
   - full empty state appears only when all three setup areas are absent
   - partial guidance appears when only some setup areas are missing
   - CTAs navigate to valid existing pages
   - existing widgets still render when relevant data exists
   - no cross-tenant leakage in counts/flags

5. If there is an API or server-side dashboard response:
   - inspect returned model and verify the new flags/counts are correct per tenant

# Risks and follow-ups
- **Route uncertainty:** The exact setup pages for agents, workflows, and knowledge may not exist or may use different route names. Verify before wiring CTAs.
- **Workflow signal ambiguity:** “No workflows” could mean no definitions, no instances, or no active workflows. Match the current dashboard/domain semantics and document the choice in code comments if needed.
- **Archived/inactive records:** Decide whether archived agents or inactive workflow definitions count as “exists” for setup guidance. Be consistent with current product behavior.
- **UI clutter risk:** Partial empty states can overwhelm the dashboard if too many banners/cards are shown. Prefer one concise setup panel over multiple noisy alerts if needed.
- **Performance risk:** Avoid expensive aggregate queries on every dashboard load; use lightweight existence checks and existing caching patterns if already present.
- **Test coverage gap:** If Blazor component testing is not established, prioritize application/query tests and keep UI logic simple.
- **Follow-up opportunity:** Consider a later enhancement to personalize setup guidance by company onboarding stage, role, or industry template.