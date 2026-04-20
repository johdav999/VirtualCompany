# Goal

Implement backlog task **TASK-20.1.3 — Expose seeding state through internal API used by finance UI and jobs** for story **US-20.1 ST-FUI-409 — Detect finance seeding state for new and existing companies**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that exposes a company finance seeding state with values:

- `not_seeded`
- `partially_seeded`
- `fully_seeded`

The implementation must provide a **shared detection service** and an **internal API surface** so that the **finance UI**, **onboarding flow**, and **background jobs** all use the same logic. Detection must be **fast-path/request-time safe**, rely on **metadata and lightweight existence checks**, and avoid full dataset scans.

# Scope

Implement the following:

1. **Domain/application contract for finance seeding state**
   - Add a canonical enum/value object or equivalent shared contract for the three states.
   - Add a result DTO that can include state plus supporting diagnostics/reason fields if useful internally.

2. **Shared finance seeding detection service**
   - Create an application-layer service responsible for resolving seeding state for a company.
   - Service must use:
     - finance seed metadata when present
     - lightweight existence checks against finance-related records when metadata is missing or inconsistent
   - Do not perform full table scans or expensive aggregate queries.

3. **Internal API endpoint**
   - Expose an internal endpoint in the API used by finance UI and jobs.
   - Endpoint should accept company context and return the resolved seeding state.
   - Keep it tenant-scoped and aligned with existing authorization/multi-tenant patterns.

4. **Fallback classification rules**
   - If metadata is missing but finance records exist, classify as `partially_seeded` or `fully_seeded` according to explicit implemented rules.
   - Make the rules deterministic and testable.
   - Prefer a small, documented rule set over implicit behavior.

5. **Shared usage path**
   - Ensure the implementation is reusable by:
     - finance UI
     - onboarding flow
     - background jobs
   - If direct call-site integration is too broad for this task, at minimum provide the shared service and endpoint and update the most relevant existing consumers or add TODO markers where integration points are not yet present.

6. **Automated tests**
   - Cover all three states.
   - Cover inconsistent metadata/data combinations.
   - Cover missing metadata with existing finance records.
   - Cover fast-path behavior at the unit/integration level as appropriate.

Out of scope unless required by existing code structure:

- Building new finance seed workflows
- Large UI redesigns
- Mobile-specific work
- Broad refactors unrelated to seeding detection

# Files to touch

Inspect the solution first, then update the most appropriate files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - Add canonical seeding state enum/value object if domain-owned.

- `src/VirtualCompany.Application/**`
  - Add query/service contract and implementation for finance seeding detection.
  - Add DTOs/models for response payload.
  - Add interfaces for metadata/existence-check repositories if needed.

- `src/VirtualCompany.Infrastructure/**`
  - Implement repository/data access for:
    - finance seed metadata lookup
    - lightweight finance record existence checks
  - Add efficient SQL/EF queries using indexed predicates and `Any`/`EXISTS` style access.

- `src/VirtualCompany.Api/**`
  - Add internal controller/endpoint for seeding state retrieval.
  - Wire DI registrations.
  - Apply tenant scoping and authorization consistent with existing internal APIs.

- `src/VirtualCompany.Shared/**`
  - Only if shared contracts are already placed here and that matches current conventions.

- `tests/VirtualCompany.Api.Tests/**`
  - Add endpoint/integration tests.

Potentially also:
- `tests/**Application*.Tests/**`
  - Add unit tests for classification logic if a dedicated application test project exists.
- `README.md` or relevant docs
  - Briefly document the endpoint and classification rules if the repo has API/internal docs.

Do not invent new project structure if the repo already has an established pattern. Follow existing conventions.

# Implementation plan

1. **Inspect current finance-related model and seeding artifacts**
   - Search the repo for:
     - `seed`
     - `seeding`
     - `finance`
     - onboarding/setup metadata
     - company settings/JSON metadata
     - internal APIs used by UI/jobs
   - Identify:
     - where company-level metadata is stored
     - what finance records exist that can act as lightweight indicators
     - whether there is already a setup status or onboarding progress model
     - existing CQRS/query patterns and endpoint style

2. **Define the canonical seeding state contract**
   - Introduce a single source of truth for the state values:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
   - Prefer enum + serialization mapping or a constrained string contract consistent with existing API style.
   - Ensure API responses serialize exactly to the required snake_case values.

3. **Define explicit classification rules**
   - Implement and document deterministic rules, for example:
     - `fully_seeded`
       - metadata explicitly indicates completed finance seeding, or
       - required finance record categories all exist according to lightweight checks
     - `partially_seeded`
       - metadata indicates in-progress/partial seeding, or
       - some but not all required finance indicators exist, or
       - metadata missing but finance records exist and full completion cannot be confirmed
     - `not_seeded`
       - metadata indicates not started, or
       - no metadata and no finance indicators exist
   - Adapt the exact “required finance record categories” to the actual schema found in the repo.
   - Keep checks limited to a small number of `EXISTS` queries or equivalent.

4. **Implement application-layer detection service**
   - Add a service such as `IFinanceSeedingStateService` with a method like:
     - `Task<FinanceSeedingStateResult> GetCompanyFinanceSeedingStateAsync(Guid companyId, CancellationToken ct)`
   - Service responsibilities:
     - load metadata
     - run lightweight existence checks only as needed
     - apply classification rules
     - return a stable result object
   - Keep business logic out of controllers and jobs.

5. **Implement infrastructure data access**
   - Add repository/query methods for:
     - finance seed metadata retrieval
     - existence checks for finance-related records
   - Use efficient query patterns:
     - `AnyAsync`
     - `SELECT EXISTS(...)`
     - indexed `company_id` filters
     - optional narrow predicates for seed-created records if available
   - Avoid:
     - `COUNT(*)` over large tables unless already optimized and necessary
     - loading full entities
     - scanning unrelated datasets

6. **Expose internal API endpoint**
   - Add an internal endpoint, e.g. under an internal/company/finance route consistent with current API conventions.
   - Response should minimally include:
     - `companyId`
     - `seedingState`
   - Optionally include:
     - `metadataState`
     - `derivedFrom`
     - `checkedAt`
   - Keep response compact and stable for request-time consumers.

7. **Wire shared usage**
   - Register the service in DI.
   - If existing finance UI/onboarding/job code paths already exist in repo, update them to call the shared service or endpoint rather than duplicating logic.
   - If those consumers are not yet implemented, ensure the service is clearly reusable and add concise comments/TODOs only where justified.

8. **Add tests for classification matrix**
   - Unit tests for service logic:
     - metadata says not seeded, no records => `not_seeded`
     - metadata says partial => `partially_seeded`
     - metadata says full => `fully_seeded`
     - metadata missing, no records => `not_seeded`
     - metadata missing, some records => `partially_seeded`
     - metadata missing, all required indicators present => `fully_seeded`
     - inconsistent metadata/data combinations resolve per rules
   - API tests:
     - endpoint returns expected serialized values
     - endpoint is tenant-scoped
     - unauthorized/invalid company access handled correctly

9. **Keep performance request-safe**
   - Ensure the service performs a bounded number of lightweight queries.
   - If appropriate and already idiomatic in the codebase, add short-lived caching for repeated request-path lookups, but only if simple and safe.
   - Do not introduce premature complexity.

10. **Document assumptions**
   - If the repo lacks explicit finance seed metadata schema, document the chosen source and fallback rules in code comments and/or a short doc note.
   - Make sure future developers can understand why a company is classified a certain way.

# Validation steps

1. **Codebase inspection**
   - Confirm the chosen metadata source and finance record indicators are real and appropriate in this repo.
   - Confirm no duplicate seeding-state logic remains in touched consumers.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Targeted verification**
   - Verify automated tests cover:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
     - missing metadata + existing records
     - inconsistent metadata/data combinations

5. **API verification**
   - Confirm endpoint response serializes exact values:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
   - Confirm tenant scoping/authorization behavior matches existing patterns.

6. **Performance sanity check**
   - Review generated queries/logs if available.
   - Ensure implementation uses bounded lightweight existence checks and no full dataset scans.

7. **Implementation summary**
   - In your final handoff, include:
     - files changed
     - classification rules implemented
     - metadata source used
     - finance indicators checked
     - any assumptions or follow-up gaps

# Risks and follow-ups

- **Schema ambiguity risk**
  - The backlog does not specify exact finance metadata fields or finance record tables.
  - Mitigation: inspect the repo and bind the implementation to existing structures rather than inventing broad new schema.

- **Consumer integration risk**
  - Finance UI, onboarding, and jobs may not all yet exist in this workspace.
  - Mitigation: implement the shared service and internal endpoint first; integrate actual consumers where present.

- **Performance risk**
  - Naive detection could become expensive if it uses counts or broad joins.
  - Mitigation: use metadata first, then a small number of `EXISTS` checks.

- **Inconsistent legacy data risk**
  - Existing companies may have records without metadata.
  - Mitigation: fallback rules must classify these deterministically and tests must cover them.

- **Serialization mismatch risk**
  - Enum serialization may default to unexpected casing.
  - Mitigation: explicitly test API output values.

Follow-ups to note if not completed in this task:
- Add/confirm DB indexes supporting the chosen existence checks.
- Add observability/metrics for seeding-state resolution latency and fallback frequency.
- Standardize any remaining finance seeding metadata writes so future reads rely less on fallback inference.