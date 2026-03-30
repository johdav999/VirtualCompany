# Goal
Implement backlog task **TASK-7.2.4** for **ST-102 Company workspace creation and onboarding wizard** so that **when a user successfully completes onboarding, they are redirected into the web dashboard and shown starter guidance**.

This work should fit the existing **.NET modular monolith** and **Blazor Web App** architecture, preserve **tenant-aware company context**, and align with the story requirement:

> Successful completion lands the user in the web dashboard with starter guidance.

Treat this as a focused vertical slice across the onboarding completion flow and dashboard entry experience, not a redesign of the full onboarding system.

# Scope
In scope:
- Identify the current onboarding completion path for company/workspace setup in the **Blazor web app**.
- Update the completion behavior so a successful final onboarding action:
  - marks onboarding/setup as completed if such state exists,
  - resolves the active company/workspace context,
  - redirects the user to the main web dashboard route for that company context.
- Add a **starter guidance** experience on the dashboard for newly onboarded users.
- Ensure the starter guidance is shown only in appropriate cases, ideally for first-entry / incomplete-product-state scenarios, and does not permanently clutter the dashboard.
- Keep implementation tenant-safe and consistent with existing app patterns.

Out of scope unless required by existing code structure:
- Rebuilding the full onboarding wizard.
- Implementing unrelated dashboard widgets or analytics.
- Adding mobile behavior.
- Introducing new backend modules or broad schema redesigns.
- Creating a full notification/tutorial engine if a lightweight dashboard guidance panel/banner/checklist will satisfy the task.

If the codebase already has onboarding progress persistence, reuse it. If not, implement the minimum necessary state handling to support correct redirect and starter guidance behavior without overengineering.

# Files to touch
Inspect and update only the files needed after confirming actual code structure. Likely areas include:

- `src/VirtualCompany.Web/...`
  - Onboarding wizard pages/components
  - Company creation/setup pages
  - Dashboard page/component
  - Shared layout/navigation components if company context or redirect handling lives there
  - View models / UI services used by onboarding and dashboard
- `src/VirtualCompany.Application/...`
  - Commands/handlers for completing onboarding or updating company setup state
  - Queries used by dashboard starter guidance
- `src/VirtualCompany.Domain/...`
  - Company/setup/onboarding state entities or value objects, if needed
- `src/VirtualCompany.Infrastructure/...`
  - Persistence mappings/repositories for onboarding completion state, if needed
- Tests in corresponding test projects if present

Before editing, locate:
- the onboarding completion submit handler,
- the dashboard route/component,
- any existing company setup status fields,
- any existing empty-state or starter-guidance UI patterns.

# Implementation plan
1. **Trace the current onboarding completion flow**
   - Find the final step of the company workspace creation/onboarding wizard in `VirtualCompany.Web`.
   - Determine how the wizard currently saves progress and what happens on successful completion.
   - Identify how the active company/tenant context is resolved after setup.

2. **Define the completion contract**
   - Reuse existing setup state if available, such as:
     - onboarding status,
     - setup completed timestamp,
     - wizard progress state,
     - company settings JSON.
   - If no explicit completion marker exists, add the smallest sensible mechanism to distinguish:
     - in-progress onboarding,
     - completed onboarding.
   - Keep naming aligned with current domain conventions.

3. **Implement successful completion redirect**
   - Update the final onboarding action so that after successful persistence it redirects to the web dashboard.
   - Use the app’s existing routing and tenant/company context conventions.
   - Avoid redirecting before the final save succeeds.
   - Ensure refresh/retry behavior is safe and does not create duplicate company/setup records.

4. **Add starter guidance on dashboard**
   - Implement a lightweight starter guidance experience on the dashboard, such as:
     - welcome banner,
     - setup checklist,
     - “next recommended steps” card,
     - empty-state guidance when no agents/workflows/knowledge exist.
   - Guidance should be relevant to a newly created company workspace and may include actions like:
     - hire your first agent,
     - invite teammates,
     - upload company knowledge,
     - connect integrations.
   - Prefer using existing dashboard card/empty-state components if available.

5. **Gate starter guidance appropriately**
   - Show starter guidance when the user has just completed onboarding and/or when the company is still in an initial setup state.
   - If possible, avoid showing it indefinitely once the workspace is clearly beyond initial setup.
   - If the codebase supports dismissible UI state, use it; otherwise use a simple rule based on company setup state and empty dashboard conditions.
   - Keep logic deterministic and tenant-scoped.

6. **Preserve authorization and tenant isolation**
   - Ensure redirect and dashboard query logic operate only within the current user’s authorized company membership.
   - Do not expose starter guidance or dashboard data across tenants.
   - Reuse existing policy/auth patterns.

7. **Keep UX coherent**
   - Ensure the post-completion transition feels intentional:
     - success state,
     - redirect,
     - visible welcome/starter guidance on arrival.
   - Avoid dead-end “setup complete” pages if the requirement is to land in the dashboard.

8. **Add or update tests**
   - Add focused tests for:
     - onboarding completion command/handler behavior if applicable,
     - redirect behavior from final onboarding step,
     - dashboard starter guidance visibility conditions.
   - Prefer existing test patterns in the repo.

9. **Document assumptions in code comments or PR notes**
   - If no explicit acceptance criteria exist beyond the story line, keep implementation minimal and explain any assumptions:
     - what counts as onboarding completion,
     - when starter guidance appears,
     - when it stops appearing.

# Validation steps
Run the relevant validation for the touched projects and confirm behavior manually.

Build/test:
- `dotnet build`
- `dotnet test`

Manual verification:
1. Sign in as a user able to create a company workspace.
2. Complete the onboarding wizard through the final step.
3. Confirm the final submit succeeds and the user is redirected to the **web dashboard** rather than remaining in the wizard or landing on a generic success page.
4. Confirm the dashboard displays **starter guidance** appropriate for a newly created workspace.
5. Refresh the dashboard and verify behavior is stable and does not re-run onboarding unexpectedly.
6. If guidance is conditionally shown, verify it appears for a new workspace and is hidden or reduced when the workspace is no longer in an initial state.
7. Verify company context is correct and no cross-tenant data appears.
8. If there is resume-progress behavior in the wizard, verify completion no longer routes the user back into onboarding.

# Risks and follow-ups
Risks:
- The current codebase may not yet have explicit onboarding completion state, requiring a minimal domain/persistence addition.
- Dashboard and onboarding may use different company-context resolution paths, which can cause redirect bugs if not unified.
- If starter guidance is tied only to “first visit” UI state without persisted setup state, behavior may be inconsistent across browsers/devices.
- If the dashboard is still early-stage, adding guidance may require creating a small reusable empty-state/welcome component.

Follow-ups:
- Consider a richer onboarding checklist tied to actual setup milestones:
  - first agent hired,
  - first teammate invited,
  - first document uploaded,
  - first integration connected.
- Consider persisting dismissible starter guidance per user/company.
- Consider adding analytics/audit events for onboarding completion and dashboard first-visit.
- If not already present, align this with broader **ST-102** acceptance criteria around persisted wizard progress and resume behavior.