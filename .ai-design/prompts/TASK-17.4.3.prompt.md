# Goal
Implement backlog task **TASK-17.4.3 — Add policy and role checks to finance action components and route-level data loading** for story **US-17.4 ST-FUI-174 — Controlled finance edits, policy-aware actions, and Laura finance context**.

The coding agent should update the .NET/Blazor solution so that finance-related UI actions and route-loaded data are consistently authorization-aware, policy-aware, and aligned with existing backend workflows.

Deliver the feature end-to-end across transaction detail, invoice screens, and Laura’s finance context/profile presentation.

# Scope
In scope:

- Add authorization/policy checks to finance action UI components.
- Add route-level data loading guards so finance pages only load/edit data when the current user is allowed.
- Enable authorized users to:
  - update a transaction category from the transaction detail view
  - receive inline validation feedback for invalid category values
  - change invoice approval status through the existing backend workflow
  - see updated invoice status after save
- Ensure unauthorized users see restricted finance actions disabled or hidden consistently across transaction and invoice screens.
- Ensure Laura’s profile/role presentation identifies her as the preconfigured **Finance Manager**.
- Add/display finance capability summaries on Laura’s profile or finance context page.
- Ensure Laura’s role-specific workflow cards link to relevant finance pages and those routes resolve successfully.
- Reuse existing application/backend patterns where possible.
- Add/update tests for authorization behavior, validation behavior, and route resolution.

Out of scope unless required by existing code structure:

- New finance domain models beyond what is needed for this task.
- Replacing the existing invoice approval backend workflow.
- Broad redesign of authorization infrastructure.
- New mobile functionality.
- New database schema unless absolutely necessary to support existing seeded Laura role/capability presentation.

# Files to touch
Inspect the solution first and then modify the minimum necessary files. Likely areas include:

- `src/VirtualCompany.Web/**/*`
  - finance pages/components
  - transaction detail page/component
  - invoice detail/list/action components
  - Laura profile/agent/role presentation pages
  - route-level loaders or page initialization logic
  - shared authorization-aware UI helpers/components
- `src/VirtualCompany.Api/**/*`
  - finance endpoints if route-level authorization/data loading is API-backed
  - policy/authorization wiring if missing
- `src/VirtualCompany.Application/**/*`
  - finance queries/commands
  - authorization/policy evaluation services
  - DTOs/view models for finance capability summaries and Laura context
  - validation for transaction category updates
- `src/VirtualCompany.Domain/**/*`
  - enums/value objects/constants for transaction categories or finance capabilities if needed
- `src/VirtualCompany.Infrastructure/**/*`
  - repository/query implementations
  - seeded/preconfigured Laura finance manager data if this is infrastructure-seeded
- `src/VirtualCompany.Shared/**/*`
  - shared contracts used by web/api if applicable
- `tests/VirtualCompany.Api.Tests/**/*`
  - API authorization/validation tests
- Also add web/component/unit/integration tests if a corresponding test project already exists in the repo.

Before editing, search for:
- finance pages/components
- transaction detail UI
- invoice approval/status workflow
- authorization policies / `IAuthorizationService` / policy names
- Laura seed/config/profile pages
- workflow cards / navigation cards / finance context pages

# Implementation plan
1. **Discover existing implementation and patterns**
   - Search the codebase for:
     - transaction detail page/component
     - invoice approval status UI and backend workflow
     - finance-related pages/routes
     - Laura agent/profile/role presentation
     - authorization policies, role checks, permission checks
     - route-level data loading patterns in Blazor
   - Identify whether the app uses:
     - ASP.NET Core policies
     - custom permission services
     - role-based checks
     - page-level `[Authorize]`
     - component-level conditional rendering
     - API-first data loading or direct application service injection

2. **Map acceptance criteria to concrete UI and backend touchpoints**
   - Determine the exact transaction detail component where category editing belongs.
   - Determine the exact invoice screen where approval status changes are already surfaced.
   - Determine where Laura’s profile/context page is rendered and how workflow cards are built.
   - Document the existing policy names/permission model and reuse them rather than inventing parallel checks.

3. **Implement transaction category edit authorization**
   - Add a clear authorization gate for the transaction category edit action.
   - Authorized users should be able to:
     - enter/edit category
     - submit changes
   - Unauthorized users should:
     - not be able to invoke the action
     - see the action hidden or disabled consistently with the rest of the finance UI
   - Prefer one shared helper/computed permission model so transaction and invoice screens behave consistently.

4. **Add inline validation for invalid transaction category values**
   - Reuse existing validation infrastructure if present:
     - data annotations
     - FluentValidation
     - command validation
     - Blazor `EditForm` validation
   - Ensure invalid values produce inline feedback in the transaction detail view.
   - Validation should happen before save and also be enforced server-side.
   - If categories are constrained to a known set, bind to that set rather than allowing arbitrary strings where possible.

5. **Implement route-level finance data loading checks**
   - For finance routes/pages, ensure data loading is authorization-aware before sensitive data/actions are shown.
   - If unauthorized:
     - avoid loading restricted data where possible
     - return/route to forbidden/not found/empty restricted state according to existing app conventions
   - Apply this consistently to:
     - transaction detail route
     - invoice route(s)
     - Laura finance context route(s) if they expose finance-only summaries/cards
   - Do not rely only on button hiding; enforce checks in route/page initialization and API/application handlers too.

6. **Wire invoice approval status changes through the existing backend workflow**
   - Find the current backend workflow/command used for invoice approval status changes.
   - Ensure the UI uses that workflow rather than bypassing it.
   - Add/confirm authorization checks for who can change approval status.
   - After save:
     - refresh the invoice view model/state
     - show the updated status immediately
   - Preserve existing audit/workflow semantics.

7. **Unify restricted-action behavior across transaction and invoice screens**
   - Standardize whether restricted actions are hidden or disabled based on current app conventions.
   - Apply the same behavior to both screens.
   - If the app already uses capability flags in page models/view models, extend that pattern.
   - Avoid duplicated ad hoc `if` checks in multiple components if a shared finance action permission model can be introduced.

8. **Implement Laura Finance Manager presentation**
   - Ensure Laura is presented as the preconfigured **Finance Manager** on her profile or role presentation page.
   - Add/display finance capability summaries, such as:
     - transaction categorization
     - invoice approval workflow participation
     - finance review/oversight capabilities
   - Reuse seeded agent/template/config data if it already exists.
   - If missing, add the minimum seed/config/view-model support needed to present Laura correctly.

9. **Ensure Laura workflow cards resolve successfully**
   - Identify the role-specific workflow cards shown on Laura’s finance context view.
   - Verify each card links to a valid finance route.
   - Fix broken routes, missing parameters, or mismatched navigation targets.
   - Ensure destination pages load successfully under expected authorization conditions.

10. **Harden backend authorization and validation**
   - Confirm API/application command/query handlers enforce the same permissions as the UI.
   - Add or update:
     - policy checks
     - role/permission checks
     - validation errors returned in a UI-consumable shape
   - Prevent unauthorized direct API calls from succeeding even if UI is bypassed.

11. **Add tests**
   - Add/update tests covering:
     - authorized transaction category update succeeds
     - invalid transaction category returns validation errors
     - unauthorized users cannot update transaction category
     - authorized invoice approval status change succeeds through existing workflow
     - unauthorized users cannot change invoice approval status
     - Laura profile/context identifies her as Finance Manager
     - Laura workflow card routes resolve
   - Prefer existing test patterns and fixtures in the repo.

12. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - Web for rendering/navigation
     - Application for commands/queries/authorization orchestration
     - Domain for rules/constants
     - Infrastructure for persistence/seed wiring
   - Keep tenant-aware and policy-enforced behavior intact.
   - Avoid direct DB access from UI.

# Validation steps
Run these steps after implementation:

1. **Build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Manual verification in web app**
   - As an authorized finance-capable user:
     - open transaction detail
     - edit category
     - submit a valid category
     - confirm save succeeds
     - submit an invalid category
     - confirm inline validation feedback appears
   - As an unauthorized user:
     - open transaction and invoice screens
     - confirm restricted finance actions are hidden or disabled consistently
     - confirm direct route/API access does not allow restricted edits
   - On invoice screen as authorized user:
     - change approval status using the existing workflow path
     - save
     - confirm updated status is shown after save
   - On Laura’s profile/finance context page:
     - confirm she is labeled/presented as **Finance Manager**
     - confirm finance capability summaries are visible
     - click each workflow card
     - confirm each target finance page resolves successfully

4. **Regression checks**
   - Confirm no broken navigation on finance pages.
   - Confirm route-level loading still works for authorized users.
   - Confirm unauthorized states do not throw unhandled exceptions.
   - Confirm validation messages are user-friendly and inline.

# Risks and follow-ups
- The repo may already have multiple overlapping authorization mechanisms; do not add a third pattern unless necessary.
- Finance permissions may be modeled as roles, claims, policies, or custom capability flags; discover and extend the existing source of truth.
- Route-level loading in Blazor may be implemented differently across SSR/interactive components; keep behavior consistent with current app architecture.
- Laura may be seeded via templates, fixtures, or hardcoded demo data; update the correct source rather than patching only the UI.
- If invoice approval status is tightly coupled to workflow/approval modules, avoid shortcut updates that bypass auditability.
- If transaction categories are currently free-form, introducing strict validation may affect existing data; validate only the edit path unless broader migration is explicitly needed.
- If hidden vs disabled behavior is inconsistent in the current app, follow the dominant existing convention and document any unavoidable deviations.
- If route resolution failures are caused by missing seed/demo data, add the minimum required seeded records and note that in the implementation summary.