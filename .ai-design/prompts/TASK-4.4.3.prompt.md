# Goal
Implement **TASK-4.4.3 — Add request validation and error handling for invalid preference payloads** for **US-4.4 Support personalized briefing preferences and delivery controls**.

The coding agent should add robust API-side validation and consistent 4xx error handling for invalid briefing preference payloads, specifically for:
- unsupported `focusAreas`
- invalid `deliveryFrequency`

This work must preserve the broader story behavior:
- authenticated users can save briefing preferences
- saved preferences are used by briefing generation
- tenant defaults apply when user preferences do not exist
- preference changes affect the next generated briefing without restart/cache clearing
- invalid payloads are rejected with descriptive error codes

# Scope
In scope:
- Locate the existing briefing preferences API, command/handler, DTOs, validators, and any briefing generation preference resolution flow.
- Add or tighten request validation for preference save/update endpoints.
- Ensure invalid payloads return **4xx** responses, preferably **400 Bad Request** or **422 Unprocessable Entity** depending on existing API conventions.
- Return structured, descriptive error codes/messages for:
  - unsupported focus areas
  - invalid delivery frequencies
  - malformed or missing required fields if applicable under current contract
- Add/update tests covering API validation and error response shape.
- Confirm valid preference changes are persisted and visible to the next briefing generation path without restart/manual cache invalidation.
- If tenant defaults are already implemented, ensure validation does not break fallback behavior and that default usage recording still works.

Out of scope unless required by existing code structure:
- redesigning the preferences domain model
- introducing a new preferences feature from scratch if it already exists
- changing briefing generation logic beyond what is needed to consume validated values
- adding new delivery channels beyond current story scope
- broad exception-handling refactors unrelated to preference payload validation

# Files to touch
Likely areas to inspect and update, based on the solution structure:

- `src/VirtualCompany.Api/**`
  - briefing preferences controller/endpoints
  - request/response contracts
  - API exception/error mapping
  - authentication/authorization attributes if missing on preference endpoints

- `src/VirtualCompany.Application/**`
  - commands/handlers for saving briefing preferences
  - validators (FluentValidation or custom validation)
  - application error/result types
  - briefing preference resolution services used during generation

- `src/VirtualCompany.Domain/**`
  - enums/value objects/constants for valid focus areas and delivery frequencies
  - domain validation helpers if validation belongs here

- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings if enum/string normalization is needed
  - repository implementations for preferences/defaults retrieval

- `src/VirtualCompany.Shared/**`
  - shared contracts or error code definitions if centralized here

- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests for invalid payloads and expected 4xx responses
  - authenticated request coverage
  - response body assertions for descriptive error codes

Also inspect:
- `README.md`
- any architecture/docs files describing API error conventions
- existing test helpers/fixtures for authenticated tenant-scoped API calls

# Implementation plan
1. **Discover the existing implementation**
   - Find the user briefing preferences endpoint(s), likely under a communication/briefing/preferences route.
   - Identify:
     - request DTO shape
     - persistence model
     - command/handler path
     - current validation behavior
     - current error response format
     - how briefing generation resolves user preferences vs tenant defaults
   - Reuse existing conventions rather than inventing a parallel validation/error model.

2. **Identify the valid preference vocabulary**
   - Determine the canonical allowed values for:
     - `deliveryFrequency`
     - `focusAreas`
     - `priorityThreshold` constraints if already modeled
   - Prefer centralizing valid values in one place:
     - enum
     - static constants
     - value object factory/parser
   - Avoid duplicated string lists across controller, validator, and generator.

3. **Add request validation**
   - Implement validation at the API/application boundary.
   - Validate at minimum:
     - `deliveryFrequency` is one of the supported values
     - every `focusArea` is supported
   - Also validate if appropriate under current contract:
     - null/empty payload
     - duplicate focus areas
     - invalid threshold range
     - empty strings / whitespace values
   - If the project uses FluentValidation, add/update a validator and wire it into the request pipeline.
   - If not, use the existing command validation/result pattern.

4. **Return descriptive 4xx errors**
   - Ensure invalid payloads do **not** bubble into 500 responses.
   - Map validation failures to the project’s standard client error response shape.
   - Include stable machine-readable error codes, for example:
     - `briefing_preferences.invalid_delivery_frequency`
     - `briefing_preferences.unsupported_focus_area`
     - optionally field-level details per invalid item
   - Keep messages descriptive but concise.
   - If the API already uses `ProblemDetails` or `ValidationProblemDetails`, extend it consistently rather than replacing it.

5. **Preserve authenticated tenant-scoped behavior**
   - Confirm the preference save endpoint remains authenticated and tenant-scoped.
   - Ensure validation runs before persistence and before any downstream generation side effects.
   - Do not allow invalid values to be stored for one tenant/user and later break generation.

6. **Verify generation uses validated preferences**
   - Inspect the briefing generation path.
   - Confirm saved preferences are read fresh enough that the next generated briefing reflects changes without restart/manual cache invalidation.
   - If a cache exists, ensure either:
     - it is not used for this path, or
     - it is invalidated/updated on preference save
   - Keep changes minimal and aligned with existing architecture.

7. **Protect tenant-default fallback**
   - If no user preference exists, confirm tenant defaults still apply.
   - Ensure validation only applies to incoming user payloads, not to reading existing defaults unless defaults are also user-editable through the same contract.
   - Preserve any recording/audit of which defaults were used.

8. **Add tests**
   - Add API tests for:
     - valid authenticated save request succeeds
     - invalid `deliveryFrequency` returns expected 4xx + error code
     - unsupported `focusAreas` returns expected 4xx + error code
     - mixed valid/invalid focus areas fail cleanly
     - unauthenticated request is rejected per existing auth behavior
   - If there are application-layer tests, add validator/handler tests too.
   - If feasible, add a regression test showing a saved preference change affects the next briefing generation request.

9. **Keep implementation idiomatic**
   - Follow existing naming, folder structure, and error handling patterns.
   - Do not introduce broad abstractions unless clearly justified by current codebase conventions.
   - Prefer small, focused changes with strong tests.

# Validation steps
Run and verify at minimum:

1. Build:
   - `dotnet build`

2. Tests:
   - `dotnet test`

3. Manual/API verification if integration tests are limited:
   - Send authenticated valid request to save preferences and confirm success.
   - Send invalid `deliveryFrequency` and confirm:
     - 4xx status
     - descriptive error code in body
     - no persistence occurs
   - Send unsupported `focusAreas` and confirm:
     - 4xx status
     - descriptive error code in body
     - invalid values are not persisted
   - Save valid preferences, trigger or simulate next briefing generation, and confirm:
     - excluded focus areas are filtered out
     - sections below priority threshold are filtered out
   - Remove/avoid user preferences and confirm tenant defaults are applied and default usage recording still occurs.

4. If the API has a standard error contract:
   - verify response shape matches existing conventions exactly
   - verify field-level validation details are included where expected

# Risks and follow-ups
- **Risk: unclear existing API error convention**
  - Follow the current project pattern for validation and `ProblemDetails`/result mapping.
  - Do not invent a new response schema unless absolutely necessary.

- **Risk: valid values are duplicated in multiple layers**
  - Centralize supported `focusAreas` and `deliveryFrequency` definitions to avoid drift.

- **Risk: cached preference resolution**
  - If briefing generation uses caching, preference updates may not affect the next run.
  - Add targeted invalidation or bypass stale cache for this path.

- **Risk: tenant defaults may contain legacy invalid values**
  - If discovered, document separately rather than expanding this task unless required to prevent runtime failures.

- **Risk: acceptance criteria span more than validation**
  - This task is specifically about request validation/error handling, but verify adjacent behavior is not regressed.

Follow-ups to note if not already covered:
- add shared API documentation/OpenAPI examples for validation failures
- add centralized error code constants if currently stringly typed
- add tests for duplicate focus areas and threshold boundary validation if the contract supports those fields