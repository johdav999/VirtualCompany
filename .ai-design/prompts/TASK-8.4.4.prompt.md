# Goal
Implement `TASK-8.4.4` for `ST-204 Agent roster and profile views` so that restricted agent profile fields and actions are hidden or disabled based on the current human user’s role.

This should fit the existing multi-tenant, ASP.NET Core + Blazor Web App architecture and use policy-based authorization patterns already established or implied by the solution.

# Scope
Focus only on role-based visibility/editability for the agent roster and agent profile experience.

Include:
- Determining the current user’s company membership role in the active tenant.
- Defining clear UI authorization rules for agent roster/profile fields and actions.
- Applying those rules in Blazor roster/detail pages and any backing query/view-model layer as needed.
- Ensuring restricted fields/actions are either:
  - hidden when they should not be discoverable, or
  - shown disabled/read-only when visibility is acceptable but modification is not.
- Preventing unauthorized server-side updates even if a user bypasses the UI.

Do not include:
- Broad redesign of the auth system.
- New identity provider work.
- Full permissions matrix for the whole app.
- Mobile app changes unless shared contracts force a small update.
- Unrelated profile analytics/workload logic beyond what is needed to preserve current behavior.

Use pragmatic role handling for the roles already called out in backlog/context:
- owner
- admin
- manager
- employee
- finance approver
- support supervisor

If the codebase already has a different role representation, align to it rather than inventing a parallel model.

# Files to touch
Inspect the solution first and then update the smallest correct set of files. Likely areas:

- `src/VirtualCompany.Web/...`
  - Agent roster page/component
  - Agent profile/detail page/component
  - Shared authorization-aware UI helpers/components if present
- `src/VirtualCompany.Application/...`
  - Agent profile query DTO/view model
  - Commands/handlers for agent updates
  - Authorization service or role capability mapping for agent management
- `src/VirtualCompany.Api/...`
  - Endpoint authorization or defensive checks if profile updates are exposed via API
- `src/VirtualCompany.Domain/...`
  - Role/capability enums/constants/value objects only if a domain-level concept already exists
- `src/VirtualCompany.Shared/...`
  - Shared contracts only if needed by both API and Web
- Tests in the relevant test projects
  - authorization/unit tests
  - application handler tests
  - UI/component tests if the repo already uses them

Do not touch unrelated modules unless required for compilation or to enforce server-side authorization.

# Implementation plan
1. **Discover existing auth and agent management patterns**
   - Find how tenant membership roles are currently represented and resolved.
   - Find existing authorization policies, claims mapping, or helper services.
   - Find the roster/profile pages and the update flows for agent profile actions.
   - Identify which fields/actions are currently editable/viewable.

2. **Define a minimal role-to-capability matrix for agent roster/profile**
   Implement a single source of truth for what the current human role can do in this story. Prefer capabilities over scattered `if role == ...` checks.

   Suggested baseline matrix unless the codebase already implies a better one:
   - **Owner/Admin**
     - view all profile fields
     - edit identity/configuration fields
     - change status
     - edit objectives/KPIs/role brief
     - edit permissions/scopes/thresholds/escalation rules/autonomy
   - **Manager**
     - view most profile fields
     - may edit non-sensitive operational fields only if already supported
     - cannot edit autonomy, tool permissions, data scopes, approval thresholds, escalation rules
     - cannot archive/restrict unless existing rules explicitly allow
   - **Employee**
     - view basic identity/role/status/high-level objectives
     - cannot view or edit sensitive governance fields
     - no management actions
   - **Finance Approver / Support Supervisor**
     - treat as specialized non-admin roles unless existing product rules indicate elevated access
     - can view basic profile info
     - sensitive governance fields hidden or read-only unless explicitly authorized by existing patterns

   Sensitive governance fields for this task should include at minimum:
   - autonomy level editing
   - tool permissions
   - data scopes
   - approval thresholds
   - escalation rules
   - trigger logic
   - working hours if considered operationally sensitive in current UX
   - status-changing actions like restrict/archive if applicable

3. **Create or extend an authorization/capability service**
   Add a focused service or helper such as:
   - `IAgentProfileAuthorizationService`
   - or a capability object returned from application layer, e.g. `AgentProfileAccessCapabilities`

   It should answer questions like:
   - CanViewSensitiveGovernanceFields
   - CanEditAgentProfile
   - CanEditObjectives
   - CanEditPolicies
   - CanChangeStatus
   - CanViewRecentActivity
   - CanStartChat / CanAssignTask if those actions exist on the page

   Prefer deriving these once per request/page model rather than repeating role checks throughout Razor markup.

4. **Apply authorization in the application/query layer**
   Update the agent detail query/view model so the UI can render safely and simply.
   Recommended pattern:
   - return the agent data needed for the page
   - include capability flags for the current user
   - optionally omit/redact sensitive fields entirely when the user cannot view them

   Prefer server-side omission/redaction for truly restricted fields instead of sending everything to the UI and hiding it there.

5. **Update roster UI**
   On the roster page:
   - keep the accepted visible columns from ST-204
   - ensure any restricted actions/buttons are hidden or disabled based on capabilities
   - if there are row actions like edit, pause, restrict, archive, configure, etc., gate them appropriately
   - avoid exposing links to edit/governance screens for unauthorized roles

6. **Update profile/detail UI**
   On the agent profile page:
   - show basic identity/responsibility information to allowed roles
   - hide or render read-only sensitive sections based on capabilities
   - disable edit controls and action buttons where the user can view but not modify
   - if a section is hidden, do not leave empty shells that imply missing data due to load failure
   - optionally show a concise “You do not have permission to view/edit this section” message only where it improves UX

   Good pattern:
   - basic sections visible broadly: name, role, department, status, high-level objectives
   - governance sections restricted: autonomy, permissions, scopes, thresholds, escalation rules

7. **Enforce server-side authorization on mutations**
   For every command/API that updates agent profile fields or status:
   - validate the current user’s tenant membership and role
   - reject unauthorized changes with the project’s standard forbidden/validation behavior
   - do not rely on disabled UI controls as protection

   If updates are currently handled by a single broad command, add field-level authorization checks so a manager cannot submit sensitive fields by crafting a request.

8. **Add tests**
   Add focused tests for:
   - capability mapping by role
   - query redaction/visibility behavior for sensitive fields
   - command rejection for unauthorized edits
   - any existing component/page tests for hidden/disabled actions if the repo supports them

   Minimum scenarios:
   - owner/admin can view and edit sensitive governance fields
   - manager can view profile but cannot edit governance fields
   - employee cannot see sensitive governance fields/actions
   - unauthorized status change is rejected server-side

9. **Keep implementation cohesive and minimal**
   - Reuse existing policy/role infrastructure.
   - Avoid introducing a large generic permissions framework unless one already exists.
   - Keep naming explicit and local to agent roster/profile authorization.

# Validation steps
1. Inspect current implementation:
   - locate roster/profile pages
   - locate agent update commands/endpoints
   - locate membership role resolution and authorization helpers

2. Build after changes:
   - `dotnet build`

3. Run tests:
   - `dotnet test`

4. Manually verify in web app with representative roles in the same tenant:
   - owner/admin
   - manager
   - employee
   - one specialized role if available

5. Manual UI checks:
   - roster loads and filters still work
   - unauthorized action buttons are hidden or disabled
   - profile page shows basic info correctly
   - sensitive sections are hidden or read-only per role
   - no broken layout from omitted sections

6. Manual security checks:
   - attempt unauthorized edit through UI
   - attempt unauthorized edit by calling the backing endpoint/command payload directly if practical
   - confirm server rejects the change and data remains unchanged

7. Regression checks:
   - authorized admin can still update agent profile successfully
   - status changes still work for authorized roles
   - tenant scoping remains intact

# Risks and follow-ups
- **Risk: scattered role checks**
  - If role logic is duplicated across pages/handlers, consolidate into one capability service to avoid drift.

- **Risk: UI-only protection**
  - Hiding controls without server-side enforcement is insufficient. Ensure commands/endpoints validate role permissions.

- **Risk: overexposing sensitive data in DTOs**
  - If the application layer returns all fields to the client, hidden UI alone is not enough. Prefer redaction/omission for restricted fields.

- **Risk: unclear product rules for specialized roles**
  - Finance approver/support supervisor permissions may need later refinement. For now, keep them conservative unless existing code or docs specify otherwise.

- **Risk: broad edit command**
  - A single update command may allow unauthorized field changes unless field-level checks are added.

Follow-ups to note in code comments or task notes if needed:
- Consider evolving from role-based checks to finer-grained permission claims later.
- Consider a reusable authorization display pattern for other admin/governance screens.
- If not already present, document the agent-profile role capability matrix for future stories.