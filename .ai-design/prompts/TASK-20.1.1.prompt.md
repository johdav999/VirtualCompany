# Goal
Implement backlog task **TASK-20.1.1 — Implement finance seeding state resolver service with metadata and lightweight record checks** for story **US-20.1 ST-FUI-409 — Detect finance seeding state for new and existing companies**.

Deliver a shared, request-time-safe finance seeding detection capability in the .NET backend that classifies a company as exactly one of:

- `not_seeded`
- `partially_seeded`
- `fully_seeded`

The implementation must use **metadata and/or lightweight existence checks only**, with **no full dataset scans**, and must be reusable by **Finance UI**, **onboarding**, and **background jobs** through a shared application service and, if appropriate in the current architecture, a thin API endpoint/query handler.

# Scope
In scope:

- Add a domain/application-level finance seeding state model.
- Implement a shared resolver service that determines seeding state for a given `company_id`.
- Use fast-path detection based on:
  - finance seed metadata if present
  - lightweight existence/count-threshold checks against key finance records if metadata is absent or inconsistent
- Define and codify fallback rules for inconsistent states, especially:
  - metadata missing + records exist
  - metadata says seeded but records are missing/incomplete
- Add automated tests covering:
  - all three states
  - inconsistent metadata/data combinations
  - fast-path behavior assumptions
- Expose the resolver through the existing application architecture in a reusable way.

Out of scope unless required by existing code patterns:

- Large refactors of finance module architecture
- Full onboarding UX changes
- Full historical backfill/migration of all finance metadata for all companies
- Expensive reconciliation jobs or full scans
- Broad new finance seed orchestration logic beyond detection

Implementation constraints:

- Follow existing modular monolith / clean architecture boundaries.
- Keep tenant/company scoping explicit.
- Prefer CQRS-lite query/service patterns already used in the solution.
- Do not introduce direct UI-only logic; detection must live in shared backend application logic.
- Avoid full table scans; use indexed `EXISTS`, `LIMIT 1`, or similarly lightweight checks.

# Files to touch
Inspect the solution first and then update the most appropriate files. Likely areas include:

- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Api/**`
- `tests/VirtualCompany.Api.Tests/**`

Expected file categories to add or modify:

- Domain enum/value object for finance seeding state
- Application query/service interface and DTO/result model
- Infrastructure implementation using EF Core / SQL access patterns already present
- Optional API endpoint/controller/minimal API/query endpoint if the architecture already exposes similar read models
- Tests for resolver logic and endpoint behavior
- Possibly migration/entity configuration files if finance seed metadata storage does not already exist

Before coding, inspect for existing concepts such as:

- finance setup/onboarding metadata
- company settings JSON / metadata tables
- finance-related entities (accounts, chart of accounts, ledgers, tax settings, fiscal periods, invoices, bills, etc.)
- existing query handlers/services/endpoints for company setup state
- existing test fixtures and database integration test patterns

# Implementation plan
1. **Discover existing finance data model and metadata storage**
   - Search the codebase for:
     - finance module/entities
     - seeding/setup/onboarding metadata
     - company settings JSON or metadata tables
     - existing “state resolver” or “readiness/status” services
   - Identify the smallest reliable set of finance records that indicate seeded state without scanning large datasets.
   - Identify whether there is already a metadata field like:
     - seed version
     - seeded at
     - onboarding completed
     - finance setup completed
     - template applied

2. **Define the canonical state contract**
   - Add a domain-safe representation for the three allowed values:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
   - Prefer an enum plus serialization mapping to the exact snake_case strings required by acceptance criteria.
   - Add a result model that can optionally include lightweight diagnostics for internal use, such as:
     - metadata present?
     - metadata indicates complete?
     - key records found?
   - Keep external response minimal if exposing via API.

3. **Define deterministic resolution rules**
   - Implement explicit rules in priority order. Use something like:
     1. If metadata explicitly indicates finance seeding complete **and** required key records exist → `fully_seeded`
     2. If no metadata and no key records exist → `not_seeded`
     3. If some key records exist but required complete set is not satisfied → `partially_seeded`
     4. If metadata is missing but required complete set of key records exists → `fully_seeded`
     5. If metadata says complete but required records are absent/incomplete → `partially_seeded`
   - Adjust the exact “required complete set” to the actual finance model found in the repo.
   - Document the implemented rules in code comments and tests.

4. **Choose lightweight record checks**
   - Use only fast existence checks on a minimal set of finance tables.
   - Prefer checks such as:
     - `AnyAsync(...)`
     - indexed `EXISTS`
     - `Take(1)`
   - Avoid:
     - loading full collections
     - aggregate scans over large transactional tables
     - expensive joins unless indexed and bounded
   - Example pattern:
     - check for presence of finance configuration metadata
     - check for existence of foundational records such as chart of accounts / fiscal settings / tax config / opening balances / default categories, depending on actual schema
   - If “fully seeded” requires multiple foundational records, check each with separate lightweight existence queries or a compact projection query.

5. **Implement shared application service**
   - Add an interface in Application, e.g. `IFinanceSeedingStateResolver`.
   - Add a method like:
     - `Task<FinanceSeedingStateResult> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default);`
   - Ensure this service is reusable by:
     - Finance UI queries
     - onboarding flow
     - background jobs
   - Keep orchestration-free and side-effect-free.

6. **Implement infrastructure resolver**
   - Add the concrete implementation in Infrastructure.
   - Use existing DbContext/repositories/query abstractions.
   - Ensure all queries are tenant/company scoped.
   - If metadata is stored in JSONB/company settings, read only the needed fields.
   - If no metadata store exists, use existing company settings or finance setup tables if available; only add schema changes if truly necessary.

7. **Expose through a shared query/endpoint**
   - If the codebase uses MediatR/CQRS handlers, add a query and handler.
   - If the API already exposes company setup/readiness endpoints, add a finance seeding state endpoint there.
   - Keep the endpoint thin and delegate all logic to the shared resolver.
   - Return the exact state values required by acceptance criteria.

8. **Add tests**
   - Add unit and/or integration tests based on existing project patterns.
   - Cover at minimum:
     - metadata absent + no finance records => `not_seeded`
     - metadata absent + some finance records => `partially_seeded`
     - metadata absent + complete required finance records => `fully_seeded`
     - metadata complete + complete records => `fully_seeded`
     - metadata complete + missing required records => `partially_seeded`
     - metadata partial/incomplete + some records => `partially_seeded`
     - inconsistent combinations do not throw and always resolve to one of the three states
   - If endpoint added, include API tests for response contract.

9. **Keep performance request-time safe**
   - Verify the resolver performs a bounded number of lightweight queries.
   - Avoid N+1 patterns.
   - If useful and already idiomatic in the codebase, add short-lived caching keyed by company ID, but only if it does not complicate correctness.
   - Do not add caching unless needed; correctness and simplicity first.

10. **Document assumptions in code**
   - Add concise comments near the resolver describing:
     - what counts as “fully seeded”
     - why the chosen checks are lightweight
     - how inconsistent metadata/data is handled

# Validation steps
1. Inspect and understand current architecture and finance-related entities:
   - search solution for finance entities, setup metadata, and existing query patterns

2. Build after implementation:
   - `dotnet build`

3. Run tests:
   - `dotnet test`

4. Verify acceptance criteria explicitly:
   - resolver returns only `not_seeded`, `partially_seeded`, `fully_seeded`
   - shared service is used by application/query/endpoint layer rather than duplicating logic
   - detection uses metadata and lightweight existence checks only
   - inconsistent metadata/data combinations resolve deterministically
   - tests cover all three states and inconsistent combinations

5. If an API endpoint is added, verify response shape manually from tests or existing API conventions:
   - company-scoped request returns expected state
   - unauthorized cross-company access remains forbidden/not found per existing tenant rules

6. In code review, confirm no full scans were introduced:
   - no materialization of large finance tables
   - no unbounded counting over transactional datasets unless clearly indexed and tiny
   - use of `AnyAsync`/`EXISTS`-style checks

# Risks and follow-ups
- **Unknown existing finance schema**: the exact definition of “fully seeded” depends on what foundational finance records already exist. Choose the smallest reliable set and document it.
- **Metadata location may be unclear**: if finance seed metadata is not yet modeled, prefer reusing existing company settings/setup structures before adding new schema.
- **Inconsistent legacy data**: older companies may have records without metadata. The fallback rules must be conservative and deterministic.
- **Performance drift**: adding too many existence checks could still hurt request-time performance. Keep the check set minimal and bounded.
- **Shared usage adoption**: this task should centralize detection logic, but follow-up work may still be needed to update all Finance UI/onboarding/background job callers to use the shared resolver if they currently duplicate logic.
- **Potential follow-up task**: add or backfill finance seed metadata during actual seeding workflows so future detection becomes even cheaper and more reliable.