# Goal
Implement backlog task **TASK-25.3.2** for story **US-25.3** by adding reconciliation suggestion review workflow APIs in the .NET backend:

- A query endpoint to return **open reconciliation suggestions** filtered by:
  - tenant/company
  - entity type
  - status
  - minimum confidence
- A command endpoint to **accept** a reconciliation suggestion:
  - validate tenant access and current state
  - perform deterministic state transition handling
  - persist the reconciliation result
  - return the persisted reconciliation result including linked record identifiers
- A command endpoint to **reject** a reconciliation suggestion:
  - validate tenant access and current state
  - persist the updated suggestion status
  - return the updated suggestion status
- Deterministic validation behavior when attempting to accept an already accepted or rejected suggestion
- API integration tests covering:
  - authorization
  - validation
  - idempotency/state-transition behavior
  - response payloads for accept/reject actions

Use the existing architecture and coding conventions in this repository. Keep the implementation tenant-scoped, CQRS-lite, and aligned with ASP.NET Core modular monolith patterns.

# Scope
In scope:

- Discover the existing reconciliation domain, entities, persistence mappings, API route conventions, auth patterns, and test infrastructure
- Add or extend:
  - query contract/handler/service for listing reconciliation suggestions
  - accept suggestion command contract/handler/service
  - reject suggestion command contract/handler/service
  - API endpoints/controllers/minimal API mappings
  - validation and deterministic error responses
  - persistence/state transition logic
  - integration tests in `tests/VirtualCompany.Api.Tests`
- Ensure tenant isolation via company/tenant context on all reads/writes
- Ensure accepted/rejected transitions are explicit and guarded

Out of scope unless required by existing code structure:

- New UI work in Blazor or MAUI
- Broad refactors unrelated to reconciliation workflow
- New background workers/outbox behavior unless already part of reconciliation acceptance flow
- Large schema redesigns beyond the minimum needed to support acceptance/rejection persistence
- Implementing unrelated approval workflows

# Files to touch
Start by locating the actual reconciliation module and then update the relevant files. Likely areas:

- `src/VirtualCompany.Api`
  - reconciliation endpoints/controllers
  - request/response DTOs
  - endpoint registration
  - error mapping / problem details behavior
- `src/VirtualCompany.Application`
  - queries/commands for reconciliation suggestions
  - validators
  - handlers/services
  - result models
- `src/VirtualCompany.Domain`
  - reconciliation suggestion aggregate/entity
  - status enum/value objects
  - domain methods for accept/reject transitions
  - reconciliation result entity if present
- `src/VirtualCompany.Infrastructure`
  - EF Core configurations/repositories
  - persistence logic for suggestion/result updates
  - migrations only if strictly necessary
- `tests/VirtualCompany.Api.Tests`
  - integration tests for:
    - list suggestions
    - accept suggestion
    - reject suggestion
    - invalid repeated transitions
    - authorization/tenant isolation

Before editing, identify the concrete files/classes already used for:
- tenant resolution
- authorization policies
- API route grouping
- reconciliation persistence
- integration test fixtures and authenticated request helpers

# Implementation plan
1. **Inspect existing reconciliation implementation**
   - Search for terms like:
     - `Reconciliation`
     - `Suggestion`
     - `Accept`
     - `Reject`
     - `Confidence`
     - `EntityType`
   - Determine:
     - whether reconciliation suggestions already exist as entities/tables
     - current statuses and allowed transitions
     - whether a reconciliation result entity/table already exists
     - current API style: controllers vs minimal APIs
     - how tenant/company context is resolved in requests
     - how deterministic validation errors are represented today

2. **Model or confirm the workflow state machine**
   - Ensure suggestion statuses are explicit, likely something like:
     - `Open`
     - `Accepted`
     - `Rejected`
   - Add domain methods if missing, e.g.:
     - `Accept(...)`
     - `Reject(...)`
   - Enforce transition rules in one place, preferably domain/application layer:
     - `Open -> Accepted` allowed
     - `Open -> Rejected` allowed
     - `Accepted -> Accept/Reject` invalid
     - `Rejected -> Accept/Reject` invalid
   - Return deterministic validation failures for invalid transitions, not ambiguous 500s or silent no-ops

3. **Implement query endpoint for open suggestions**
   - Add a query and handler to fetch suggestions scoped to the current tenant/company
   - Support filters from acceptance criteria:
     - entity type
     - status
     - minimum confidence
   - Clarify behavior for “open suggestions” plus status filter:
     - if the product/API contract expects only open suggestions, default status to open and optionally allow explicit status filtering if already supported
     - keep behavior consistent and documented in code/tests
   - Return stable response DTOs with the fields needed by clients

4. **Implement accept workflow**
   - Add request DTO/command for accepting a suggestion
   - Load suggestion by:
     - suggestion id
     - tenant/company id
   - Validate existence and authorization
   - Validate current state before mutation
   - Persist:
     - suggestion status change to accepted
     - reconciliation result record if the model requires a separate persisted result
     - linked record identifiers in the result payload
   - Return the persisted reconciliation result DTO

5. **Implement reject workflow**
   - Add request DTO/command for rejecting a suggestion
   - Load suggestion by id + tenant/company id
   - Validate existence and authorization
   - Validate current state before mutation
   - Persist updated suggestion status as rejected
   - Return a response DTO containing at least:
     - suggestion id
     - updated status
     - timestamps / metadata if consistent with existing API patterns

6. **Wire API endpoints**
   - Add routes following existing API conventions, likely something like:
     - `GET /api/reconciliation/suggestions`
     - `POST /api/reconciliation/suggestions/{id}/accept`
     - `POST /api/reconciliation/suggestions/{id}/reject`
   - Reuse existing auth attributes/policies and tenant resolution mechanisms
   - Ensure route handlers/controllers delegate to application layer rather than embedding business logic

7. **Implement deterministic validation/error mapping**
   - For already accepted/rejected suggestions, return a deterministic validation response:
     - use the project’s existing validation/problem details format
     - likely `400 Bad Request`, `409 Conflict`, or domain validation mapping depending on current conventions
   - Be consistent across accept and reject endpoints
   - Include machine-usable error code/message if the codebase already supports it

8. **Add integration tests**
   - Follow existing API test patterns and fixtures
   - Cover:
     - authorized tenant can list suggestions
     - filters work for entity type, status, minimum confidence
     - unauthorized/forbidden access is rejected
     - tenant isolation prevents cross-tenant access
     - accept returns persisted reconciliation result with linked record identifiers
     - reject returns updated suggestion status
     - accepting already accepted suggestion returns deterministic validation error
     - accepting already rejected suggestion returns deterministic validation error
     - optionally rejecting already rejected/accepted suggestion if current domain rules require deterministic handling there too
   - Verify response payload shapes, not just status codes

9. **Keep persistence and transaction boundaries safe**
   - If accept creates both a result and a status update, ensure they are committed atomically
   - Reuse existing DbContext/repository patterns
   - Avoid duplicate result creation on invalid repeated requests

10. **Document assumptions in code comments only where necessary**
   - Do not add broad documentation files unless the repo already expects them for API changes
   - Keep comments minimal and focused on non-obvious state transition rules

# Validation steps
1. Restore/build and run tests:
   - `dotnet build`
   - `dotnet test`

2. Run targeted test project if needed:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. Verify integration scenarios:
   - list endpoint returns tenant-scoped suggestions only
   - filters behave correctly
   - accept endpoint returns persisted reconciliation result with linked identifiers
   - reject endpoint returns updated status
   - repeated accept on accepted/rejected suggestion returns deterministic validation error
   - auth and tenant isolation tests pass

4. If migrations are required, ensure they are minimal and validated against existing infrastructure patterns. Only add them if the current schema cannot support the acceptance criteria.

# Risks and follow-ups
- **Risk: reconciliation domain may already partially exist with different naming**
  - Mitigation: inspect existing modules first and extend rather than duplicate
- **Risk: acceptance criteria mention idempotency behavior while also requiring deterministic validation on repeated accept**
  - Treat repeated accept/reject as deterministic, tested state-transition validation according to current domain rules; do not silently succeed unless the existing API contract explicitly requires that
- **Risk: unclear response contract for “persisted reconciliation result”**
  - Reuse existing DTO/entity shape if present; otherwise return a concise result model including result id, suggestion id, tenant/company id if standard, and linked record identifiers
- **Risk: tenant resolution/auth patterns may be centralized**
  - Follow existing policy and tenant-context mechanisms exactly; do not invent a parallel approach
- **Risk: schema gaps**
  - If linked record identifiers or result persistence are not modeled yet, add the smallest viable persistence change and corresponding tests

Follow-up items to note if discovered but not required for this task:
- audit event creation for accept/reject actions
- optimistic concurrency tokens for stronger race-condition handling
- pagination/sorting for suggestion listing if not already implemented
- richer error codes for client-side workflow UX