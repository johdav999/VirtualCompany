# Goal
Implement backlog task **TASK-17.4.1** for story **US-17.4 ST-FUI-174** by adding a complete, policy-aware finance edit experience across web UI and backend for:

1. **Transaction category editing** from the transaction detail view
   - Persist changes through a backend mutation
   - Show **inline validation feedback** for invalid category values
   - Refresh or reflect the saved category after success

2. **Invoice approval status updates**
   - Use the **existing backend workflow** rather than bypassing domain rules
   - Show updated invoice status after save

3. **Permission-aware finance actions**
   - Ensure users without required permissions see finance actions **disabled or hidden consistently** on both transaction and invoice screens

4. **Laura finance context presentation**
   - Ensure Laura’s profile/role presentation page identifies her as the preconfigured **Finance Manager**
   - Display finance capability summaries
   - Ensure role-specific workflow cards link to relevant finance pages and resolve successfully

Follow existing project architecture and conventions. Prefer minimal, cohesive changes over broad refactors.

# Scope
In scope:

- .NET backend command/query/API work needed to support transaction category updates and invoice approval status changes
- Server-side validation and UI inline validation mapping
- Authorization/policy checks for finance edit actions
- Blazor UI updates for:
  - transaction detail edit form
  - invoice status edit action
  - permission-based disabled/hidden states
  - Laura finance context/profile presentation and workflow links
- Tests covering happy path, validation failure, and authorization behavior

Out of scope unless required by existing patterns:

- New finance domain redesign
- New workflow engine implementation
- New role system
- Large navigation or layout rewrites
- Mobile app changes unless a shared contract forces a compile fix
- Introducing new infrastructure patterns when an existing command/workflow path already exists

# Files to touch
Inspect the solution first and then update the actual matching files. Likely areas include:

- `src/VirtualCompany.Web/**`
  - Transaction detail page/component
  - Invoice detail page/component
  - Laura profile/context page/component
  - Shared finance action components
  - View models / form models / validators
- `src/VirtualCompany.Api/**`
  - Finance-related endpoints/controllers/minimal APIs
- `src/VirtualCompany.Application/**`
  - Commands/handlers for transaction category update
  - Commands/handlers or workflow invocations for invoice approval status change
  - Validation classes
  - Authorization/policy services
  - DTOs/query models returned to web
- `src/VirtualCompany.Domain/**`
  - Finance entities/value objects/enums if category/status rules live here
- `src/VirtualCompany.Infrastructure/**`
  - Repository/data access updates
  - Workflow integration plumbing if needed
- `src/VirtualCompany.Shared/**`
  - Shared contracts/enums if used by both API and Web
- `tests/VirtualCompany.Api.Tests/**`
  - API/handler authorization and validation tests
- Potentially web test project files if present in repo

Before editing, locate the concrete finance modules and use the existing vertical slice / CQRS / feature organization already present.

# Implementation plan
1. **Discover existing finance implementation**
   - Search for:
     - transaction detail pages
     - invoice detail pages
     - finance permissions/policies
     - Laura seed/profile/configuration
     - existing invoice approval workflow commands/endpoints
   - Identify current patterns for:
     - form binding in Blazor
     - validation result transport from API to UI
     - authorization checks in UI and backend
     - navigation/link generation for workflow cards

2. **Implement transaction category backend mutation**
   - Add or extend an application command such as `UpdateTransactionCategory`
   - Enforce:
     - tenant/company scoping
     - authorization for finance edit capability
     - allowed category validation
   - Return structured validation errors suitable for inline display
   - Persist the updated category and updated timestamp
   - If audit/event patterns already exist, emit the appropriate audit event

3. **Wire API endpoint for transaction category update**
   - Add/update endpoint in API layer
   - Map application validation failures to the project’s standard validation response shape
   - Map forbidden/not-found correctly
   - Do not leak cross-tenant existence

4. **Implement transaction detail edit form in Blazor**
   - Add editable category UI on the transaction detail view
   - Use existing form components/patterns where possible
   - On submit:
     - call backend mutation
     - show field-level inline validation for invalid category
     - show success state and refresh bound transaction data
   - Respect permission state:
     - editable for authorized users
     - disabled or hidden for unauthorized users, matching existing UX conventions

5. **Implement invoice approval status change via existing workflow**
   - Find the current backend workflow path for invoice approval/status transitions
   - Reuse it rather than directly updating persistence
   - Add/update UI action on invoice screen to invoke that path
   - After save, refresh invoice data so the updated status is visible
   - Preserve domain rules for valid transitions and authorization

6. **Unify permission-aware finance action rendering**
   - Identify how permissions are exposed to the web layer
   - Ensure transaction and invoice screens use the same capability checks
   - Make behavior consistent:
     - if current UX convention is hidden, use hidden
     - if current UX convention is disabled with explanation, use that consistently
   - Avoid UI-only security; backend must still enforce authorization

7. **Update Laura finance context/profile presentation**
   - Find Laura’s seeded/configured profile page or role presentation page
   - Ensure it clearly identifies her as **Finance Manager**
   - Add or fix finance capability summaries
   - Verify role-specific workflow cards point to valid finance routes
   - Fix broken links or route mismatches
   - Ensure linked pages resolve successfully for authorized users

8. **Validation and error handling**
   - Reuse existing validation framework if present, likely FluentValidation or custom result objects
   - Ensure invalid category values produce field-specific messages, not generic toast-only failures
   - Ensure invalid invoice status transitions surface meaningful feedback consistent with current UX

9. **Testing**
   - Add backend tests for:
     - authorized transaction category update succeeds
     - invalid category returns validation error
     - unauthorized update is forbidden
     - invoice approval status change uses valid workflow path and updates status
   - Add UI/component tests if test infrastructure exists; otherwise cover via API tests and manual validation steps

10. **Keep changes small and coherent**
   - Prefer extending existing finance feature slices
   - Avoid introducing duplicate DTOs or parallel permission systems
   - If data is seeded for Laura, update seed/config in the existing location only

# Validation steps
Run these after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification: transaction category**
   - Open transaction detail as an authorized finance user
   - Edit category to a valid value and save
   - Confirm category updates and remains updated after refresh
   - Enter an invalid/unsupported category
   - Confirm inline field validation appears without a crash or generic-only error

4. **Manual verification: invoice approval status**
   - Open invoice screen as an authorized user
   - Change approval status through the UI
   - Confirm backend workflow executes successfully
   - Confirm updated status is shown after save/refresh
   - Try an invalid transition if possible and confirm proper feedback

5. **Manual verification: permissions**
   - Sign in as a user lacking finance edit permissions
   - Confirm finance actions on both transaction and invoice screens are consistently disabled or hidden
   - Attempt direct API calls if test harness exists; confirm backend forbids access

6. **Manual verification: Laura**
   - Open Laura’s profile/context page
   - Confirm she is labeled as **Finance Manager**
   - Confirm finance capability summaries are visible
   - Click each finance workflow card
   - Confirm each link resolves to the intended finance page without broken navigation

7. **Regression check**
   - Verify no compile/runtime regressions in related finance pages
   - Verify tenant scoping still applies correctly

# Risks and follow-ups
- **Unknown existing finance structure:** The repo may already have partial finance features under different names. Adapt to actual code organization rather than forcing new abstractions.
- **Validation response mismatch:** Inline validation depends on the current API-to-Blazor error contract. If inconsistent, normalize carefully without breaking other forms.
- **Workflow coupling:** Invoice approval status must use the existing workflow path. Avoid shortcut persistence updates that bypass approvals/audit logic.
- **Permission inconsistency:** Transaction and invoice screens may currently use different capability checks. Consolidate carefully to avoid accidental privilege changes.
- **Laura seed/config location:** Her profile may be seeded in data, config, or UI composition. Update the authoritative source only.
- **Follow-up suggestion:** If permission checks are duplicated across finance screens, consider a shared finance capability helper/component in a later task, but do not over-refactor in this task.