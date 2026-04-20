# Goal
Implement backlog task **TASK-19.2.2 — Integrate seed generation and validation API mutations** for **US-19.2 ST-FUI-302 — Seed dataset generation and validation UI**.

Deliver the UI-to-API integration for seed dataset generation so that:
- the form accepts **company**, **seed value**, **anchor date**, and **generation mode**
- submit triggers the **seed generation API mutation**
- duplicate submission is prevented while the request is in progress
- successful responses render a **generation summary** with **created** and **updated** counts
- returned validation results render inline, including **referential-integrity errors** and **warnings**
- API and validation failures are shown inline with actionable messaging

Use the existing solution structure and conventions in this repository. Prefer minimal, cohesive changes over broad refactors.

# Scope
In scope:
- Find the existing seed dataset generation UI page/component in `src/VirtualCompany.Web`
- Wire the form submit action to the backend API
- Add or complete request/response DTO usage in the web layer or shared contracts if needed
- Implement loading/submitting state to disable duplicate submissions
- Render success summary and validation results from the API response
- Render inline error states for:
  - transport/API failures
  - backend validation failures
  - generation failures returned as structured errors
- Add/update tests for the web component logic and any API client/service abstraction involved

Out of scope:
- Creating a brand-new backend generation engine if an API already exists
- Large redesign of the page layout or unrelated UX polish
- Mobile app changes
- Broad contract redesign unless required to match the existing backend
- New persistence or migration work unless absolutely necessary for missing API support

# Files to touch
Inspect first, then update only the relevant files. Likely candidates include:

- `src/VirtualCompany.Web/...` seed dataset page/component(s)
- `src/VirtualCompany.Web/...` API client/service classes used by the page
- `src/VirtualCompany.Shared/...` shared request/response contracts if the web and API share DTOs
- `src/VirtualCompany.Api/...` only if the mutation endpoint or contract mapping is missing/incomplete
- `tests/VirtualCompany.Api.Tests/...` if API contract coverage needs adjustment
- Any web test project or component test location if present in the repo

Before editing, identify:
- the page/component that owns the seed generation form
- the API endpoint or typed client for seed generation
- the response shape for generation summary and validation results
- existing patterns for form submission, inline validation, and loading states in Blazor

# Implementation plan
1. **Discover existing implementation**
   - Search for terms like:
     - `seed`
     - `dataset generation`
     - `validation`
     - `anchor date`
     - `generation mode`
   - Identify:
     - current UI form component
     - current API endpoint/controller
     - any existing service abstraction in Web for calling the API
     - shared DTOs or local view models

2. **Confirm or establish request contract**
   - Ensure the submit payload includes:
     - `CompanyId` or equivalent company selector value
     - `SeedValue`
     - `AnchorDate`
     - `GenerationMode`
   - If the backend already expects a different naming convention, align the web client to it rather than inventing a new contract.
   - If shared DTOs exist in `VirtualCompany.Shared`, reuse them.

3. **Confirm or establish response contract**
   - Ensure the response can represent:
     - generation success/failure
     - created record count
     - updated record count
     - validation errors
     - validation warnings
     - referential-integrity issues
     - actionable error messages
   - Prefer structured response handling over string parsing.
   - If the backend returns `ProblemDetails` or a standard error envelope, integrate with that pattern.

4. **Implement web API mutation integration**
   - In the Blazor web layer, wire form submission to the typed API client/service.
   - Use async submit handling.
   - Add an `isSubmitting` or equivalent flag.
   - Disable:
     - submit button
     - any duplicate-trigger action path
   - Prevent concurrent submissions from double-clicks or repeated Enter key submits.

5. **Implement form state and inline UX**
   - Ensure the form binds all required inputs:
     - company
     - seed value
     - anchor date
     - generation mode
   - Add client-side required validation where appropriate, but do not replace backend validation.
   - Clear stale success/error state on a new submission attempt.
   - Preserve user-entered values after failed submission.

6. **Render successful generation summary**
   - After a successful API response, display a summary section showing at minimum:
     - created count
     - updated count
   - If the response includes additional summary metadata, render only if it fits existing UI patterns.

7. **Render validation results**
   - After generation, display validation results inline.
   - Include separate rendering for:
     - errors
     - warnings
     - referential-integrity errors
   - Make the messages readable and actionable.
   - If there are no validation issues, show a concise success/clean validation state if consistent with current UX.

8. **Handle failures robustly**
   - For API/network/server failures:
     - show inline error banner/message near the form
     - include actionable text such as retry guidance
   - For backend validation failures:
     - map field-level issues to the form when possible
     - otherwise show a structured summary
   - For generation responses that succeed transport-wise but contain business/validation failure details:
     - render those details inline without treating them as silent success

9. **Add/update tests**
   - Add tests covering:
     - submit sends expected payload
     - submit button disabled during in-flight request
     - duplicate submit prevented
     - success summary renders created/updated counts
     - validation errors and warnings render
     - API failure renders inline error
   - Use existing test patterns in the repo; do not introduce a new test framework unless already present.

10. **Keep architecture aligned**
   - Respect modular boundaries:
     - UI in Web
     - contracts in Shared if already used that way
     - backend endpoint logic in Api/Application only if needed
   - Avoid direct DB or infrastructure concerns from the UI layer.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Open the seed dataset generation UI
   - Confirm the form accepts:
     - company
     - seed value
     - anchor date
     - generation mode
   - Submit valid input and verify:
     - only one request is sent
     - submit is disabled while loading
     - success summary shows created and updated counts
     - validation results display after generation
   - Trigger or simulate backend validation issues and verify:
     - referential-integrity errors render inline
     - warnings render inline
   - Trigger or simulate API failure and verify:
     - inline actionable error message appears
     - form remains usable for retry after failure

4. If there is an HTTP client abstraction or endpoint test coverage, verify payload/response mapping with tests or logs.

# Risks and follow-ups
- The exact API contract may differ from the acceptance criteria wording; inspect the existing backend before changing shared DTOs.
- There may already be a partial implementation with mismatched naming or incomplete response handling; prefer completing it over replacing it.
- If the backend does not yet return structured validation details, note that as a follow-up rather than inventing fragile client parsing.
- If company context is already tenant-resolved elsewhere in the app, avoid duplicating company selection logic unnecessarily.
- If no web-component test project exists, add the smallest practical automated coverage in the existing test structure and document any remaining manual verification.
- Follow-up candidates after this task:
  - richer field-level validation mapping
  - persisted run history
  - downloadable validation report
  - polling/progress UI if generation becomes long-running