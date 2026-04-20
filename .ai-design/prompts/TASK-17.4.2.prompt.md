# Goal
Implement `TASK-17.4.2` for story `US-17.4 ST-FUI-174` by adding permission-aware finance action controls across transaction and invoice UI flows, wiring invoice approval status updates through the existing backend workflow, and surfacing Laura as the preconfigured Finance Manager with finance capability/context links.

The coding agent should deliver a vertical slice that:
- lets authorized users edit transaction category from transaction detail
- shows inline validation for invalid category values
- lets authorized users change invoice approval status using the existing backend workflow
- refreshes and displays updated invoice status after save
- consistently hides or disables restricted finance actions for unauthorized users across transaction and invoice screens
- updates Laura’s profile/role presentation to clearly identify her as the preconfigured Finance Manager
- adds finance capability summaries and role-specific workflow cards on Laura’s finance context view with working links

# Scope
In scope:
- Blazor Web UI updates for transaction detail and invoice screens
- Permission-aware rendering based on existing auth/policy patterns in the solution
- Application/API integration needed to submit transaction category changes and invoice approval status changes
- Reuse of existing backend approval/workflow endpoints/services where available
- Inline validation UX for transaction category edits
- Laura finance profile/context presentation updates
- Navigation/linking for finance workflow cards

Out of scope unless strictly required to complete acceptance criteria:
- New finance domain model redesign
- New approval engine implementation if an existing workflow already supports invoice approval status changes
- Broad RBAC refactor
- Mobile app changes
- New database schema unless current persistence lacks a minimal field/query needed for Laura presentation or finance capability summaries

Implementation constraints:
- Prefer existing CQRS-lite patterns in Application layer
- Keep tenant-aware and permission-aware behavior consistent with current authorization approach
- Do not bypass backend policy checks with UI-only enforcement
- If permissions are not already exposed to the UI, add the smallest shared contract necessary
- Reuse existing workflow/approval backend path for invoice approval status changes rather than inventing a parallel update path

# Files to touch
Inspect first, then modify only what is necessary in these likely areas.

Likely frontend files:
- `src/VirtualCompany.Web/**/Transactions/*.razor`
- `src/VirtualCompany.Web/**/Invoices/*.razor`
- `src/VirtualCompany.Web/**/Agents/*.razor`
- `src/VirtualCompany.Web/**/Profiles/*.razor`
- `src/VirtualCompany.Web/**/Shared/*.razor`
- `src/VirtualCompany.Web/**/Components/*.razor`
- `src/VirtualCompany.Web/**/Services/*.cs`

Likely API/Application files:
- `src/VirtualCompany.Api/**/Controllers/*.cs`
- `src/VirtualCompany.Api/**/Endpoints/*.cs`
- `src/VirtualCompany.Application/**/Transactions/*.cs`
- `src/VirtualCompany.Application/**/Invoices/*.cs`
- `src/VirtualCompany.Application/**/Approvals/*.cs`
- `src/VirtualCompany.Application/**/Finance/*.cs`
- `src/VirtualCompany.Shared/**/*.cs`

Likely domain/infrastructure files if needed:
- `src/VirtualCompany.Domain/**/*.cs`
- `src/VirtualCompany.Infrastructure/**/*.cs`

Likely tests:
- `tests/VirtualCompany.Api.Tests/**/*.cs`
- any existing Web/UI/component test project if present

Also inspect:
- `README.md`
- solution-wide auth/permission helpers
- existing Laura seed/configuration files
- existing finance navigation/menu definitions

# Implementation plan
1. **Discover existing finance flows and permission model**
   - Search for:
     - transaction detail page/component
     - invoice detail/list page/component
     - approval status UI
     - category editing UI
     - Laura agent/profile page
     - permission checks such as policies, roles, claims, capability flags, `AuthorizeView`, or custom helpers
   - Search for backend handlers/endpoints related to:
     - transaction category update
     - invoice approval status change
     - approval workflow execution
   - Identify whether finance permissions are represented as:
     - role names
     - policy names
     - capability flags
     - membership permissions JSON-derived claims

2. **Map acceptance criteria to concrete UI states**
   - Transaction detail:
     - authorized: editable category control + save action + inline validation
     - unauthorized: hidden or disabled control, matching app conventions
   - Invoice screen:
     - authorized: approval status action control bound to existing backend workflow
     - unauthorized: hidden or disabled consistently
   - Laura page:
     - visible Finance Manager identity marker
     - finance capability summary section
     - workflow cards with valid links

3. **Implement or reuse permission abstraction for finance actions**
   - If the UI already has a permission service/view model, extend it with the minimum needed flags, for example:
     - `CanEditTransactionCategory`
     - `CanChangeInvoiceApprovalStatus`
     - `CanViewFinanceCapabilities`
   - If no such abstraction exists, add a small shared DTO/view model from API to UI rather than scattering role checks in Razor.
   - Ensure backend endpoints also enforce authorization/policy checks.

4. **Add transaction category editing with inline validation**
   - On the transaction detail view:
     - render category as editable only for authorized users
     - use existing form components/patterns
     - validate invalid values inline before submit where possible
     - also handle server-side validation errors and display them inline
   - If categories are constrained:
     - prefer dropdown/select from allowed values if available
     - if free text is current pattern, validate against backend rules and show field-level message
   - On successful save:
     - refresh transaction detail state
     - show updated category without full page inconsistency
   - On unauthorized:
     - hide or disable according to existing UX conventions, and keep consistency with invoice screen

5. **Wire invoice approval status action controls to existing backend workflow**
   - Do not directly mutate invoice status if the system already routes this through approvals/workflows.
   - Reuse existing command/endpoint/service if present.
   - If the UI currently lacks a control:
     - add a status action dropdown/button group/modal on invoice detail
     - submit selected status transition through the existing backend workflow endpoint/service
   - After save:
     - reload invoice detail or patch local state from response
     - display updated approval status immediately
   - Handle:
     - validation/business rule failures
     - permission denial
     - stale state/conflict if supported by backend

6. **Normalize restricted action rendering across transaction and invoice screens**
   - Use one shared helper/component/pattern so both screens behave the same.
   - Decide based on current app conventions whether restricted actions should be hidden or disabled; acceptance allows either, but behavior must be consistent.
   - If mixed behavior already exists, standardize finance actions in both places.
   - Ensure labels/tooltips/messages are consistent if disabled controls are used.

7. **Update Laura’s profile/role presentation**
   - Find the seeded/preconfigured Laura agent/user/profile page.
   - Ensure the page explicitly identifies Laura as:
     - `Finance Manager`
   - Add finance capability summaries, for example:
     - transaction categorization
     - invoice approval workflow participation
     - finance policy-aware actions
     - approval/review responsibilities
   - Keep copy grounded in actual enabled capabilities, not fictional features.

8. **Add role-specific workflow cards on Laura’s finance context view**
   - Add cards/tiles/links for relevant finance pages already in the app, such as:
     - transactions
     - invoices
     - approvals/inbox
     - finance dashboard if present
   - Each card should:
     - have a clear title and short summary
     - navigate to an existing route
     - resolve successfully without broken links
   - If a target page does not exist, do not invent a dead route; instead link only to existing finance-relevant pages.

9. **Backend adjustments only if required**
   - If transaction category update endpoint/command is missing validation:
     - add field-level validation in Application layer
     - return structured validation errors consumable by Blazor UI
   - If invoice approval status workflow endpoint is not exposed to the web app:
     - add a thin API endpoint that delegates to the existing application workflow/command
   - Ensure tenant scoping and authorization are enforced server-side.

10. **Testing**
   - Add/update tests for:
     - authorized transaction category update success
     - invalid transaction category returns validation error
     - unauthorized transaction category update forbidden/blocked
     - authorized invoice approval status change uses workflow path and returns updated status
     - unauthorized invoice approval status change forbidden/blocked
     - Laura profile/context content and workflow links if UI/component tests exist
   - If only API tests are practical, prioritize backend authorization/validation coverage and manually verify UI rendering.

11. **Implementation quality requirements**
   - Keep changes minimal and cohesive
   - Follow existing naming, folder, and CQRS conventions
   - Avoid duplicating permission logic in multiple Razor files
   - Prefer reusable components/helpers for finance action gating
   - Add comments only where behavior is non-obvious

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Manual verification in web app:
   - Open transaction detail as authorized finance user
     - confirm category is editable
     - enter invalid value
     - confirm inline validation appears
     - enter valid value and save
     - confirm updated category is shown
   - Open transaction detail as unauthorized user
     - confirm finance action is hidden or disabled consistently with app conventions

3. Invoice verification:
   - Open invoice screen as authorized finance user
     - confirm approval status action control is visible
     - change status through UI
     - confirm request goes through existing backend workflow path
     - confirm updated status is shown after save
   - Open invoice screen as unauthorized user
     - confirm restricted action is hidden or disabled consistently with transaction screen

4. Laura verification:
   - Open Laura’s profile/role presentation page
   - confirm she is explicitly labeled as the preconfigured Finance Manager
   - confirm finance capability summaries are visible
   - confirm workflow cards are present
   - click each workflow card and verify destination page loads successfully

5. Regression checks:
   - confirm no tenant scoping regressions
   - confirm no unauthorized backend updates are possible via direct API call
   - confirm validation errors are user-friendly and not raw exception text

# Risks and follow-ups
- **Risk: permission model ambiguity**
  - The solution may not yet expose fine-grained finance permissions to the UI.
  - Follow-up: introduce a shared finance capability projection rather than hardcoding role names in Razor.

- **Risk: no existing invoice workflow endpoint**
  - Acceptance says to use the existing backend workflow, but the UI may currently bypass or lack access to it.
  - Follow-up: add a thin API adapter to the existing application command/workflow, not a new status mutation path.

- **Risk: inconsistent UX conventions for restricted actions**
  - Some screens may hide while others disable.
  - Follow-up: standardize via a shared component/helper for permission-aware action rendering.

- **Risk: Laura data may be seeded differently**
  - Laura may be an agent, a user, or a static presentation page.
  - Follow-up: update whichever source currently drives her displayed role/capabilities, and avoid duplicating truth across seed + UI constants.

- **Risk: missing UI test infrastructure**
  - If component/UI tests are absent, coverage may rely mostly on API tests and manual verification.
  - Follow-up: add lightweight component tests for permission-aware rendering in a later task.

- **Risk: category validation source of truth**
  - If valid transaction categories are not centralized, validation may drift between UI and backend.
  - Follow-up: centralize allowed category values in shared contracts or server-provided metadata.