# Goal
Implement `TASK-7.1.2` for story `ST-101 Tenant-aware authentication and membership` so that every tenant-owned API request is resolved and enforced against a company context.

The coding agent should add or complete the backend plumbing required for company-scoped requests in the ASP.NET Core API, aligned with the shared-schema multi-tenant architecture where tenant-owned data is isolated by `company_id`.

Because the task acceptance criteria are implicit from the parent story, treat the effective target behavior as:

- authenticated users can operate only within companies they belong to
- every tenant-owned API request carries a resolved company context
- unauthorized cross-company access is rejected with `403` or `404` as appropriate
- membership role data is available for downstream authorization
- implementation should prefer policy-based ASP.NET Core authorization and avoid ad hoc controller checks where possible

# Scope
In scope:

- Inspect the current auth, membership, and API pipeline implementation
- Introduce a consistent company-context resolution mechanism for API requests
- Ensure tenant-owned endpoints require company context
- Resolve company context from a clear request source, preferably one of:
  - route value
  - header such as `X-Company-Id`
  - authenticated claim if the app already uses active-company selection
- Validate that the authenticated user has an active membership for the resolved company
- Make resolved company context available to application/services via an abstraction
- Ensure authorization failures are handled safely
- Update or add tests covering valid membership, missing company context, and cross-tenant access denial

Out of scope unless already partially implemented and necessary to complete this task:

- full SSO support
- UI company switcher
- invitation flows
- broad refactors unrelated to tenant scoping
- database row-level security
- redesign of the entire auth model

If the repository already contains a partial tenant-context implementation, extend and standardize it rather than replacing it wholesale.

# Files to touch
Inspect first, then modify only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Api/Program.cs`
- auth/authorization setup files under `src/VirtualCompany.Api`
- middleware, filters, or pipeline behaviors under `src/VirtualCompany.Api`
- current user / request context abstractions in:
  - `src/VirtualCompany.Application`
  - `src/VirtualCompany.Infrastructure`
  - `src/VirtualCompany.Shared`
- membership and company access query/services in:
  - `src/VirtualCompany.Application`
  - `src/VirtualCompany.Infrastructure`
- tenant-owned controllers/endpoints in `src/VirtualCompany.Api`
- EF Core entities/configurations/repositories if membership lookup support is missing
- test projects if present anywhere in the solution

Also inspect:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

# Implementation plan
1. **Discover the existing auth and request-context design**
   - Find how authentication is configured in the API.
   - Find whether there is already:
     - a current user abstraction
     - claims mapping
     - company membership entity/repository
     - authorization handlers/policies
     - MediatR pipeline behavior or endpoint filter for request context
   - Identify how tenant-owned endpoints are currently structured:
     - route-based company IDs
     - header-based company IDs
     - no company scoping yet

2. **Choose and standardize company context resolution**
   - Reuse the existing pattern if one already exists.
   - Otherwise implement a single canonical resolution strategy for API requests, with this preference order:
     1. explicit route/company parameter if endpoints are already nested by company
     2. `X-Company-Id` header for tenant-owned API requests
   - Do not silently infer company from “first membership” unless the codebase already intentionally models an active company selection.
   - Define a request-scoped abstraction such as `ICompanyContext` / `ITenantContext` exposing at minimum:
     - `CompanyId`
     - `UserId`
     - membership role/status if available
     - whether context is resolved

3. **Implement request pipeline enforcement**
   - Add middleware, endpoint filter, or equivalent request pipeline component in the API that:
     - runs after authentication
     - resolves company context for tenant-owned requests
     - validates the authenticated user is a member of that company
     - stores the resolved context in a scoped service for downstream use
   - Keep non-tenant endpoints excluded, such as:
     - health endpoints
     - auth bootstrap endpoints
     - possibly company creation endpoint if it must work before membership exists
   - Prefer an explicit allowlist/marker approach over brittle path string matching if feasible.

4. **Back company validation with membership lookup**
   - Use the `company_memberships` model described in the architecture/backlog.
   - Validate membership by:
     - matching authenticated user to membership
     - matching requested company
     - ensuring membership status is active/accepted as appropriate in the current domain model
   - If role/permissions are stored on membership, attach them to the resolved context for later authorization use.
   - Avoid duplicating membership queries in every controller.

5. **Integrate with authorization**
   - If the codebase already uses policy-based authorization, add a company-membership requirement/handler.
   - If not, introduce a minimal policy-based mechanism that can be applied to tenant-owned endpoints.
   - Ensure downstream handlers/services can rely on resolved company context rather than trusting client-supplied company IDs.
   - Where commands/queries accept `companyId`, ensure they are validated against the resolved context or derive from it.

6. **Harden tenant-owned data access**
   - Review repositories/query handlers for tenant-owned entities and ensure they filter by `company_id`.
   - For any endpoint that fetches tenant-owned resources by entity ID only, ensure access is constrained by the resolved company context.
   - Prefer returning:
     - `403` when company membership is invalid for the requested company context
     - `404` when hiding existence of another tenant’s resource is more appropriate for resource fetches
   - Follow existing API conventions in the repo if they already define this behavior.

7. **Add tests**
   - Add or update tests for:
     - authenticated user with valid membership can access tenant-owned endpoint
     - authenticated user without membership is denied
     - missing company context on tenant-owned endpoint is rejected
     - user cannot access another company’s resource
     - membership role is available in request context/authorization path
   - Prefer integration tests around the API pipeline if test infrastructure exists.
   - If only unit tests are practical, cover:
     - company context resolver
     - membership validator
     - authorization handler

8. **Keep implementation clean and minimal**
   - Use DI-friendly abstractions
   - Avoid leaking `HttpContext` into application layer
   - Keep tenant context in API/infrastructure boundary and expose only a clean application-facing interface
   - Preserve future compatibility with:
     - multi-membership users
     - policy-based authorization
     - mobile/web clients using the same API

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify behavior in code or existing test harness for:
   - tenant-owned endpoint without authentication returns unauthorized
   - tenant-owned endpoint with authentication but no company context returns bad request/forbidden per API conventions
   - tenant-owned endpoint with valid company context and active membership succeeds
   - tenant-owned endpoint with another company’s context is denied
   - resource lookup cannot cross tenant boundaries even if a valid foreign entity ID is supplied

4. Confirm DI/pipeline wiring:
   - company context service is scoped
   - middleware/filter runs after authentication
   - excluded endpoints still function

5. Confirm no obvious regressions:
   - health endpoints still work
   - company creation/onboarding endpoints still work if they are intentionally pre-membership
   - existing authorization policies still resolve

# Risks and follow-ups
- **Risk: ambiguous company resolution**
  - If the repo has no established active-company pattern, choosing header vs route may affect future API consistency.
  - Follow existing endpoint conventions first.

- **Risk: partial enforcement**
  - Adding middleware alone is not enough if repositories still query tenant-owned data without `company_id` filters.
  - Review data access paths for at least the currently exposed tenant-owned endpoints.

- **Risk: overblocking bootstrap endpoints**
  - Company creation, auth callback, and health endpoints may need to bypass tenant enforcement.
  - Be explicit about exclusions.

- **Risk: duplicated source of truth**
  - Do not trust both request `companyId` and arbitrary command payload `companyId` independently.
  - Prefer deriving application context from the resolved request company.

- **Risk: membership status semantics**
  - The domain may distinguish pending, invited, revoked, and active memberships.
  - Only active/accepted memberships should authorize tenant-owned requests unless existing rules say otherwise.

Follow-ups after this task, if not already covered elsewhere:

- add a formal active-company selection flow for multi-membership users
- add reusable authorization policies by membership role
- add structured logging enrichment with `company_id` and correlation ID
- add broader integration tests for all tenant-owned modules
- consider EF query filters or additional guardrails for tenant-owned aggregates