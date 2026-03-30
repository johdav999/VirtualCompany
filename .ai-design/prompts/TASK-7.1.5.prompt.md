# Goal
Implement **TASK-7.1.5 — Use shared-schema multi-tenancy with `company_id` enforcement** for **ST-101 Tenant-aware authentication and membership** in the existing .NET solution.

The coding agent should add the foundational tenant-scoping mechanism so that:

- tenant-owned data uses **shared-schema multi-tenancy**
- the active tenant is resolved as **company context**
- all tenant-owned application/database access is enforced via **`company_id`**
- membership is used to validate whether the authenticated user can access a company
- cross-company access is rejected with **forbidden or not found semantics**
- the implementation aligns with the architecture and backlog notes:
  - ASP.NET Core
  - policy-based authorization
  - auth provider abstraction
  - shared database/shared schema with `company_id` on tenant-owned tables

There are no explicit task-level acceptance criteria, so implement to satisfy the parent story acceptance criteria for ST-101 and the architecture guidance.

# Scope
In scope:

- Add a **tenant context abstraction** that resolves the active `company_id` for the current request.
- Add or update **membership-aware tenant resolution** based on the authenticated user and selected company.
- Enforce `company_id` filtering for tenant-owned entities in the application/infrastructure layers.
- Add ASP.NET Core plumbing so tenant context is available consistently in API requests.
- Add authorization/policy hooks to ensure the authenticated user is a valid member of the requested company.
- Update persistence mappings/entities/configuration as needed to support `company_id` enforcement.
- Add tests covering:
  - valid membership access
  - denied cross-tenant access
  - missing tenant context
  - repository/query filtering behavior

Out of scope unless required by existing code structure:

- Full SSO implementation
- UI-heavy company switcher flows
- Broad refactors unrelated to tenant enforcement
- RLS/database-native tenant isolation unless the project already uses it
- Implementing all future stories beyond what is necessary for ST-101

Use the existing architecture style:
- modular monolith
- clean boundaries
- CQRS-lite
- policy-based authorization
- shared-schema multi-tenancy with `company_id` on tenant-owned tables

# Files to touch
Inspect the solution first and then modify the minimum necessary set. Likely areas include:

- `src/VirtualCompany.Api/`
  - `Program.cs`
  - authentication/authorization setup files
  - middleware, filters, or endpoint pipeline files
  - request context / current user / tenant resolution classes
- `src/VirtualCompany.Application/`
  - abstractions for current user / tenant context
  - commands, queries, handlers that access tenant-owned data
  - authorization services or membership validation services
- `src/VirtualCompany.Domain/`
  - membership/company/user entities
  - tenant-owned entity interfaces or base types if appropriate
- `src/VirtualCompany.Infrastructure/`
  - EF Core `DbContext`
  - entity configurations
  - repository/query implementations
  - migrations if schema changes are needed
  - tenant-aware query filters or enforcement helpers
- `src/VirtualCompany.Shared/`
  - shared contracts/constants if this project holds cross-layer primitives
- tests in the existing test projects
  - add/update unit/integration tests for tenant resolution and enforcement

Also review:
- `README.md` for conventions
- solution-wide patterns for DI, auth, EF Core, and testing

Do not invent new top-level projects unless absolutely necessary.

# Implementation plan
1. **Inspect the current auth, membership, and persistence setup**
   - Build a quick mental map of:
     - how authentication is currently configured
     - whether there is already a current-user abstraction
     - whether `Company`, `User`, and `CompanyMembership` already exist
     - how EF Core entities and repositories are structured
     - whether there are existing authorization policies/handlers
   - Prefer extending existing patterns over introducing parallel ones.

2. **Define tenant context abstractions**
   - Introduce a small, explicit abstraction for request tenant context, for example:
     - current company id
     - whether tenant context is present
     - authenticated user id if already available through a separate abstraction
   - Keep it application-friendly and HTTP-agnostic where possible.
   - If there is already a current request/user service, extend it rather than duplicating.

3. **Choose and implement company resolution strategy**
   - Resolve the active company from the request using the project’s existing API conventions.
   - Prefer one clear mechanism already compatible with web/mobile/API usage, such as:
     - header like `X-Company-Id`
     - route value
     - claim if already established by the auth flow
   - Validate the resolved company against the authenticated user’s memberships.
   - If the user is not a member of the company, fail authorization.
   - If no company is supplied for a tenant-owned endpoint, return an appropriate client error or authorization failure consistent with the existing API style.

4. **Add membership validation service**
   - Implement a focused service in Application/Infrastructure that can answer:
     - is user a member of company?
     - what role(s)/membership record apply?
   - Use `company_memberships` as the source of truth.
   - Ensure membership status is considered if the model supports active/pending/revoked states.
   - Keep this reusable for future authorization checks.

5. **Enforce `company_id` on tenant-owned entities**
   - Identify tenant-owned entities already present in the codebase.
   - Ensure they expose/store `company_id` consistently.
   - If the domain model lacks a common marker, add a lightweight interface/base contract such as a tenant-owned marker only if it fits the current design.
   - For create flows, ensure `company_id` is assigned from the resolved tenant context, not trusted from client payloads.
   - For reads/updates/deletes, ensure queries are filtered by the active `company_id`.

6. **Implement infrastructure-level safeguards**
   - In EF Core, add one or both of the following depending on current architecture:
     - global query filters for tenant-owned entities
     - repository/query helper methods that always require company scope
   - If using global query filters:
     - ensure the current tenant can be accessed safely by the `DbContext`
     - handle design-time/migrations carefully
     - avoid applying tenant filters to global/system tables like `users` or system templates
   - If using repositories/specifications:
     - make tenant scope mandatory for tenant-owned queries
   - Add save-time enforcement where practical:
     - new tenant-owned entities get `company_id` from context
     - prevent accidental mismatched `company_id` updates

7. **Wire authorization policies**
   - Add or update ASP.NET Core authorization so tenant-owned endpoints require:
     - authenticated user
     - valid company context
     - membership in that company
   - Prefer policy-based authorization and reusable handlers/requirements.
   - Keep the policy generic enough for reuse by future stories.

8. **Apply enforcement to relevant endpoints/handlers**
   - Update the relevant API endpoints, controllers, minimal APIs, MediatR handlers, or services so they:
     - require tenant context
     - do not accept arbitrary `company_id` from clients for tenant-owned operations unless explicitly validated
     - use the resolved tenant context for data access
   - For membership listing or company selection endpoints, allow the necessary non-tenant-scoped behavior intentionally.

9. **Handle error semantics carefully**
   - Align with ST-101:
     - unauthorized user -> unauthorized
     - authenticated but not a member / wrong company -> forbidden or not found
   - Prefer not leaking existence of another company’s records.
   - Be consistent across handlers and endpoints.

10. **Add/update schema and migrations if needed**
   - If any tenant-owned tables are missing `company_id`, add it.
   - Add indexes that support tenant-scoped access patterns, e.g. composite indexes involving `company_id`.
   - Ensure foreign keys and nullability reflect the architecture.
   - Generate EF Core migrations only if the current codebase uses migrations in source control.

11. **Add tests**
   - Unit tests:
     - tenant context resolution
     - membership validation
     - authorization handler behavior
   - Integration tests where feasible:
     - authenticated user with valid membership can access company-scoped endpoint
     - authenticated user without membership is denied
     - tenant-owned queries only return rows for active `company_id`
     - create operations stamp `company_id` from context, not request payload
   - Include at least one regression test for cross-tenant data leakage prevention.

12. **Keep implementation clean and minimal**
   - Do not over-engineer a full multi-tenant framework.
   - Build the smallest reusable foundation that future stories can extend.
   - Add concise comments only where the enforcement behavior is non-obvious.

# Validation steps
Run these after implementation:

1. **Restore/build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **If migrations were added, verify them**
   - Ensure the migration compiles and matches the intended schema changes.
   - If the repo has a standard migration validation flow, use it.

4. **Manual verification checklist**
   - Authenticated user with membership in Company A can access Company A tenant-scoped endpoint.
   - Same user cannot access Company B data without membership.
   - Tenant-owned queries return only rows with the active `company_id`.
   - New tenant-owned records are saved with server-resolved `company_id`.
   - Membership roles remain available for downstream authorization checks.
   - Endpoints that should be global/non-tenant-scoped still function correctly.

5. **Code quality checks**
   - Confirm no tenant-owned handler trusts client-supplied `company_id` blindly.
   - Confirm no obvious repository/query path bypasses tenant filtering.
   - Confirm logging/error handling does not leak cross-tenant data.

# Risks and follow-ups
- **Risk: hidden query paths bypass tenant filtering**
  - Some direct `DbContext` usage or ad hoc LINQ may ignore tenant scope.
  - Mitigation: search broadly for tenant-owned entity access and add tests around the most sensitive paths.

- **Risk: overuse of global query filters**
  - EF Core global filters can be powerful but may create surprises in admin/system flows and tests.
  - Mitigation: use them carefully, document exceptions, and combine with explicit application-layer checks.

- **Risk: ambiguous company selection UX/API contract**
  - If the current app has no established company-selection mechanism, choosing one may affect future clients.
  - Mitigation: implement a simple, explicit request mechanism now and keep it abstracted behind a tenant resolver.

- **Risk: membership state semantics may be incomplete**
  - Pending/revoked/inactive memberships may need clearer rules.
  - Mitigation: default to conservative access; only active memberships should authorize tenant access unless existing code says otherwise.

- **Risk: system/global tables accidentally become tenant-scoped**
  - Tables like `users` or system templates should not be filtered by `company_id`.
  - Mitigation: apply tenant enforcement only to tenant-owned entities.

Follow-ups to note in code comments or task notes if not fully addressed here:
- add richer company selection flow for users with multiple memberships
- extend role-based authorization using membership roles
- consider row-level security later if operational needs justify it
- add tenant context to structured logs/correlation for observability story ST-104