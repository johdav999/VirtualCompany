# Goal
Implement backlog task **TASK-7.2.1** for story **ST-102 Company workspace creation and onboarding wizard** by adding the ability for a signed-in user to create a company workspace with these fields:

- `name`
- `industry`
- `business type`
- `timezone`
- `currency`
- `language`
- `compliance region`

This should fit the existing **multi-tenant modular monolith** architecture, use the **Company Setup Module**, persist to PostgreSQL via the existing .NET backend layers, and provide the primary **Blazor Web** onboarding entry point.

Because the task only covers the company creation fields, implement the smallest coherent vertical slice that:
- exposes a backend command/API for company creation,
- persists a `companies` record,
- creates the creator’s initial membership as `owner`,
- enforces tenant-safe patterns,
- and provides a web UI form to submit the company creation flow.

If onboarding wizard progress/resume or template prefills already exist in the codebase, integrate cleanly; otherwise do **not** overbuild them in this task beyond leaving extension points.

# Scope
In scope:
- Domain model support for company creation if missing.
- Application command/handler for creating a company.
- Validation for required fields and basic allowed values/lengths.
- Persistence mapping/repository updates for `companies` and initial `company_memberships`.
- API endpoint or server action used by the web app.
- Blazor Web page/component for entering the required company fields.
- Redirect or success behavior after creation.
- Tests for command validation/handler behavior and any critical endpoint behavior.

Out of scope unless already partially implemented and trivial to finish:
- Full multi-step onboarding wizard.
- Wizard progress persistence/resume.
- Industry/business templates and recommended defaults.
- Branding/settings JSON configuration UX.
- Invitations, advanced role management, or dashboard implementation.
- Mobile app changes.

Assumptions to preserve:
- Shared-schema multi-tenancy with `company_id`.
- Creator becomes the initial `owner` membership for the new company.
- Use policy-based auth patterns already present in the solution.
- Prefer CQRS-lite in the application layer.

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Domain/**`
  - Company aggregate/entity/value objects if present
  - Membership entity if creator membership creation belongs in domain logic

- `src/VirtualCompany.Application/**`
  - Company setup commands/handlers
  - DTOs/contracts
  - Validators
  - Interfaces for repositories/unit of work

- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext / entity configurations
  - Repository implementations
  - Migrations
  - Any current identity-to-user resolution service

- `src/VirtualCompany.Api/**`
  - Endpoint/controller/minimal API for company creation
  - Auth/user context wiring

- `src/VirtualCompany.Web/**`
  - Onboarding/company creation page
  - Form model and validation
  - Navigation after success

- `README.md`
  - Only if setup/run notes need a brief update

Also inspect for existing equivalents before creating new files:
- `Company`, `CompanyMembership`, `CreateCompany*`, `Onboarding*`, `Tenant*`, `CurrentUser*`, `ApplicationDbContext`, `DbContext`, `Companies`, `Memberships`.

# Implementation plan
1. **Discover existing architecture and conventions**
   - Review solution structure and current patterns in:
     - API endpoint style
     - application command/handler organization
     - validation library usage
     - EF Core mapping/migrations
     - auth/current-user abstraction
     - Blazor page/form patterns
   - Reuse naming and folder conventions exactly.

2. **Model the company creation use case**
   - Ensure the `companies` persistence model supports:
     - `name`
     - `industry`
     - `business_type`
     - `timezone`
     - `currency`
     - `language`
     - `compliance_region`
     - timestamps/status if already part of the model
   - Ensure `company_memberships` supports creating an initial owner membership.
   - If entities already exist, extend rather than duplicate.

3. **Add application command**
   - Create a command such as `CreateCompanyCommand`.
   - Include the exact required fields.
   - Return enough data for post-create navigation, e.g. `CompanyId`.
   - Add validation:
     - required fields
     - sensible max lengths
     - trim input
     - reject obviously invalid blank/whitespace values
   - If the codebase already uses enums/lookups for role/status, use them.

4. **Implement handler**
   - Resolve the current authenticated user via existing abstraction.
   - Create the company record.
   - Create the initial membership linking current user to the new company with role `owner`.
   - Save atomically in one transaction/unit of work.
   - If the system tracks audit/business events already, emit a basic company-created event only if the pattern already exists.
   - Do not invent a broad eventing framework for this task.

5. **Persistence and mapping**
   - Update EF Core entity configurations if needed.
   - Add or update migration for the company fields/table if schema is incomplete.
   - Ensure indexes/constraints are reasonable but minimal.
   - Preserve tenant-safe design; the company itself is the tenant root, while membership links user access.

6. **Expose backend entry point**
   - Add an authenticated endpoint/controller action/minimal API route for company creation.
   - Use existing API response conventions.
   - Return validation errors in the project’s standard shape.
   - Ensure only authenticated users can create a company workspace.

7. **Build the Blazor Web form**
   - Add or update the onboarding/company creation page.
   - Include fields for:
     - Company name
     - Industry
     - Business type
     - Timezone
     - Currency
     - Language
     - Compliance region
   - Use built-in validation UI or existing shared form components.
   - Submit to the backend command/API.
   - On success:
     - navigate to the expected next page if one exists,
     - otherwise route to a reasonable placeholder/dashboard/home page already in the app.
   - Keep UX simple and production-safe:
     - disable submit while saving
     - show validation summary/errors
     - show success/failure feedback

8. **Keep extension points for later ST-102 work**
   - Structure code so future tasks can add:
     - wizard progress persistence/resume
     - templates/prefills
     - starter guidance after completion
   - Do this via clean DTOs/services, not TODO-heavy overengineering.

9. **Testing**
   - Add unit/integration tests aligned with existing test style.
   - Cover at least:
     - valid company creation persists company and owner membership
     - invalid input is rejected
     - unauthenticated access is denied at endpoint level if endpoint tests exist
   - If there is an application test project, prefer handler tests there.

10. **Quality pass**
   - Ensure nullable reference types, async usage, and cancellation tokens follow repo standards.
   - Keep code formatting and naming consistent.
   - Avoid unrelated refactors.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used in this repo, create/apply and verify:
   - generate migration for company schema changes
   - run the app against local DB
   - confirm schema includes required company fields and membership linkage

4. Manual verification in web app:
   - Sign in as a valid user.
   - Open the company creation/onboarding page.
   - Submit a valid company with all required fields.
   - Confirm:
     - company record is created,
     - creator gets owner membership,
     - success navigation occurs.

5. Negative-path verification:
   - Submit empty form and confirm field validation.
   - Submit whitespace-only values and confirm rejection.
   - If endpoint can be called directly, verify unauthenticated request is rejected.

6. Data verification:
   - Inspect database rows for:
     - `companies`
     - `company_memberships`
   - Confirm timestamps and role/status values match conventions.

# Risks and follow-ups
- **Risk: existing auth/current-user plumbing may be incomplete.**
  - Follow existing abstractions; if missing, add the smallest reusable current-user service needed.

- **Risk: ST-101 tenant/membership foundations may not be fully implemented yet.**
  - Do not fake tenant context globally; only create the company and owner membership cleanly.
  - If company selection/session context is not ready, redirect to a neutral post-create page.

- **Risk: lookup values for timezone/currency/language/compliance region may be undefined.**
  - For this task, accept validated strings unless the codebase already has canonical option providers.
  - Leave future enhancement points for curated dropdown sources.

- **Risk: onboarding wizard acceptance criteria for progress/resume and templates are broader than this task.**
  - Keep code ready for extension, but do not implement those broader story requirements unless already scaffolded.

- **Follow-up likely needed after this task:**
  - onboarding progress persistence/resume,
  - template-driven prefills,
  - starter guidance/dashboard landing,
  - company selection when a user has multiple memberships,
  - audit event for company creation,
  - curated reference data for timezone/currency/language/compliance region.