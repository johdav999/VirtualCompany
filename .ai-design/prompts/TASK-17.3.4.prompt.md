# Goal
Implement `TASK-17.3.4` by adding integration tests that verify transaction and invoice detail data mapping end-to-end for `US-17.3 ST-FUI-173`.

The tests must prove that the API/UI-facing detail models correctly map:
- transaction list and detail fields
- invoice list and detail fields
- linked document/workflow metadata when available
- clear non-blocking status messaging when linked resources are missing or inaccessible
- navigation/link presence only when the user has access

Keep the implementation aligned with the existing .NET modular monolith, tenant-scoped behavior, and current test conventions in `tests/VirtualCompany.Api.Tests`.

# Scope
In scope:
- Add integration tests for transaction detail mapping.
- Add integration tests for invoice detail mapping.
- Seed realistic tenant-scoped finance data needed by the tests.
- Verify list-to-detail behavior for the fields called out in acceptance criteria.
- Verify linked document metadata and access-dependent navigation behavior.
- Verify missing/inaccessible linked resource status messaging is surfaced without blocking detail rendering.

Out of scope:
- Building new finance workspace features unless required to make tests compile/run.
- Refactoring unrelated production code.
- Adding broad new test infrastructure unless a minimal reusable helper is clearly needed.
- Mobile-specific coverage.
- Performance/load testing.

Assumptions to validate from the codebase before changing anything:
- There is already an API surface or page model for transactions and invoices.
- There is an existing integration test host/factory pattern in `tests/VirtualCompany.Api.Tests`.
- Finance/workspace entities may already exist under Application/Infrastructure/Api layers, and tests should target those existing contracts rather than inventing new ones.

# Files to touch
Prefer the smallest possible set. Likely candidates:

- `tests/VirtualCompany.Api.Tests/...`  
  Add new integration test classes for:
  - transactions detail mapping
  - invoices detail mapping

- `tests/VirtualCompany.Api.Tests/.../Fixtures/...` or equivalent  
  Add/extend test data builders, seed helpers, or authenticated tenant setup helpers if needed.

- `tests/VirtualCompany.Api.Tests/.../TestWebApplicationFactory...` or equivalent  
  Only if required to register seed data or test auth/access patterns.

- Production files under:
  - `src/VirtualCompany.Api/...`
  - `src/VirtualCompany.Application/...`
  - `src/VirtualCompany.Infrastructure/...`
  
  Touch production code only if tests expose a real mapping gap or missing status field required by the acceptance criteria.

Before editing, inspect:
- existing finance/transactions/invoices endpoints
- DTO/view model mapping code
- existing integration test patterns for list/detail endpoints and authorization
- any document access policy handling already implemented

# Implementation plan
1. **Discover existing finance contracts and test patterns**
   - Search for transaction/invoice endpoints, queries, handlers, DTOs, and mapping profiles/manual mappers.
   - Search `tests/VirtualCompany.Api.Tests` for existing integration tests that:
     - seed tenant data
     - authenticate users
     - assert JSON response contracts
     - verify forbidden/not-found behavior for inaccessible resources
   - Reuse naming, fixture style, and assertion style already present in the repo.

2. **Identify the exact response models to validate**
   - Confirm list response fields for:
     - transactions: date/category/flagged state
     - invoices: status/amount/supplier
   - Confirm detail response fields for:
     - transactions: category, flags, anomaly state, linked document metadata
     - invoices: status, amount, supplier, linked workflow or approval context
   - Confirm how linked navigation is represented:
     - URL
     - route token/id
     - `canNavigate` boolean
     - status enum/message
   - If the contract differs from the wording in the backlog, test the actual implemented contract while ensuring it satisfies the acceptance criteria.

3. **Create focused integration test coverage for transactions**
   Add tests covering at minimum:
   - transaction list returns expected mapped summary fields
   - selecting/fetching transaction detail returns:
     - category
     - flags
     - anomaly state
     - linked document metadata when available
   - linked document is navigable when user has access
   - missing linked document returns clear non-blocking status message
   - inaccessible linked document returns clear non-blocking status message and no navigation affordance
   - tenant isolation is preserved for transaction detail and linked metadata

4. **Create focused integration test coverage for invoices**
   Add tests covering at minimum:
   - invoice list returns expected mapped summary fields
   - selecting/fetching invoice detail returns:
     - status
     - amount
     - supplier
     - linked workflow/approval context when available
   - linked workflow/approval navigation/context appears only when available and accessible
   - missing linked workflow/approval context returns clear non-blocking status message if that is part of the implemented contract
   - tenant isolation is preserved for invoice detail and linked context

5. **Seed realistic test data**
   Build deterministic fixtures for:
   - one company/tenant with accessible linked document
   - one company/tenant with missing linked document reference
   - one company/tenant with inaccessible linked document due to permissions/access scope
   - invoice records with linked workflow/approval context present and absent
   - optional second tenant to verify isolation
   Keep seed data minimal and explicit so assertions are easy to read.

6. **Assert mapping, not just status codes**
   For each test:
   - assert HTTP success where expected
   - deserialize into the actual response DTO
   - assert every relevant field value exactly
   - assert nullability/absence behavior explicitly
   - assert status message text or status code field is clear and non-blocking
   - assert navigation metadata is present only when access is allowed

7. **Only patch production code if tests reveal a real gap**
   If a required acceptance criterion is not represented in the current response:
   - make the smallest production change necessary
   - keep naming consistent with existing DTO conventions
   - avoid introducing speculative fields beyond what tests require
   - ensure tenant/access checks remain enforced in query/application layer, not only in tests

8. **Keep tests maintainable**
   - Prefer helper methods/builders for repeated setup.
   - Use descriptive test names in `Given_When_Then` or repo-standard style.
   - Group transaction and invoice tests separately.
   - Avoid brittle assertions on unrelated payload fields.

Suggested test matrix:

| Area | Scenario | Expected |
|---|---|---|
| Transactions list | records exist with filters-relevant fields | category/flagged/date fields map correctly |
| Transaction detail | linked document available and accessible | metadata present, navigation enabled |
| Transaction detail | linked document missing | detail still loads, clear status message, no navigation |
| Transaction detail | linked document inaccessible | detail still loads, clear status message, no navigation |
| Invoices list | records exist | status/amount/supplier map correctly |
| Invoice detail | linked workflow/approval context available | context fields map correctly |
| Invoice detail | linked context absent | detail still loads, context absent or status shown per contract |
| Multi-tenant | cross-tenant access attempt | forbidden/not found per existing API behavior |

# Validation steps
1. Inspect and follow existing test conventions:
   - `tests/VirtualCompany.Api.Tests`
   - any existing integration fixture/auth helpers

2. Run targeted tests during development:
   - `dotnet test --filter FullyQualifiedName~Transaction`
   - `dotnet test --filter FullyQualifiedName~Invoice`

3. Run the full API test project:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

4. If production code changed, run solution build:
   - `dotnet build VirtualCompany.sln`

5. Verify each acceptance criterion is covered by at least one integration assertion:
   - transactions list fields
   - transaction detail fields
   - invoices list fields
   - invoice detail fields
   - linked navigation when accessible
   - clear non-blocking message when missing/inaccessible

6. In the final change summary, include:
   - new test files added
   - scenarios covered
   - whether any production mapping changes were required

# Risks and follow-ups
- **Risk: finance endpoints/models may not yet exist or may be named differently.**  
  Mitigation: discover actual contracts first and adapt test names/locations to the implemented module structure.

- **Risk: linked document access rules may be enforced indirectly or not exposed in DTOs.**  
  Mitigation: test the externally visible contract only; if needed, add the smallest explicit status/navigation fields to support acceptance criteria.

- **Risk: brittle tests due to over-seeding or asserting incidental fields.**  
  Mitigation: keep fixtures minimal and assert only acceptance-critical mapping fields.

- **Risk: ambiguity between API integration tests and Blazor UI integration tests.**  
  Mitigation: default to API integration tests in `tests/VirtualCompany.Api.Tests` unless the repo already has established UI integration coverage for these pages.

- **Risk: missing non-blocking status message standardization.**  
  Mitigation: if messages are localized or centralized, assert stable status codes/flags plus message presence rather than hardcoding fragile full strings unless the repo convention supports exact text assertions.

Follow-ups after completion:
- Add filter behavior integration tests for transaction list date/category/flagged controls if not already covered elsewhere.
- Add authorization-focused tests for document navigation endpoints directly.
- Add UI-level coverage for rendering of linked-resource status messages in Blazor if the project has component/page test infrastructure.