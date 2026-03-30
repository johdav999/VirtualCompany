# Goal

Implement backlog task **TASK-ST-102 — Company workspace creation and onboarding wizard** for the existing **.NET modular monolith** solution.

Deliver a **web-first onboarding flow in Blazor Web App** that allows an authenticated user to:

- create a new company workspace
- capture required company setup fields
- apply optional industry/business template defaults
- persist onboarding progress so the flow can be resumed
- complete onboarding and land in the web dashboard with starter guidance

This work must align with the architecture and backlog story **ST-102** and fit cleanly into the existing solution structure across **Web, Api, Application, Domain, and Infrastructure** layers.

# Scope

Implement the following functional behavior:

1. **Company workspace creation**
   - Support creation of a company/workspace with:
     - name
     - industry
     - business type
     - timezone
     - currency
     - language
     - compliance region
   - Persist to the `companies` table/model.
   - Create an initial membership for the creating user as the workspace owner/admin according to current auth/membership conventions in the codebase.

2. **Onboarding wizard**
   - Build a multi-step onboarding wizard in the **Blazor Web** app.
   - Wizard should support:
     - step navigation
     - validation per step
     - save/resume behavior
     - final completion action
   - On successful completion, redirect to dashboard and show starter guidance/empty-state guidance.

3. **Progress persistence**
   - Persist onboarding progress server-side so a user can leave and resume later.
   - If no dedicated onboarding persistence exists yet, add a pragmatic v1 design that fits the architecture, preferably using flexible JSON-backed settings/state rather than overengineering.
   - Resume should restore the latest saved step and entered values.

4. **Template defaults**
   - Support industry/business templates that can prefill recommended defaults.
   - Keep the template model extensible and data-driven.
   - Avoid hardcoding template behavior in UI-only logic.
   - A simple seed-backed or configuration-backed template catalog is acceptable for v1.

5. **Dashboard landing**
   - After successful onboarding completion, route the user to the web dashboard.
   - If the dashboard is not fully implemented yet, provide a safe placeholder/landing page with starter guidance such as:
     - next steps
     - invite teammates
     - hire first agents
     - upload company knowledge

6. **Tenant-aware alignment**
   - Ensure the implementation is compatible with the shared-schema multi-tenant model.
   - Company creation and membership creation must be consistent with tenant isolation patterns already present or being introduced by ST-101.

Out of scope unless required by existing code patterns:

- full invitation flow
- advanced branding editor
- mobile onboarding
- SSO
- complex workflow engine integration
- production-grade analytics on onboarding completion

# Files to touch

Inspect the repository first and then update only the files needed. Expect to touch files in these areas if they exist:

- `src/VirtualCompany.Web/**`
  - onboarding pages/components
  - forms/view models
  - dashboard landing/empty state
  - navigation/route guards if needed

- `src/VirtualCompany.Api/**`
  - endpoints/controllers for company creation, onboarding state, template retrieval, completion
  - request/response DTOs if API layer owns them

- `src/VirtualCompany.Application/**`
  - commands/queries and handlers for:
    - create company
    - save onboarding progress
    - load onboarding progress
    - list templates
    - complete onboarding
  - validation
  - mapping

- `src/VirtualCompany.Domain/**`
  - company aggregate/entity updates
  - membership creation rules if domain-owned
  - onboarding/template domain models if appropriate
  - enums/value objects for setup fields/statuses

- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence/configuration
  - repositories
  - migrations
  - seed/config loading for templates
  - JSONB mapping if used for settings/progress/template defaults

Potential additional files:

- solution registration / DI setup
- shared contracts in `src/VirtualCompany.Shared/**`
- tests in existing test projects if present
- `README.md` only if a brief setup note is necessary

Do not invent broad new modules if a simpler implementation fits the current codebase.

# Implementation plan

1. **Inspect the current solution structure**
   - Identify:
     - auth/user context patterns
     - company and membership entities
     - existing CQRS conventions
     - EF Core DbContext and migrations approach
     - Blazor app routing/layout/form patterns
     - whether dashboard page already exists
   - Reuse existing patterns over introducing new abstractions.

2. **Model onboarding state**
   - Add a minimal onboarding persistence model.
   - Preferred v1 approach:
     - store onboarding state in a flexible JSON field associated with the company or a dedicated onboarding record if the codebase already favors explicit tables.
   - Include at minimum:
     - current step
     - entered company setup values
     - selected template identifiers
     - completion flag/status
     - timestamps
   - Keep the design extensible for future onboarding steps.

3. **Implement template catalog**
   - Add a data-driven template representation for industry/business defaults.
   - Templates should be able to prefill recommended defaults such as:
     - settings JSON
     - starter guidance hints
     - optional future defaults
   - Seed a small initial set if no template source exists.
   - Keep template retrieval in application/API layer, not embedded directly in Razor components.

4. **Implement application commands/queries**
   - Add CQRS-lite handlers for:
     - `GetOnboardingTemplates`
     - `GetOnboardingProgress`
     - `SaveOnboardingProgress`
     - `CreateCompanyWorkspace`
     - `CompleteOnboarding`
   - Ensure validation for required fields and acceptable values.
   - Ensure the creating user becomes the initial company member with owner/admin role per current role model.
   - Make completion idempotent where practical.

5. **Implement persistence**
   - Update EF Core entities/configuration.
   - Add migration(s) for any schema changes.
   - Use PostgreSQL-friendly types and JSONB where appropriate.
   - Ensure tenant-owned records include `company_id` where required by the existing model.

6. **Implement API endpoints**
   - Add endpoints for:
     - loading templates
     - loading current onboarding progress
     - saving progress
     - creating/completing workspace setup
   - Secure endpoints for authenticated users.
   - Return safe errors with field-level validation details where possible.

7. **Build Blazor onboarding wizard**
   - Create a multi-step wizard with clear steps such as:
     1. company basics
     2. locale/compliance
     3. template selection/review
     4. confirmation
   - Support:
     - next/back
     - save and resume
     - loading existing progress on entry
     - validation messages
     - disabled completion until valid
   - Keep UI simple, production-safe, and aligned with current app styling.

8. **Add post-completion landing behavior**
   - Redirect to dashboard after completion.
   - If dashboard exists, add starter guidance card/empty state.
   - If not, create a minimal dashboard placeholder page that includes:
     - workspace created confirmation
     - suggested next actions

9. **Authorization and tenant correctness**
   - Ensure onboarding endpoints and pages operate in the correct user context.
   - Prevent users from reading/updating onboarding state for workspaces they do not own or belong to.
   - If ST-101 infrastructure is partial, implement the minimum safe checks consistent with current code.

10. **Testing**
   - Add or update tests for:
     - company creation
     - initial membership creation
     - onboarding progress save/load
     - template application
     - completion redirect/behavior where testable
   - Prefer focused unit/application tests plus any existing integration test style already used in the repo.

11. **Keep implementation pragmatic**
   - No speculative architecture.
   - No unnecessary generic wizard framework.
   - No client-only state as the source of truth.
   - Favor maintainable, incremental delivery.

# Validation steps

Run and verify the following after implementation:

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual functional verification**
   - Start the app using the repo’s existing run configuration.
   - Sign in as a valid user.
   - Navigate to onboarding/workspace creation.
   - Verify a user can enter:
     - company name
     - industry
     - business type
     - timezone
     - currency
     - language
     - compliance region
   - Verify validation blocks invalid submission.
   - Select a template and confirm defaults are applied/persisted.
   - Save progress mid-wizard, leave, and resume later.
   - Complete onboarding and confirm:
     - company record exists
     - membership exists for creator
     - onboarding is marked complete
     - user lands on dashboard/landing page with starter guidance

4. **Persistence verification**
   - Confirm schema changes/migrations apply cleanly.
   - Confirm onboarding progress survives app restart.
   - Confirm JSON/JSONB fields serialize and deserialize correctly.

5. **Security verification**
   - Confirm unauthenticated access is blocked.
   - Confirm a different user cannot access another company’s onboarding state unless authorized by existing membership rules.

6. **Regression check**
   - Ensure no obvious breakage to existing auth, navigation, or company-related flows.

# Risks and follow-ups

- **Dependency on ST-101 state**  
  If tenant-aware auth/membership foundations are incomplete, implement the minimum safe integration and clearly note assumptions in code comments or a short summary.

- **Onboarding persistence design**  
  If there is no existing onboarding model, prefer a simple extensible JSON-backed approach now, but note whether a dedicated onboarding table may be warranted later.

- **Template extensibility**  
  Seed/config-backed templates are fine for v1, but document where future admin-managed templates could plug in.

- **Dashboard readiness**  
  If the full dashboard is not yet implemented, provide a minimal but polished landing page rather than blocking completion.

- **Role naming mismatch**  
  Use the repository’s existing role constants/enums if present. Do not create conflicting role names.

- **Migration safety**  
  Keep schema changes minimal and reversible. Avoid introducing tables/relations that are broader than this story requires.

- **Follow-up suggestions**
  - richer branding/settings step
  - onboarding checklist progress widget in dashboard
  - starter agent recommendations based on template
  - teammate invite step integration with ST-103
  - audit event emission for workspace creation and onboarding completion