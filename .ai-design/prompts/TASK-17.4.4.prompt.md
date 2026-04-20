# Goal
Implement backlog task **TASK-17.4.4 — Build Laura finance role presentation view with capability cards and finance deep links** for story **US-17.4 ST-FUI-174 — Controlled finance edits, policy-aware actions, and Laura finance context**.

Deliver a cohesive finance UX slice that:
- presents **Laura** as the preconfigured **Finance Manager**
- shows a finance-focused profile/context page with **capability summary cards**
- includes **role-specific workflow/deep-link cards** to relevant finance pages
- supports controlled finance edits on transaction and invoice screens
- enforces permission-aware visibility/disabled states consistently
- uses existing backend workflow patterns for invoice approval status changes
- provides inline validation feedback for invalid transaction category updates

# Scope
In scope:
- Add or update the **Laura finance presentation/profile view** in the Blazor web app.
- Surface Laura as a seeded/preconfigured finance agent/role if not already represented in UI composition.
- Add finance capability cards and finance workflow/deep-link cards that navigate to relevant finance pages.
- Enable authorized users to update **transaction category** from transaction detail UI.
- Show **inline validation** for invalid transaction category values.
- Enable authorized users to change **invoice approval status** through the **existing backend workflow/application path**, not via ad hoc direct persistence.
- Refresh UI after save so updated invoice status is visible immediately.
- Apply consistent permission handling across transaction and invoice screens:
  - hide restricted actions where that is the established pattern
  - otherwise disable with clear non-destructive UX
- Add/update tests covering authorization, validation, rendering, and link resolution.

Out of scope:
- New finance domain model redesigns.
- New workflow engine architecture.
- Mobile app changes unless a shared contract must be updated.
- Broad roster redesign outside what is necessary to expose Laura’s finance context page.
- Introducing new external integrations.

# Files to touch
Inspect the solution first and then modify the minimum necessary files in these likely areas.

Likely projects:
- `src/VirtualCompany.Web`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests` and/or any web/application test projects present

Likely file categories to touch:
- Blazor pages/components for:
  - agent profile / role presentation
  - finance transaction detail
  - finance invoice detail
  - reusable capability/deep-link cards
- Application commands/queries/validators for:
  - updating transaction category
  - changing invoice approval status via existing workflow
  - loading Laura finance context view model
- Authorization/policy helpers for finance actions
- Seed/configuration files if Laura’s preconfigured Finance Manager identity is data-driven
- Navigation route constants or page link builders
- DTO/view model contracts shared between app and web
- Tests:
  - command validation tests
  - authorization tests
  - page/component rendering tests
  - route/link resolution tests

Before editing, locate:
- existing finance transaction and invoice detail pages
- existing agent roster/profile pages
- existing policy/permission checks
- existing workflow command for invoice approval/status changes
- any seed data or template config for Laura / Finance Manager

# Implementation plan
1. **Discover existing implementation paths**
   - Search for:
     - finance transaction detail page/component
     - invoice detail page/component
     - agent profile/roster/detail pages
     - Laura seed/template/config
     - finance-related permissions/policies
     - invoice approval workflow commands/handlers
   - Reuse existing CQRS, validation, and authorization patterns already used in the solution.
   - Do not bypass established application-layer boundaries.

2. **Model Laura finance context view**
   - Add or extend a query/view model that loads a finance role presentation for Laura.
   - Ensure the page clearly identifies:
     - display name: Laura
     - role/title: Finance Manager
     - finance context/capability summaries
   - Capability cards should be concise and role-specific, for example:
     - transaction review/categorization
     - invoice approval workflow oversight
     - finance policy-aware actions
     - audit/reconciliation visibility
   - Keep content data-driven if the app already uses seeded agent/template metadata; otherwise use a minimal presentation mapping in the web layer.

3. **Add workflow/deep-link cards**
   - Add cards on Laura’s finance context page linking to relevant finance destinations already present in the app.
   - Prefer existing named routes/constants/helpers over hardcoded strings.
   - Each card should include:
     - title
     - short description
     - CTA/link
   - Candidate destinations:
     - transactions list/detail area
     - invoices list/detail area
     - approvals/inbox if finance-relevant
   - Ensure links resolve successfully and do not point to placeholder routes.

4. **Implement transaction category update flow**
   - On transaction detail view, add/edit UI for authorized users to update category.
   - Wire the action through the application layer using a command/handler.
   - Add validation for invalid category values using the project’s existing validation approach.
   - Show **inline field-level validation feedback** in the UI.
   - For unauthorized users, apply the established UX convention:
     - hidden if the app hides restricted actions
     - otherwise disabled/read-only with consistent styling
   - Avoid direct DB updates from UI or controller/page code.

5. **Implement invoice approval status change via existing backend workflow**
   - Find the existing backend workflow/application path for invoice approval status changes and use it.
   - If a thin adapter command is needed for the UI, make it delegate into the existing workflow logic rather than duplicating business rules.
   - On successful save:
     - reload or update the invoice detail state
     - show the updated status immediately
   - Preserve auditability and policy enforcement already present in the workflow path.

6. **Unify permission handling**
   - Centralize or reuse finance permission checks so transaction and invoice screens behave consistently.
   - Ensure the same permission semantics are used across:
     - category edit affordance
     - invoice approval status change affordance
     - finance deep links/cards if they should be gated
   - If there is an existing authorization service/policy abstraction, extend it rather than adding page-local logic.

7. **Polish Laura presentation UX**
   - Keep the page aligned with existing Blazor styling/components.
   - Make Laura’s role presentation feel like an anchor page for finance context, not a disconnected marketing card.
   - If there is an existing agent profile shell, compose within it rather than creating a parallel page structure.

8. **Add tests**
   - Application tests:
     - invalid transaction category rejected
     - authorized category update succeeds
     - unauthorized category update denied
     - invoice approval status change uses valid workflow path and returns updated state
   - UI/component/page tests where available:
     - Laura page renders Finance Manager identity and capability cards
     - workflow/deep-link cards render expected destinations
     - restricted actions hidden/disabled for users lacking permission
     - inline validation message appears for invalid category input
   - If route tests are not present, add lightweight assertions around generated URLs or navigation targets.

9. **Keep implementation minimal and consistent**
   - Follow modular monolith and CQRS-lite boundaries from the architecture.
   - Prefer additive changes over refactors unless a small shared helper reduces duplication materially.
   - Avoid introducing new abstractions unless repeated logic clearly warrants it.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual verification in web app:
   - Navigate to Laura’s profile/role presentation page.
   - Confirm page identifies Laura as **Finance Manager**.
   - Confirm finance capability summary cards are visible and styled consistently.
   - Click each finance workflow/deep-link card and verify destination loads successfully.

4. Transaction detail verification:
   - As an authorized user:
     - open a transaction detail page
     - update category with a valid value
     - save and confirm updated category persists
   - Try an invalid category value and confirm **inline validation feedback** appears without a broken page flow.
   - As an unauthorized user:
     - confirm category edit action is hidden or disabled consistently with app conventions

5. Invoice detail verification:
   - As an authorized user:
     - change invoice approval status through the UI
     - save
     - confirm updated status is shown after save
   - As an unauthorized user:
     - confirm restricted invoice action is hidden or disabled consistently

6. Regression checks:
   - Verify no tenant-scoping or authorization regressions in finance pages.
   - Verify no direct persistence bypasses existing application/workflow logic.
   - Verify navigation from roster/profile to Laura finance context still works if applicable.

# Risks and follow-ups
- **Risk: Laura may not yet exist as seeded/configured data**
  - Follow existing seed/template patterns.
  - If missing, add the smallest data/config change necessary and avoid hardcoding where a seeded agent/template already exists conceptually.

- **Risk: invoice approval status may already be controlled by a workflow with non-obvious entry points**
  - Reuse the existing command/service/handler path.
  - Do not duplicate approval logic in the UI layer.

- **Risk: permission behavior may currently differ between transaction and invoice screens**
  - Normalize behavior through shared policy checks, but preserve established UX conventions where the app intentionally hides vs disables.

- **Risk: category validation source of truth may be unclear**
  - Prefer domain/application validation backed by known allowed values or existing enums/reference data.
  - UI validation should complement, not replace, server-side validation.

- **Risk: deep links may target pages behind permissions**
  - Ensure links resolve successfully; if access is restricted, handle gracefully with existing authorization UX rather than broken navigation.

Follow-ups if needed after this task:
- consolidate finance permission checks into a shared authorization helper if duplication remains
- move Laura capability/deep-link card content to configuration/seed metadata if currently embedded in UI
- add broader finance audit/explainability links from Laura’s context page in a later task