# Goal
Implement backlog task **TASK-8.4.7 — Keep profile page as the anchor for future analytics** for story **ST-204 Agent roster and profile views**.

The coding agent should update the agent profile experience so that the **profile page remains the stable, extensible entry point for future analytics**, without prematurely building full analytics features. The implementation should make the profile page structurally ready for later KPI/health/activity analytics by establishing clear UI sections, route stability, query/view-model seams, and placeholder/extensibility points.

# Scope
In scope:

- Review the current implementation of **agent roster and agent profile views** in the Blazor web app.
- Ensure the **agent detail/profile page** is the canonical destination from the roster and any related navigation.
- Refine the profile page layout so it can naturally host future analytics sections such as:
  - workload/health summary
  - recent activity
  - KPI/metrics panels
  - trend/analytics modules later
- Add lightweight, non-invasive placeholders or section containers if needed, but **do not invent full analytics functionality** beyond what the current story supports.
- Preserve alignment with ST-204:
  - roster lists agents with key summary fields
  - detail view shows identity, objectives, permissions, thresholds, and recent activity
  - filters by department and status
  - restricted fields/actions hidden or disabled by human role
- Keep implementation **Blazor SSR first**, with interactivity only if already required.
- Maintain tenant-aware and role-aware behavior.

Out of scope:

- Building full analytics dashboards, charts, or historical KPI computation.
- Adding new backend analytics pipelines or persistence models unless absolutely required for current profile rendering.
- Mobile changes.
- Broad redesign outside roster/profile flow.

# Files to touch
Likely areas to inspect and update:

- `src/VirtualCompany.Web/**`
  - Agent roster page/component
  - Agent profile/detail page/component
  - Shared layout/navigation components related to agent links
  - Any view models, DTO bindings, or authorization-aware UI helpers
- `src/VirtualCompany.Application/**`
  - Queries/handlers for agent roster and agent detail/profile
  - Any profile-specific response models that should expose stable summary sections for future analytics
- `src/VirtualCompany.Domain/**`
  - Only if a missing domain concept is required for current story semantics
- `src/VirtualCompany.Infrastructure/**`
  - Query/repository implementations if profile data retrieval needs adjustment
- `src/VirtualCompany.Shared/**`
  - Shared contracts if the web/app boundary uses shared DTOs

Also inspect:

- `README.md` for conventions
- Solution/project structure under:
  - `src/VirtualCompany.Api/`
  - `src/VirtualCompany.Web/`
  - `src/VirtualCompany.Application/`

Prefer minimal, targeted changes. Do not touch unrelated modules.

# Implementation plan
1. **Inspect existing roster/profile implementation**
   - Find the current ST-204 implementation status.
   - Identify:
     - roster route/component
     - profile/detail route/component
     - how navigation currently works
     - what data the profile page already renders
     - whether there is already a workload/health/recent activity summary
   - Determine whether the profile page is already the primary destination or if other pages/fragments compete with it.

2. **Make the profile page the canonical anchor**
   - Ensure roster rows/cards link directly to the agent profile page.
   - If there are alternate detail surfaces, align them so the profile route is the primary destination.
   - Keep route naming stable and intuitive, e.g. an agent-specific detail route that can later support analytics sub-sections.
   - If appropriate, introduce clear internal anchors/sections on the profile page such as:
     - Overview
     - Objectives
     - Permissions & thresholds
     - Recent activity
     - Analytics (placeholder/coming soon/empty container)
   - Do not add speculative routing complexity unless it clearly improves future extensibility.

3. **Create a future-friendly profile composition**
   - Refactor the profile page into clearly separated sections/components if the current page is monolithic.
   - Add a stable summary area near the top that can later host analytics widgets.
   - If useful, introduce a lightweight profile view model shaped around:
     - identity
     - operational configuration
     - current status/autonomy
     - workload/health summary
     - recent activity summary
     - reserved analytics section metadata or placeholder state
   - Keep naming explicit and business-oriented.

4. **Preserve current story behavior**
   - Confirm the profile still shows the required ST-204 information:
     - identity
     - objectives
     - permissions
     - thresholds
     - recent activity
   - Confirm the roster still supports:
     - name
     - role
     - department
     - status
     - autonomy level
     - workload/health summary
     - filtering by department and status
   - Confirm restricted fields/actions remain hidden or disabled based on role.

5. **Avoid overbuilding analytics**
   - Do not implement charts, trend engines, or fake metrics.
   - If a placeholder is added, it should be subtle and structural, not noisy.
   - Prefer comments, section boundaries, component seams, and extensible DTO/view-model design over speculative features.

6. **Keep architecture aligned**
   - Follow modular monolith and CQRS-lite patterns already present.
   - Keep UI concerns in Web, query shaping in Application, persistence in Infrastructure.
   - Respect tenant scoping and authorization boundaries in all queries and rendered actions.

7. **Polish for maintainability**
   - Use clear component names and section headings that make future analytics additions obvious.
   - Add concise code comments only where they clarify future extension intent.
   - Avoid introducing dead code or unused abstractions.

# Validation steps
Run and verify at minimum:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification in web app**
   - Open the agent roster page.
   - Verify agents navigate to the profile/detail page as the primary drill-in path.
   - Verify the profile page renders required ST-204 sections:
     - identity
     - objectives
     - permissions
     - thresholds
     - recent activity
   - Verify the page layout clearly supports future analytics expansion without requiring redesign.
   - Verify department/status filtering still works on the roster.
   - Verify restricted fields/actions are hidden or disabled for lower-privilege roles if role-aware UI is already implemented.
   - Verify empty states do not break the profile page when activity/summary data is sparse.

4. **Code quality checks**
   - Ensure no unrelated files were changed.
   - Ensure no placeholder analytics logic introduces misleading business behavior.
   - Ensure route/query/view-model naming is coherent and future-friendly.

# Risks and follow-ups
Risks:

- The existing codebase may not yet have a clean separation between roster summary data and profile detail data, making future-friendly shaping require modest refactoring.
- Role-based UI restrictions may be partially implemented, so changes to the profile page could accidentally expose fields/actions unless carefully checked.
- If recent activity or workload/health summaries are weakly defined today, avoid inventing analytics semantics that conflict with future work.

Follow-ups to note in implementation comments or handoff:

- Future analytics work should extend the **agent profile page first**, rather than creating a disconnected analytics destination.
- Consider a later task to add dedicated profile sub-sections/tabs for:
  - performance trends
  - KPI history
  - workload distribution
  - audit-linked operational insights
- If not already present, a later story may formalize a dedicated application query/view model for profile analytics summaries.
- If route design remains basic today, future work can add nested analytics navigation while preserving the profile page as the canonical anchor.