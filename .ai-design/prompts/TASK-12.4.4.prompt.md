# Goal

Implement backlog task **TASK-12.4.4** for **ST-604 Mobile companion for approvals, alerts, and quick chat** by making the **mobile scope limitation explicit in the product and codebase**, so the MAUI app is clearly positioned as a **companion experience** rather than a full-featured admin/workflow client.

This task should ensure the system communicates and enforces that mobile supports:
- sign-in
- company selection
- alerts
- approvals
- daily briefing
- direct agent chat
- quick company status / task follow-up summaries

And does **not** imply or expose full parity for:
- company setup/onboarding
- agent hiring/configuration
- workflow definition/administration
- deep analytics/cockpit administration
- broad system management features

The implementation should favor **clarity, guardrails, and UX consistency** over adding new backend business logic.

# Scope

In scope:
- Review current mobile app structure and existing navigation/routes/pages.
- Update mobile app IA/navigation so only companion features are surfaced.
- Remove, hide, or explicitly defer any mobile entry points that suggest full admin/workflow parity.
- Add lightweight product copy / empty states / labels where needed to communicate “mobile companion” scope.
- Ensure any shared DTO/viewmodel usage in mobile only requests and renders the focused companion scenarios.
- If needed, add a small capability/config abstraction in mobile to centralize supported mobile features.
- Keep backend reuse intact; do not introduce mobile-specific business rules in the API.
- Add or update tests covering navigation/menu visibility and any scope-limiting logic.

Out of scope:
- Building missing full mobile features beyond the companion scope.
- Adding mobile-specific backend endpoints unless absolutely required for existing companion flows.
- Implementing full workflow management, admin configuration, or dashboard parity on mobile.
- Major redesign of web or backend modules.
- Push notification infrastructure unless already partially present and directly impacted.

# Files to touch

Start by inspecting these likely areas and adjust based on actual repository structure:

- `src/VirtualCompany.Mobile/**`
  - App shell / navigation
  - Pages for alerts, approvals, briefing, chat, status
  - Menu/tab definitions
  - ViewModels and service clients
  - Any feature flags or capability configuration
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/contracts only if mobile needs slimmer/focused models already supported by backend
- `src/VirtualCompany.Api/**`
  - Only if a minimal non-business-logic adjustment is needed to support concise mobile payloads or existing companion endpoints
- `src/VirtualCompany.Web/**`
  - Only if shared copy/constants/components are used across web/mobile and need alignment
- `README.md`
  - If product surface documentation mentions mobile, update wording to reflect companion scope
- Tests:
  - `tests/VirtualCompany.Api.Tests/**` if API contract behavior changes
  - Any mobile/unit/UI test project if present in solution or adjacent folders

Do not broaden changes beyond what is necessary for this task.

# Implementation plan

1. **Inspect current mobile surface area**
   - Review `src/VirtualCompany.Mobile` for:
     - Shell/tab bar/flyout items
     - route registration
     - landing/home/dashboard pages
     - approval, alert, briefing, chat, task summary pages
     - any admin/setup/workflow pages already scaffolded
   - Identify any mobile screens or menu items that imply parity with web-first features.

2. **Define the intended mobile companion feature set in code**
   - Introduce a simple centralized definition for supported mobile capabilities if one does not exist, for example:
     - `Alerts`
     - `Approvals`
     - `DailyBriefing`
     - `DirectAgentChat`
     - `CompanyStatus`
     - `TaskFollowUp`
   - Use this to drive navigation visibility and prevent accidental exposure of unsupported areas.
   - Keep this app-side only unless a shared contract already exists and is appropriate.

3. **Constrain mobile navigation**
   - Update the MAUI shell/tab/flyout so the primary mobile destinations are limited to the companion scenarios.
   - Remove or hide routes/menu items for:
     - admin/configuration
     - workflow builder/management
     - full analytics/cockpit parity
     - agent management/hiring
     - company setup
   - If unsupported routes already exist and cannot be removed safely, redirect them to a simple “Available on web” screen.

4. **Add explicit UX messaging**
   - Add concise copy in relevant mobile screens/settings/help text to reinforce:
     - mobile is for quick action and follow-up
     - advanced administration and workflow management remain web-first
   - Examples:
     - “For full setup and administration, use the web app.”
     - “Mobile is optimized for approvals, alerts, and quick chat.”
   - Keep wording short and product-facing.

5. **Preserve backend reuse**
   - Verify mobile uses existing backend APIs for approvals, alerts, briefing, chat, and status.
   - Do not add mobile-only business logic to the API.
   - If payloads are too heavy, prefer:
     - existing query parameters
     - projection/query DTO refinement
     - non-breaking response shaping
   - Any API changes must remain generic and reusable by web.

6. **Tighten page-level behavior**
   - For any existing mobile pages that expose unsupported actions:
     - disable action buttons
     - hide edit/admin controls
     - replace with read-only summaries where appropriate
   - Ensure approval actions still fully work and update the same backend state as web.
   - Ensure direct chat remains focused on quick interactions, not full orchestration administration.

7. **Document the scope boundary**
   - Update any relevant README or in-app documentation/comments to reflect:
     - web-first architecture
     - mobile companion intent
     - no full admin/workflow parity on mobile
   - Keep documentation aligned with the architecture and backlog notes.

8. **Add tests**
   - Add/update tests for:
     - mobile navigation only showing supported companion destinations
     - unsupported/admin routes being absent, blocked, or redirected
     - any capability gating helper logic
   - If there are API changes, add contract/regression tests to ensure no business behavior changed.

9. **Keep implementation minimal and safe**
   - Prefer small, targeted changes.
   - Avoid speculative abstractions unless they clearly reduce future accidental parity creep.
   - Do not refactor unrelated mobile architecture.

# Validation steps

Run and verify at minimum:

1. Build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual validation in mobile project:
   - Launch or inspect `src/VirtualCompany.Mobile`
   - Confirm visible mobile destinations are limited to:
     - alerts
     - approvals
     - daily briefing
     - direct agent chat
     - quick company status / task follow-up
   - Confirm there are no obvious entry points for:
     - company setup
     - agent admin/configuration
     - workflow definition/management
     - full cockpit/admin parity

4. Approval flow validation:
   - Confirm approval actions from mobile still call the same backend approval endpoints/state transitions as web.
   - Confirm no mobile-specific approval business logic was introduced.

5. UX copy validation:
   - Confirm at least one clear user-facing indication that advanced administration/workflow features are web-first.

6. Regression check:
   - Ensure existing mobile companion flows still compile and function after navigation restriction changes.

# Risks and follow-ups

- **Risk: accidental hidden dependency on removed routes**
  - Some pages/viewmodels may assume admin/workflow routes exist.
  - Mitigation: prefer route redirection or graceful “Use web app” placeholders before deleting code.

- **Risk: over-implementing backend changes**
  - This task is about scope limitation, not expanding mobile capability.
  - Mitigation: keep API changes minimal and generic.

- **Risk: unclear product messaging**
  - If copy is too subtle, users may still expect parity.
  - Mitigation: add explicit but concise messaging in navigation/help/empty states.

- **Risk: future parity creep**
  - New mobile pages may be added ad hoc later.
  - Mitigation: centralize supported mobile capabilities and gate navigation from one place.

Follow-ups to note in comments or implementation summary if relevant:
- Consider a dedicated “Available on web” reusable page/component for unsupported advanced features.
- Consider adding mobile-focused API projections for concise payloads if current responses are too heavy.
- Consider documenting mobile companion boundaries in product docs/architecture notes more broadly if not already present.