# Goal

Implement backlog task **TASK-7.1.3 — Unauthorized access to another company’s data returns forbidden/not found** for story **ST-101 Tenant-aware authentication and membership** in the existing .NET solution.

The coding agent should ensure that tenant-owned API access is consistently scoped by the active company context and that attempts to access resources belonging to a different company do **not** leak cross-tenant existence or data. The result should be a secure, test-covered implementation aligned with the architecture’s shared-schema multi-tenancy model using `company_id` enforcement and ASP.NET Core policy-based authorization patterns.

# Scope

In scope:

- Review the current authentication, tenant resolution, membership, and authorization flow across API/application/infrastructure layers.
- Identify tenant-owned entities/endpoints already present and the current company context resolution mechanism.
- Implement or tighten enforcement so that:
  - requests are evaluated against the caller’s active company membership,
  - tenant-owned queries are filtered by `company_id`,
  - cross-tenant access attempts return **403 Forbidden** or **404 Not Found** consistently per the existing API style, with preference for **not leaking existence** where appropriate.
- Add or refine reusable abstractions for:
  - current company context resolution,
  - membership lookup,
  - tenant-scoped query access,
  - authorization/policy helpers if needed.
- Update relevant API endpoints, handlers, repositories, or query services that currently fetch tenant-owned records without guaranteed company scoping.
- Add automated tests covering authorized access, unauthorized same-user cross-company access, and unauthenticated/no-membership cases as applicable.

Out of scope unless required by existing code structure:

- Building full SSO support.
- Reworking the entire auth stack.
- Adding new product features unrelated to tenant isolation.
- Large refactors outside the minimum needed to enforce tenant-safe access.
- UI redesigns.

If the codebase already has a tenant enforcement pattern, extend it rather than introducing a parallel approach.

# Files to touch

Start by inspecting these files/projects and then touch the minimum necessary set:

- `README.md`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Domain/VirtualCompany.Domain.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

Likely areas to modify after inspection:

- API startup/configuration:
  - `src/VirtualCompany.Api/Program.cs`
  - auth/authorization configuration files under `src/VirtualCompany.Api`
  - middleware, filters, or endpoint mapping files
- Application layer:
  - current user / tenant context abstractions
  - query/command handlers for tenant-owned resources
  - authorization services/policies
- Infrastructure layer:
  - EF Core DbContext query patterns
  - repository implementations
  - membership lookup services
- Domain/shared contracts if needed:
  - membership/role models
  - tenant-scoped entity interfaces or base types
- Tests:
  - API integration tests
  - application-layer unit tests
  - infrastructure tests if present

Do not invent file paths blindly. First inspect the solution structure and follow existing conventions.

# Implementation plan

1. **Inspect the current tenant/auth design**
   - Find how authentication is configured in the API.
   - Find how the current user is represented in application code.
   - Find whether there is already:
     - a current company accessor,
     - membership entity/service,
     - authorization policies,
     - tenant-scoped repository/query pattern,
     - exception-to-HTTP mapping.
   - Identify existing tenant-owned entities/endpoints already implemented.

2. **Define the enforcement strategy based on existing patterns**
   - Prefer a single reusable mechanism over endpoint-by-endpoint ad hoc checks.
   - Align with the architecture:
     - shared schema,
     - `company_id` on tenant-owned tables,
     - policy-based authorization in ASP.NET Core,
     - application/repository layer enforcement.
   - Use default-deny behavior when company context or membership is missing.

3. **Implement active company context resolution**
   - Ensure each tenant-owned request resolves an active company context from the existing request/auth model.
   - If the app already supports multiple memberships, ensure the selected company is validated against the authenticated user’s memberships.
   - If no valid membership exists for the requested company, fail safely.

4. **Enforce membership before data access**
   - Add or refine a reusable authorization check such as:
     - “user is a member of active company”
     - “resource belongs to active company”
   - Prefer checking membership before executing tenant-owned operations.
   - Ensure role data remains available for downstream authorization checks.

5. **Enforce `company_id` filtering in data access**
   - For all relevant tenant-owned queries in scope, ensure reads are filtered by the active company ID.
   - Avoid patterns like:
     - fetch by entity ID only, then inspect later,
     - returning entities before tenant validation.
   - Prefer queries like `WHERE id = ? AND company_id = activeCompanyId`.
   - For list endpoints, ensure all results are scoped to the active company.

6. **Return forbidden/not found safely**
   - Follow existing API conventions if already established.
   - If no convention exists:
     - use **401** for unauthenticated requests,
     - use **403** when the user is authenticated but lacks valid membership/context for the requested tenant operation,
     - use **404** when accessing a specific tenant-owned resource by ID and the resource is outside the active tenant scope, to avoid leaking existence.
   - Keep behavior consistent across endpoints in scope.
   - Reuse centralized exception handling if present.

7. **Add tests**
   - Add integration tests for representative tenant-owned endpoints.
   - Cover at minimum:
     - authenticated user accessing own company data succeeds,
     - authenticated user with membership in company A cannot access company B resource by ID,
     - authenticated user without required company membership is rejected,
     - unauthenticated request is rejected,
     - list/query endpoints do not return records from another company.
   - If the codebase uses seeded test data, create at least:
     - user with membership in company A,
     - user with membership in company B,
     - resource in company A,
     - resource in company B,
     - optionally a multi-company user if supported.

8. **Keep the implementation small and idiomatic**
   - Reuse existing abstractions and naming.
   - Avoid introducing speculative frameworks.
   - Document any assumptions in code comments only where necessary.

# Validation steps

Run the relevant validation locally after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If API integration tests exist, ensure they specifically validate tenant isolation behavior.

4. Manually verify, if practical from the existing test harness or API project:
   - request to a tenant-owned endpoint with valid membership returns success,
   - request to another company’s resource returns the expected forbidden/not found response,
   - response body does not leak whether the foreign resource exists.

5. Confirm no obvious regressions in auth startup/configuration and no broken DI registrations.

# Risks and follow-ups

- **Risk: inconsistent endpoint behavior**
  - Some endpoints may use handlers/repositories that already scope by company while others do not. Be careful to standardize behavior for the touched surface area.

- **Risk: leaking resource existence**
  - Returning 403 for resource-by-ID lookups can reveal that a record exists in another tenant. Prefer 404 for direct resource fetches if that matches current API style.

- **Risk: missing active company selection model**
  - If the codebase has authentication but no finalized active-company selection mechanism, implement the smallest viable pattern consistent with existing claims/headers/routes and note any assumptions.

- **Risk: authorization only at controller level**
  - Controller-only checks are brittle. Ensure enforcement also exists in application/query/data access paths where appropriate.

- **Risk: test fragility**
  - Integration tests around auth can be brittle if the project lacks test helpers. Reuse existing test infrastructure rather than inventing a separate auth simulation model.

Follow-ups to note if discovered but not required for this task:

- Add a formal tenant authorization policy/requirement if current checks are scattered.
- Add a shared tenant-scoped repository/query helper or specification pattern if repeated filtering appears.
- Consider database-level reinforcement later, such as PostgreSQL row-level security, if the architecture evolves that way.
- Extend coverage to all tenant-owned modules as more endpoints are implemented under EP-1 and later stories.