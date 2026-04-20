# Goal
Implement backlog task **TASK-19.2.3 — Render generation summary and validation result panels** for **US-19.2 ST-FUI-302 — Seed dataset generation and validation UI** in the Blazor web app.

Deliver a tenant-aware UI flow that:
- accepts **company**, **seed value**, **anchor date**, and **generation mode**
- submits those inputs to the seed generation API
- prevents duplicate submission while the request is in progress
- renders a **generation summary** after success, including **created** and **updated** record counts
- renders **validation results** returned by the backend, including **referential-integrity errors** and **warnings**
- shows **inline actionable error messages** for API failures and validation failures

Keep implementation aligned with the existing .NET modular monolith and Blazor Web App architecture. Prefer extending existing DTOs, page models, and reusable UI components over introducing parallel patterns.

# Scope
In scope:
- Blazor UI updates for the seed dataset generation page/form
- Form state management for:
  - selected company
  - seed value
  - anchor date
  - generation mode
  - submitting/loading state
- API integration from the web app to the existing backend endpoint for seed generation
- Rendering of:
  - success summary panel
  - validation results panel
  - inline error panel/message area
- Basic mapping of backend response payloads into UI view models if needed
- Tests covering form behavior and result rendering where the current solution patterns support it

Out of scope:
- Creating a new backend generation engine
- Redesigning the API contract unless required to match acceptance criteria
- Background job orchestration changes
- Mobile app changes
- Broad styling refactors unrelated to this page
- New persistence or schema changes unless the current API contract is missing required fields

# Files to touch
Inspect the solution first and update the actual files that already own this feature area. Likely candidates include:

- `src/VirtualCompany.Web/...` seed dataset generation page/component
- `src/VirtualCompany.Web/...` related view models or request/response models
- `src/VirtualCompany.Web/...` API client/service used by the page
- `src/VirtualCompany.Shared/...` shared DTOs for generation request/response, if shared contracts already exist
- `src/VirtualCompany.Api/...` endpoint contract or response mapping only if the UI cannot receive:
  - created count
  - updated count
  - validation errors
  - validation warnings
  - actionable failure messages
- `tests/VirtualCompany.Api.Tests/...` only if API contract tests need adjustment
- any existing web test project if present for component/page behavior

Do not invent new top-level folders unless the repository already uses them for this feature.

# Implementation plan
1. **Discover existing feature implementation**
   - Locate the current seed dataset generation UI, route, and API client.
   - Identify existing request/response DTOs and whether they already include:
     - company identifier
     - seed value
     - anchor date
     - generation mode
     - created/updated counts
     - validation errors/warnings
     - failure details
   - Reuse existing naming and layering conventions.

2. **Complete the form inputs**
   - Ensure the form exposes all required inputs:
     - company
     - seed value
     - anchor date
     - generation mode
   - Use Blazor form validation patterns already used in the app.
   - Add field-level validation where obvious:
     - required company
     - required seed value if contract requires it
     - required anchor date
     - required generation mode

3. **Wire submission to the API**
   - On submit, call the seed generation API with the selected parameters.
   - Introduce/confirm an `isSubmitting` or equivalent state flag.
   - Disable:
     - submit button
     - duplicate submit actions
   - Optionally disable mutable inputs during submission if consistent with current UX patterns.

4. **Handle success response**
   - Map the API response into a page/component state object.
   - Render a **generation summary panel** after success.
   - Include at minimum:
     - created record count
     - updated record count
   - If the backend returns additional summary metadata, render it only if already supported by the contract and useful.

5. **Handle validation results**
   - Render a separate **validation results panel** when validation data is returned.
   - Show:
     - referential-integrity errors
     - warnings
   - Distinguish severity visually and structurally:
     - errors section
     - warnings section
   - If validation failures are returned alongside a successful generation call, still show the summary and the validation panel together.

6. **Handle API and validation failures inline**
   - Show inline actionable error messages for:
     - transport/API failures
     - backend business/validation failures
   - Prefer user-facing wording such as:
     - what failed
     - what the user can do next
   - Preserve backend detail only when safe and useful.
   - Do not rely on browser alerts/toasts alone if acceptance requires inline display.

7. **Align contracts if necessary**
   - If the current API response does not expose required fields, minimally extend the shared/API DTOs and response mapping.
   - Keep changes backward-compatible where possible.
   - Do not add persistence changes unless absolutely necessary.

8. **Add tests**
   - Add or update tests for the implemented behavior using existing repo patterns.
   - Cover at least:
     - form submits expected payload
     - duplicate submission is prevented while request is in progress
     - success summary renders created/updated counts
     - validation errors/warnings render when returned
     - inline error message renders on API failure

9. **Keep implementation clean**
   - Prefer small reusable render fragments/components if the page is becoming large:
     - generation summary panel
     - validation results panel
     - inline error panel
   - But only extract components if it matches current project structure.

# Validation steps
Run the relevant checks after implementation:

1. Build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Open the seed dataset generation page
   - Confirm the form accepts:
     - company
     - seed value
     - anchor date
     - generation mode
   - Submit valid inputs and verify:
     - API is called once
     - submit action is disabled while request is in progress
     - generation summary appears with created/updated counts
   - Verify a response containing validation issues shows:
     - referential-integrity errors
     - warnings
   - Verify API failure path shows inline actionable error messaging
   - Verify a validation failure response also shows inline actionable messaging

4. If there are existing API contract tests or snapshot-style UI tests, update and rerun them.

# Risks and follow-ups
- The current backend contract may not yet expose structured validation results or separate created/updated counts. If so, minimally extend shared/API DTOs.
- The exact seed generation page may not exist yet or may be partially implemented under a different name; inspect before coding.
- If the app lacks a consistent inline error component, avoid overengineering and implement a simple accessible error panel consistent with existing styling.
- If generation mode is currently free-text or enum-backed differently across layers, normalize carefully to avoid breaking serialization.
- Follow-up tasks may be needed for:
  - richer validation result formatting
  - downloadable validation reports
  - preserving last-run results across navigation/refresh
  - audit/history of generation runs per company