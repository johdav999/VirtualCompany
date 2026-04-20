# Goal
Implement automated unit and integration tests for `TASK-20.1.4` covering finance seeding state classification and fallback behavior for `US-20.1 ST-FUI-409 — Detect finance seeding state for new and existing companies`.

The coding agent should add tests that prove:

- the system classifies companies as `not_seeded`, `partially_seeded`, or `fully_seeded`
- classification uses the shared detection service/endpoint intended for Finance UI, onboarding, and background jobs
- fallback logic works when metadata is missing or inconsistent with actual finance records
- all important inconsistent metadata/data combinations are covered
- request-time fast-path behavior is preserved by validating lightweight existence-check-oriented logic rather than full scans

Do not redesign the feature unless required to make it testable. Prefer testing the existing implementation with minimal production changes only where necessary to expose seams or stabilize behavior.

# Scope
In scope:

- discover the existing finance seeding detection implementation, including:
  - shared application service
  - API endpoint if present
  - any consumers in UI/onboarding/jobs
  - metadata source and lightweight existence checks
- add unit tests for classification rules
- add integration tests for end-to-end resolution through the shared service and/or HTTP endpoint
- cover acceptance-criteria scenarios, especially:
  - `not_seeded`
  - `partially_seeded`
  - `fully_seeded`
  - metadata present and consistent
  - metadata missing with finance records present
  - metadata inconsistent with actual data
  - fallback classification according to implemented rules
- add small production refactors only if needed for deterministic testing, dependency injection, or clearer rule boundaries

Out of scope:

- introducing new product behavior beyond what is necessary to satisfy the tests
- broad architecture changes
- performance benchmarking infrastructure
- unrelated finance onboarding/UI changes unless a test seam requires a tiny adjustment

# Files to touch
Inspect first, then update only the relevant files. Likely areas include:

- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Api/**`
- `tests/VirtualCompany.Api.Tests/**`

Potential file categories to touch:

- finance seeding detection service tests
- API integration tests for finance seeding state endpoint
- test fixtures/builders for company + finance seed metadata + finance records
- shared test helpers for tenant/company setup
- minimal production files if needed:
  - classification enum/value object definitions
  - detection service interface/implementation
  - endpoint/controller/query handler wiring
  - repository abstractions for metadata/existence checks

Before editing, identify the exact implementation files and keep the change set focused.

# Implementation plan
1. **Discover the current implementation**
   - Search the solution for terms like:
     - `seeded`
     - `seeding`
     - `finance seed`
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`
     - `onboarding`
     - `finance`
   - Identify:
     - the canonical classification logic
     - whether classification is implemented in domain, application, infrastructure, or API
     - whether there is already a shared service used by UI/onboarding/jobs
     - whether an endpoint exists and what contract it returns

2. **Map the implemented rules before writing tests**
   - Document in code comments or test names the actual implemented decision table.
   - Confirm how the system determines:
     - metadata says seeded / partially seeded / not seeded
     - metadata missing
     - finance records exist
     - finance records do not exist
     - inconsistent metadata vs data
   - If rules are ambiguous in code, infer from current implementation and acceptance criteria, then preserve behavior unless clearly incorrect.

3. **Add unit tests for classification logic**
   - Create focused unit tests around the smallest rule-bearing component.
   - Prefer table-driven tests if practical.
   - Cover at minimum:
     - no metadata + no finance records => `not_seeded`
     - metadata indicates not seeded + no finance records => `not_seeded`
     - metadata indicates fully seeded + expected supporting data exists => `fully_seeded`
     - metadata indicates partial seeded + partial supporting data exists => `partially_seeded`
     - metadata missing + some finance records exist => fallback is not `not_seeded`
     - metadata missing + finance records satisfy full rules => `fully_seeded`
     - metadata missing + finance records satisfy only partial rules => `partially_seeded`
     - metadata inconsistent with data where fallback should win or degrade classification according to implementation
   - If the implementation uses repository existence checks, mock/stub them and assert only lightweight boolean/count-threshold style interactions, not full collection enumeration.

4. **Add integration tests for shared resolution path**
   - Add integration tests in `tests/VirtualCompany.Api.Tests`.
   - Exercise the real application wiring through:
     - the HTTP endpoint, if one exists, and/or
     - the application query/service resolved from DI in a test host
   - Seed test data for a company and verify returned state.
   - Include tenant/company scoping in tests if applicable.
   - Ensure the same shared path is used rather than duplicating logic in tests.

5. **Cover inconsistent metadata/data combinations explicitly**
   - Add named tests for scenarios such as:
     - metadata missing, records exist
     - metadata says `not_seeded`, records exist
     - metadata says `fully_seeded`, records absent or incomplete
     - metadata says `partially_seeded`, records satisfy full criteria
   - Expected result should match implemented fallback rules.
   - If current behavior is surprising but intentional, encode it in tests with clear names.

6. **Stabilize testability with minimal refactoring if needed**
   - If classification logic is buried in handlers/controllers, extract a small pure classifier or rule method.
   - If infrastructure is hard to seed, add test builders/factories.
   - If endpoint contract is unclear, keep production changes minimal and avoid changing public API shape unless already required by the backlog.

7. **Ensure naming and assertions reflect acceptance criteria**
   - Test names should clearly describe:
     - input metadata state
     - input data existence state
     - expected classification
   - Prefer deterministic assertions on returned enum/string values:
     - `not_seeded`
     - `partially_seeded`
     - `fully_seeded`

8. **Keep fast-path intent visible**
   - Where possible, verify the detection path depends on metadata and lightweight existence checks.
   - Do not implement tests that require loading full finance datasets into memory.
   - If repository abstractions expose existence methods, assert those are used in unit tests.
   - If not directly assertable, at least avoid writing tests that normalize full scans as the expected behavior.

# Validation steps
1. Restore/build/test locally:
   - `dotnet build`
   - `dotnet test`

2. Run the targeted test project if needed:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Verify test coverage conceptually includes:
   - all three states
   - metadata-only path
   - data-fallback path
   - inconsistent metadata/data combinations
   - shared service/endpoint path

4. Confirm no existing tests were broken unintentionally.

5. In the final implementation summary, report:
   - exact files changed
   - discovered classification rule source
   - scenarios covered by unit tests
   - scenarios covered by integration tests
   - any minimal production refactors made for testability

# Risks and follow-ups
- **Risk: rule ambiguity**
  - The implemented fallback rules may not be obvious from backlog text alone.
  - Follow the existing implementation as source of truth unless a clear defect is found.

- **Risk: no clean shared seam exists**
  - If UI/onboarding/jobs each use separate logic, note this clearly.
  - Add tests around the canonical path you find, and flag duplication as follow-up technical debt.

- **Risk: integration test setup may be heavy**
  - Reuse existing API test host patterns and fixtures if present.
  - Avoid introducing brittle test infrastructure.

- **Risk: fast-path behavior may be hard to prove**
  - Prefer verifying existence-check abstractions in unit tests rather than trying to measure performance.

Follow-up items to note if encountered:

- consolidate duplicated finance seeding detection into one shared application service if not already done
- add explicit repository existence-check abstractions if current implementation hides scan behavior
- add endpoint contract tests for consumers beyond API tests if Finance UI or onboarding has separate client wrappers