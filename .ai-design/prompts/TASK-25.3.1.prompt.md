# Goal
Implement backlog task **TASK-25.3.1 — Implement reconciliation suggestion query endpoints with filtering and pagination** for story **US-25.3 Expose reconciliation suggestion review and accept/reject workflow APIs**.

Deliver production-ready ASP.NET Core API support for:
- querying **open reconciliation suggestions** with filtering and pagination
- **accepting** a reconciliation suggestion and returning the persisted reconciliation result with linked record identifiers
- **rejecting** a reconciliation suggestion and returning the updated suggestion status
- returning a **deterministic validation error** when accept is attempted on an already accepted or rejected suggestion
- adding **API integration tests** covering authorization, validation, idempotency/state-transition behavior, and response payloads

Use the existing solution structure and patterns already present in the repository. Prefer extending current modules, DTO conventions, endpoint registration style, authorization policies, result/error handling, and test fixtures rather than inventing new patterns.

# Scope
In scope:
- Add/complete API endpoints for reconciliation suggestion review workflow
- Add query filtering by:
  - tenant/company
  - entity type
  - status
  - minimum confidence
- Add pagination support to the query endpoint
- Ensure tenant isolation is enforced in all query and mutation paths
- Implement accept action behavior:
  - validate current suggestion state
  - persist reconciliation result
  - update suggestion state
  - return reconciliation result payload including linked record identifiers
- Implement reject action behavior:
  - validate current suggestion state
  - update suggestion status
  - return updated suggestion status payload
- Implement deterministic validation/error response for invalid accept transitions
- Add/extend integration tests for:
  - authorization
  - validation
  - idempotency/state transition behavior
  - response payloads

Out of scope unless required by existing code patterns:
- UI changes
- mobile changes
- unrelated refactors
- introducing new infrastructure patterns if existing ones already solve the problem
- broad redesign of reconciliation domain beyond what is necessary for this task

# Files to touch
Inspect the codebase first, then update the most relevant existing files. Likely areas include:

- `src/VirtualCompany.Api/**/*`
  - endpoint/controller definitions
  - request/response contracts if API-owned
  - authorization wiring
  - model binding / pagination contracts
- `src/VirtualCompany.Application/**/*`
  - query handlers/services for reconciliation suggestion listing
  - command handlers/services for accept/reject actions
  - validators
  - DTOs/read models
- `src/VirtualCompany.Domain/**/*`
  - reconciliation suggestion aggregate/entity behavior
  - status transition rules
  - reconciliation result model if domain-owned
- `src/VirtualCompany.Infrastructure/**/*`
  - EF Core/repository/query implementations
  - persistence mappings
  - pagination/filter query composition
- `tests/VirtualCompany.Api.Tests/**/*`
  - integration tests for query/accept/reject endpoints
  - fixture/seeding helpers if needed

Also inspect:
- existing reconciliation-related files
- approval/workflow/task endpoint patterns
- shared pagination/result envelope conventions
- existing error response format and validation problem details handling

# Implementation plan
1. **Discover existing reconciliation implementation**
   - Search for reconciliation-related domain models, statuses, endpoints, handlers, migrations, and tests.
   - Identify whether suggestion and reconciliation result entities already exist.
   - Reuse existing naming and module boundaries.
   - Confirm current authorization and tenant resolution approach.

2. **Map the API surface to acceptance criteria**
   - Ensure there is an endpoint for listing suggestions, ideally something like:
     - `GET /api/.../reconciliation-suggestions`
   - Ensure there are action endpoints for:
     - accept
     - reject
   - If endpoints already exist partially, complete them rather than duplicating.
   - Keep route design RESTful and consistent with existing API style.

3. **Implement query endpoint with filtering and pagination**
   - Add request parameters for:
     - tenant/company scope from authenticated context, not caller-supplied unless existing conventions require it
     - `entityType`
     - `status`
     - `minConfidence`
     - pagination fields such as `page`, `pageSize`, or `cursor` based on existing conventions
   - Default the query to **open** suggestions if that is the intended behavior from the acceptance criteria.
   - Ensure filtering is applied in the data access layer efficiently.
   - Return a paginated response using the project’s existing response envelope/read model pattern.
   - Include only tenant-scoped data.

4. **Implement accept command flow**
   - Load the suggestion by id within tenant scope.
   - Validate current status:
     - allow accept only from the valid open/pending state
     - reject invalid transitions deterministically
   - Persist:
     - updated suggestion status
     - reconciliation result record
     - linked record identifiers required by the acceptance criteria
   - Return the persisted reconciliation result DTO.
   - If the system already has audit/outbox hooks for business actions, use them if they are already part of the module pattern.

5. **Implement reject command flow**
   - Load the suggestion by id within tenant scope.
   - Validate current status transition.
   - Persist updated suggestion status.
   - Return a response containing the updated suggestion status and identifiers expected by current API conventions.

6. **Enforce deterministic validation behavior**
   - For accepting an already accepted or rejected suggestion:
     - return the project’s standard validation/business rule error response
     - keep the response deterministic in status code, error code/key, and message shape
   - Follow existing conventions for:
     - `400 Bad Request` vs `409 Conflict` vs validation problem details
   - Do not invent a new error format if one already exists.

7. **Add/adjust domain rules**
   - If status transitions are currently enforced in handlers only, move or centralize them in the domain entity/value object if consistent with current architecture.
   - Ensure statuses are explicit and not stringly-typed if the project already uses enums/value objects.
   - Preserve clean architecture boundaries.

8. **Implement persistence/query support**
   - Add repository/query methods or EF query composition for:
     - filtered suggestion listing
     - paginated results
     - loading suggestion with required related data for accept/reject
   - Ensure indexes or query shape are reasonable if schema already supports them.
   - Only add migrations if the task truly requires schema changes.

9. **Add integration tests**
   - Cover query endpoint:
     - authorized access returns tenant-scoped results
     - unauthorized/forbidden behavior
     - filters by entity type, status, min confidence
     - pagination metadata and page slicing
   - Cover accept endpoint:
     - success path returns persisted reconciliation result with linked record identifiers
     - invalid state transition for already accepted suggestion
     - invalid state transition for already rejected suggestion
     - authorization and tenant isolation
   - Cover reject endpoint:
     - success path returns updated suggestion status
     - authorization and tenant isolation
     - repeat/invalid transition behavior if existing business rules define it
   - Assert response payload shape, status codes, and deterministic error payloads.

10. **Keep implementation aligned with existing conventions**
   - Reuse existing DTO namespaces, endpoint registration style, MediatR/CQRS-lite patterns, validators, and test helpers.
   - Avoid broad refactors.
   - Update any API documentation comments/OpenAPI annotations only if the project already maintains them in code.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run API tests:
   - `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj`

3. If needed, run full test suite:
   - `dotnet test`

4. Manually verify the implemented behavior through tests and, if practical, local API execution:
   - query endpoint returns only tenant-scoped open suggestions by default
   - filters work for entity type, status, and minimum confidence
   - pagination works and response metadata is correct
   - accept persists and returns reconciliation result with linked record identifiers
   - reject updates and returns suggestion status
   - accept on already accepted/rejected suggestion returns deterministic validation error response
   - authorization failures behave consistently with the rest of the API

5. Before finishing, confirm:
   - no unrelated files were changed
   - nullability warnings or analyzer issues were not introduced
   - new code follows existing project patterns

# Risks and follow-ups
- The repository may already contain partial reconciliation workflow code with naming or route conventions that differ from the task wording; prefer repository conventions over inventing new ones.
- The exact deterministic error contract may already be standardized elsewhere in the API; match that format exactly.
- If reconciliation result persistence depends on schema not yet present, a migration may be required; only add it if unavoidable and keep it minimal.
- Pagination conventions may already be shared across modules; reuse them to avoid inconsistent API behavior.
- If authorization policies for reconciliation review are not yet defined, use the nearest existing approval/review policy pattern and note any gap.
- If reject idempotency behavior is not explicitly defined, do not guess beyond existing domain rules; implement only what current patterns support and cover with tests.
- If audit/outbox side effects are expected for accept/reject actions but not yet implemented in this module, note them as follow-up only unless clearly required by existing architecture or failing tests.