# Goal
Implement backlog task **TASK-19.2.1 — Seed dataset generation form with parameter validation** for story **US-19.2 ST-FUI-302 — Seed dataset generation and validation UI** in the **Blazor Web App**.

Deliver a tenant-aware UI flow that:
- accepts **company**, **seed value**, **anchor date**, and **generation mode**
- validates inputs before submission
- calls the backend seed generation API with the selected parameters
- prevents duplicate submissions while the request is in progress
- displays a **generation summary** after success, including **created** and **updated** record counts
- displays backend-returned **validation results**, including **referential-integrity errors** and **warnings**
- shows **inline actionable errors** for API failures and validation failures

Keep the implementation aligned with the existing modular monolith architecture, CQRS-lite application style, and Blazor web-first UI approach.

# Scope
In scope:
- Add or complete the Blazor page/component for seed dataset generation
- Add a form model with client-side and server-side-friendly validation
- Wire the submit action to the existing or newly added typed API client/service
- Handle loading/submitting state to disable duplicate submissions
- Render success summary and validation results from backend response
- Render inline error states for:
  - invalid form input
  - backend validation failures
  - transport/server/API failures
- Add/update tests covering validation, submit behavior, and result rendering

Out of scope unless required by missing infrastructure:
- Designing a new backend domain workflow beyond the minimum contract needed by the UI
- Broad redesign of navigation/layout
- Mobile implementation
- Nonessential styling refactors
- New persistence model changes unless absolutely required to support the API contract already implied by the story

If the backend API contract does not yet exist, implement the smallest coherent contract necessary to satisfy the UI and acceptance criteria, using existing application/API patterns.

# Files to touch
Inspect the solution first and then touch only the minimal relevant files. Likely areas:

- `src/VirtualCompany.Web/**`
  - Blazor page for seed dataset generation
  - shared form/result components if appropriate
  - view models / UI DTOs
  - typed HTTP client or service wrapper
- `src/VirtualCompany.Shared/**`
  - shared request/response contracts if this repo uses shared DTOs between API and Web
- `src/VirtualCompany.Api/**`
  - endpoint/controller/minimal API for seed generation if missing
- `src/VirtualCompany.Application/**`
  - command/query handler or application service for seed generation if missing
  - validation objects if application-layer validation is the established pattern
- `tests/VirtualCompany.Api.Tests/**`
  - API tests for request validation / response shape if API work is needed
- Add web/UI tests in the appropriate test project if one exists for Blazor components; otherwise add focused API/service tests only if that matches the repo’s current testing setup

Before editing, identify the actual existing feature location and follow established naming, folder structure, and patterns.

# Implementation plan
1. **Discover existing implementation patterns**
   - Inspect `README.md`, project structure, and relevant web/API modules.
   - Find:
     - how Blazor pages are organized
     - whether forms use `EditForm`, data annotations, FluentValidation, or custom validators
     - how typed API clients are registered and used
     - whether there is already a seed dataset endpoint, DTO, or placeholder page
   - Reuse existing conventions for tenant/company context resolution.

2. **Locate or create the feature entry point**
   - Add or update the seed dataset generation page in `VirtualCompany.Web`.
   - Ensure the page is reachable from the intended admin/setup/testing area if routing already exists.
   - Keep the UX simple and operationally clear.

3. **Create the form model**
   - Add a request/form model with fields:
     - `CompanyId` or equivalent selected company identifier
     - `SeedValue`
     - `AnchorDate`
     - `GenerationMode`
   - Add validation rules such as:
     - company is required
     - seed value is required and within any expected numeric/string constraints
     - anchor date is required and valid
     - generation mode is required and restricted to supported values
   - Prefer existing validation mechanisms already used in the solution.
   - Surface field-level validation messages inline.

4. **Implement submit state management**
   - Add an `isSubmitting` or equivalent state flag.
   - Disable:
     - submit button
     - duplicate submit actions
     - optionally mutable inputs during submission if consistent with existing UX
   - Ensure rapid double-click or repeated enter key submission cannot trigger duplicate API calls.

5. **Wire the API call**
   - Use or add a typed client/service method such as `GenerateSeedDatasetAsync(request, cancellationToken)`.
   - Pass the selected parameters exactly from the form.
   - Preserve tenant/company scoping according to existing app patterns.
   - If no API exists, add the minimal end-to-end path:
     - request DTO
     - response DTO
     - API endpoint
     - application command/service
   - Keep contracts explicit and structured.

6. **Define/align response contract**
   - The UI needs a response shape that can represent:
     - generation summary
       - created record count
       - updated record count
       - optionally total processed count if already available
     - validation results
       - errors
       - warnings
       - referential-integrity issues
     - failure details
       - user-facing message
       - field-specific or operation-specific details where available
   - If backend already returns a different but equivalent shape, adapt in the web layer rather than forcing unnecessary backend changes.

7. **Render success state**
   - After a successful generation run, display a summary panel with at minimum:
     - created count
     - updated count
   - Keep the summary visible after submission completes.
   - If validation results are returned alongside success, render them directly below or adjacent to the summary.

8. **Render validation results**
   - Add a dedicated results section for backend validation output.
   - Clearly separate:
     - errors
     - warnings
     - referential-integrity errors
   - Use concise, actionable labels and messages.
   - If there are no validation issues, show either nothing or a small success indicator, depending on existing UX conventions.

9. **Render inline failure states**
   - For API failures and validation failures, show inline messages near the form/results area.
   - Messages should be actionable, for example:
     - retry guidance
     - correct invalid parameters
     - inspect referential-integrity issues
   - Avoid generic “something went wrong” unless no better detail exists.
   - Preserve prior successful results only if that matches expected UX; otherwise clear stale results on new submission.

10. **Handle edge cases**
   - Submission with invalid form should not call API.
   - Failed API call should re-enable submit.
   - Validation-failure response should still render returned details.
   - Null/partial response payloads should not crash the page.
   - Cancellation/navigation should not leave the component in a broken state.

11. **Add tests**
   - Add tests for the most important behavior using the repo’s existing test style:
     - invalid form blocks submission
     - valid submission calls API with selected parameters
     - duplicate submission is prevented while request is in progress
     - success response renders created/updated counts
     - validation results render errors/warnings/referential-integrity issues
     - API failure renders inline actionable error
   - If component tests are not set up, cover the API/service layer and keep UI logic small and deterministic.

12. **Keep implementation production-friendly**
   - Follow existing logging/error-handling patterns.
   - Do not introduce direct DB access from UI.
   - Keep the feature tenant-aware and modular.
   - Avoid overengineering; implement only what is needed for this task.

# Validation steps
Run the smallest relevant validation first, then broader checks.

1. **Build**
   - `dotnet build`

2. **Tests**
   - `dotnet test`

3. **Manual verification in the web app**
   - Open the seed dataset generation page.
   - Confirm the form shows inputs for:
     - company
     - seed value
     - anchor date
     - generation mode
   - Try submitting with empty/invalid values and verify inline validation appears and no API call is made.
   - Submit valid values and verify:
     - submit button is disabled while request is in progress
     - duplicate clicks do not trigger duplicate requests
   - On success, verify the UI shows:
     - created record count
     - updated record count
   - Verify validation results display when returned:
     - referential-integrity errors
     - warnings
   - Simulate or trigger API failure and verify inline actionable error messaging.
   - Confirm the form can be resubmitted after completion or failure.

4. **If API was added/changed**
   - Verify request/response contract consistency between Web and API.
   - Verify tenant/company scoping is preserved.
   - Verify invalid request payloads return safe, structured errors.

# Risks and follow-ups
- **Unknown existing API contract**: The backend may already expose a seed generation endpoint with a different shape. Prefer adapting in the web layer unless the contract is clearly incomplete for acceptance criteria.
- **Unknown company selection pattern**: The app may derive current company from tenant context rather than free selection. If so, satisfy the acceptance criteria by using the current company context or a selector consistent with existing admin UX.
- **Validation duplication**: Avoid conflicting validation rules across UI and API. Keep UI validation lightweight and rely on backend for authoritative validation.
- **Testing limitations**: If there is no Blazor component test setup, do not introduce a large new test framework unless necessary; use existing test infrastructure.
- **Long-running generation**: If the API is slow, current scope only requires in-progress duplicate prevention and result display, not background job orchestration.
- **Follow-up candidates**:
  - persist recent generation parameters
  - add richer result metrics by entity type
  - add downloadable validation report
  - add audit trail entry / history of generation runs
  - add cancellation support if generation becomes long-running