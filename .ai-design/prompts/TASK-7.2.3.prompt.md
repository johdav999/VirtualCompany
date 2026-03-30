# Goal
Implement backlog task **TASK-7.2.3 — Industry/business templates can prefill recommended defaults** for **ST-102 Company workspace creation and onboarding wizard**.

Add an extensible, data-driven template mechanism to the company setup flow so that when a user selects an **industry** and/or **business type** during workspace creation, the onboarding wizard can prefill recommended defaults such as:

- timezone
- currency
- language
- compliance region
- optional branding/settings JSON defaults
- any other setup-safe recommended values already supported by the current company creation model

The implementation must fit the existing **.NET modular monolith** architecture, preserve **tenant-safe boundaries**, and keep the template model **extensible without code changes** where practical.

# Scope
In scope:

- Identify the current company creation + onboarding wizard flow in the web app and backend.
- Add a backend-supported template source for recommended defaults keyed by industry and/or business type.
- Expose an application/API query or endpoint that returns recommended defaults for a selected industry/business type.
- Update the onboarding wizard UI so selecting/changing industry/business type can prefill unset fields with recommended defaults.
- Ensure user-entered values are not unexpectedly overwritten once manually changed.
- Keep the design extensible for future template additions without requiring business logic rewrites.
- Add tests for application logic and any UI/component behavior that is already test-covered in the repo.

Out of scope unless already trivial and clearly aligned:

- Full admin CRUD for managing templates.
- Seeding a large production-grade catalog.
- Agent hiring/template behavior beyond company setup defaults.
- Mobile app changes.
- Broad redesign of onboarding UX.

If the codebase already has partial support for templates or setup defaults, extend it rather than replacing it.

# Files to touch
Inspect the solution first and then update the relevant files you actually find. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - company setup entities/value objects
  - any template-related domain models
- `src/VirtualCompany.Application/**`
  - company setup commands/queries
  - DTOs/view models for onboarding
  - validation and orchestration logic
- `src/VirtualCompany.Infrastructure/**`
  - persistence for template data
  - seed/configuration loading
  - repository implementations
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for onboarding/template recommendation lookup
- `src/VirtualCompany.Web/**`
  - Blazor onboarding wizard pages/components
  - form state handling for prefill behavior
- `README.md`
  - only if a short note is needed for seed/config behavior

Also inspect for:
- existing migrations or seed-data conventions
- shared contracts in `src/VirtualCompany.Shared/**`
- test projects related to application/web/API behavior

Do not invent new layers if existing patterns already cover this.

# Implementation plan
1. **Discover current onboarding flow**
   - Find the existing implementation for ST-102-related company creation and onboarding wizard.
   - Trace:
     - where company creation fields are defined
     - how wizard progress/state is persisted
     - how the web UI binds form values
     - whether there is already a template/settings abstraction
   - Follow existing CQRS-lite and module boundaries.

2. **Design the template contract**
   - Introduce or extend a model for **industry/business setup templates**.
   - The model should support matching by:
     - industry
     - business type
     - optionally a combined match with fallback behavior
   - The template payload should contain recommended defaults for fields already supported by the company setup flow, likely:
     - timezone
     - currency
     - language
     - compliance region
     - optional settings/branding JSON fragments if appropriate
   - Prefer a data-driven representation such as persisted records or structured seed/config data rather than hardcoded switch statements.
   - Keep matching deterministic and simple:
     - exact combined match first
     - then industry-only or business-type-only fallback
     - then no recommendation

3. **Add backend retrieval logic**
   - Implement an application-layer query/service that accepts selected industry and business type and returns recommended defaults.
   - Keep this logic independent from UI concerns.
   - Return only safe recommendation data, not persisted company state changes.
   - If the architecture already uses repositories, add a repository abstraction and implementation for template lookup.
   - If seed/config data is used, load it through the existing infrastructure pattern.

4. **Seed a minimal template catalog**
   - Add a small but representative set of templates sufficient to demonstrate behavior.
   - Use examples aligned with the product domain, but keep them generic and maintainable.
   - Ensure the seed approach is versionable and consistent with repo conventions.
   - Do not over-engineer a management UI.

5. **Expose API/application contract**
   - Add or extend an endpoint/query used by the onboarding wizard to fetch recommendations when industry/business type changes.
   - Ensure request/response contracts are explicit and stable.
   - Include enough metadata for the UI to know which fields are recommended.

6. **Implement UI prefill behavior in Blazor**
   - Update the onboarding wizard so that when the user selects or changes industry/business type:
     - recommended defaults are fetched
     - empty/unmodified fields can be prefilled
     - manually edited fields are not overwritten automatically
   - Track field edit state in a lightweight way if needed.
   - If the current UX already has a review/apply step, integrate with that pattern instead of forcing auto-apply.
   - Make the behavior predictable:
     - first recommendation can populate blank fields
     - later template changes should not clobber user-entered values
   - Surface recommendations clearly but keep UX minimal.

7. **Persist final chosen values through existing company creation flow**
   - Ensure the final submitted company record still persists through the existing command/path.
   - Do not bypass validation.
   - Confirm the resulting `companies` record stores the selected/final values, not just the template recommendation.

8. **Validation and edge cases**
   - Handle missing or unknown industry/business type gracefully.
   - Handle no-match template results without errors.
   - Ensure null/empty recommendations do not clear existing values.
   - Preserve tenant-safe behavior; template lookup should not expose tenant-owned data unless the existing design intentionally supports tenant-specific templates.

9. **Testing**
   - Add unit/integration tests for:
     - template matching precedence
     - fallback behavior
     - no-match behavior
     - API/query response shape
     - onboarding prefill behavior not overwriting manually edited fields
   - Reuse existing test patterns and naming conventions.

10. **Keep implementation clean**
   - Avoid embedding business rules directly in Razor components if application services can own them.
   - Avoid hardcoded UI-only defaults that diverge from backend recommendations.
   - Keep the feature ready for future extensibility, such as DB-backed templates or admin-managed template catalogs.

# Validation steps
Run the relevant commands after implementation:

- `dotnet build`
- `dotnet test`

Also validate manually if the web app can be run locally:

1. Open the onboarding/company creation wizard.
2. Select an industry and/or business type with a known template.
3. Confirm recommended defaults populate supported fields.
4. Manually edit one or more fields.
5. Change industry/business type again.
6. Confirm manually edited fields are not overwritten unexpectedly.
7. Confirm blank untouched fields can still receive recommendations.
8. Submit the wizard and verify the created company persists the final selected values.
9. Test a no-template combination and confirm the wizard remains functional without errors.

If there are API tests or endpoint smoke checks in the repo, include them.

# Risks and follow-ups
- **Risk: overwriting user intent**
  - The biggest UX risk is clobbering manually entered values when recommendations refresh.
  - Mitigate by tracking touched/edited fields and only auto-filling untouched values.

- **Risk: hardcoded template logic**
  - Avoid large switch/case logic in UI or controllers.
  - Keep templates data-driven and centrally resolved.

- **Risk: duplicated defaults across layers**
  - Do not define one set of defaults in backend and another in Blazor.
  - Backend should be the source of truth for recommendations.

- **Risk: unclear precedence**
  - Document and test matching order, especially combined vs fallback templates.

- **Risk: future extensibility**
  - This task should not block later support for richer templates, tenant-specific templates, or admin-managed catalogs.
  - Keep contracts and persistence flexible.

Suggested follow-ups after this task:
- add richer template metadata and descriptions for UX
- support starter branding/settings bundles
- add admin/template management if needed
- connect template recommendations to downstream starter agents/workflows in later stories