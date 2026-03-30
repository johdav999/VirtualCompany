# Goal
Implement backlog task **TASK-7.1.4 — Membership roles are persisted and available to authorization checks** for story **ST-101 Tenant-aware authentication and membership** in the existing .NET solution.

The coding agent should ensure that:
- company membership roles are stored in persistence,
- roles are loaded as part of authenticated tenant membership resolution,
- authorization checks can consume those roles in a policy-based ASP.NET Core flow,
- the implementation aligns with the shared-schema multi-tenant architecture using `company_id` enforcement.

No explicit task-level acceptance criteria were provided, so derive completion from the story acceptance criteria and architecture notes, especially:
- users can resolve one or more company memberships,
- every tenant-owned API request is scoped by company context,
- membership roles are persisted and available to authorization checks,
- prefer policy-based authorization in ASP.NET Core.

# Scope
In scope:
- Inspect the current identity, membership, and authorization implementation across API, Application, Domain, and Infrastructure projects.
- Add or complete persistence for membership roles on the company membership model/entity.
- Ensure role values are represented consistently across domain, persistence, and auth layers.
- Ensure authenticated user context can resolve memberships including role information.
- Expose membership role data to authorization checks, ideally through claims, a tenant access context, or a custom authorization service/policy handler already used by the solution.
- Add or update tests covering persistence and authorization consumption of membership roles.
- Update any necessary EF Core mappings, migrations, DTOs, query handlers, auth services, and policy registration.

Out of scope:
- Full invitation flows from ST-103 unless required to keep membership role persistence coherent.
- Broad redesign of authentication provider abstraction.
- UI work beyond what is minimally necessary to support or verify the backend behavior.
- Implementing all tenant authorization policies if the app does not yet have them; focus on making membership roles available to the existing or intended authorization path.

# Files to touch
Start by inspecting these likely areas, then modify the concrete files you find relevant:

- `src/VirtualCompany.Domain/**`
  - membership/company/user domain entities or value objects
  - role enums/constants
- `src/VirtualCompany.Application/**`
  - auth/user context abstractions
  - membership queries/commands
  - tenant access services
  - authorization requirement/policy abstractions
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext
  - entity configurations
  - repositories
  - auth claim transformation or identity integration
  - migrations
- `src/VirtualCompany.Api/**`
  - authentication/authorization setup
  - policy registration
  - current user / tenant resolution middleware or filters
  - protected endpoints used to validate role-based access
- `src/VirtualCompany.Web/**`
  - only if there is a thin dependency on membership role display or tenant selection models
- `README.md`
  - only if auth/tenant setup documentation needs a brief update

Also inspect:
- existing test projects in the solution, if present
- any seed/bootstrap code that creates companies or memberships
- any current `company_memberships` mapping or SQL migration history

# Implementation plan
1. **Assess the current implementation**
   - Locate the current user, company, and membership models.
   - Determine whether `company_memberships.role` already exists in the domain and database, or whether it is missing/incomplete.
   - Identify how authenticated users are currently resolved and how tenant context is selected per request.
   - Identify whether authorization currently uses:
     - ASP.NET Core roles,
     - custom claims,
     - custom policies/requirements,
     - a tenant access service,
     - or a combination.

2. **Standardize membership role representation**
   - Introduce or refine a canonical role definition for human company membership roles, matching backlog language where practical:
     - `owner`
     - `admin`
     - `manager`
     - `employee`
     - `finance_approver`
     - `support_supervisor`
   - Prefer a strongly typed representation in domain/application code:
     - enum, smart enum, or constants + validation.
   - Ensure serialization/persistence uses stable string values suitable for PostgreSQL `role text`.

3. **Persist membership roles**
   - Update the membership entity/model so role is required and persisted.
   - Update EF Core configuration to map the role column correctly.
   - If missing in schema, add a migration for `company_memberships.role`.
   - If present but nullable or unconstrained, tighten the model as appropriate without breaking existing data assumptions.
   - Ensure create/update membership flows persist role values correctly.

4. **Load roles during membership resolution**
   - Update the membership retrieval path used after authentication so resolved memberships include:
     - membership id
     - company id
     - user id
     - role
     - status
   - If there is a “current tenant access” or “current user memberships” query/service, ensure role is included in its result contract.
   - If there is tenant selection logic, ensure the selected company membership carries role information forward.

5. **Make roles available to authorization checks**
   - Integrate membership role into the authorization path using the project’s existing pattern.
   - Preferred approaches, in order of fit:
     1. custom tenant-aware authorization service/handler that reads current membership role from resolved tenant context,
     2. claims enrichment for selected company membership,
     3. policy handlers that query membership role on demand.
   - Avoid using global ASP.NET identity roles detached from tenant context, since roles are company-scoped.
   - Ensure authorization checks are tenant-aware: the same user may have different roles in different companies.
   - If policies do not yet exist, add a minimal policy pattern such as:
     - `RequireCompanyRole(owner|admin|...)`
     - or a requirement/handler that checks current company membership role.
   - Keep the implementation extensible for future policy-based authorization.

6. **Enforce membership status if applicable**
   - If the codebase already models membership status, ensure only active/accepted memberships participate in authorization.
   - Pending/revoked memberships should not grant role-based access.

7. **Wire into API authorization**
   - Update API startup/program configuration to register any new:
     - authorization requirements/handlers,
     - claims transformation,
     - tenant membership context services.
   - Apply or verify policy usage on at least one representative protected endpoint if such endpoints already exist.
   - Do not over-expand endpoint coverage; just ensure the plumbing is real and testable.

8. **Add tests**
   - Add unit/integration tests covering:
     - membership role persistence round-trip,
     - membership resolution returns role,
     - authorization succeeds for allowed role in selected company,
     - authorization fails for insufficient role,
     - authorization is tenant-specific when a user has different roles across companies.
   - Prefer integration tests around API/auth behavior if the solution already has that pattern; otherwise combine application/infrastructure tests.

9. **Document assumptions in code**
   - Add concise comments only where the tenant-scoped role behavior is non-obvious.
   - If needed, add a short README note describing that company membership roles are tenant-scoped and not global identity roles.

10. **Keep changes minimal and aligned**
   - Follow existing project conventions, naming, and architecture boundaries.
   - Do not introduce a new auth framework or broad refactor unless absolutely necessary to complete the task correctly.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If EF Core migrations are used in this repo:
   - generate/apply the migration if needed,
   - verify the `company_memberships` table includes a persisted `role` column with expected values.

4. Manually verify the code path:
   - create or inspect a user with memberships in multiple companies,
   - confirm each membership has a persisted role,
   - confirm the resolved current tenant membership includes the role,
   - confirm an authorization check reads the role from the selected company context rather than a global user role.

5. Verify negative cases:
   - user with no membership in company is denied,
   - user with insufficient membership role is denied,
   - pending/inactive membership does not authorize if status is modeled.

6. Confirm no architectural regressions:
   - tenant-owned requests remain scoped by `company_id`,
   - no authorization logic bypasses tenant context,
   - no direct dependence on non-tenant global roles for company authorization.

# Risks and follow-ups
- **Risk: global vs tenant-scoped roles confusion**
  - Do not map company membership roles directly to global ASP.NET identity roles unless the implementation is explicitly tenant-qualified.

- **Risk: incomplete tenant context selection**
  - If the app has not fully implemented current company selection, role-based authorization may be ambiguous for multi-membership users. In that case, use the existing tenant resolution mechanism and document assumptions.

- **Risk: existing schema drift**
  - The database may already contain a `role` column with inconsistent/null data. Handle migration carefully and avoid destructive assumptions.

- **Risk: authorization plumbing may be partial**
  - If policies are not yet established, implement the smallest viable tenant-aware policy/requirement to prove roles are consumable by authorization checks.

Follow-ups to note if not completed within this task:
- add richer role hierarchy helpers and reusable policy builders,
- add invitation/role-change flows from ST-103 if role persistence is only partially wired,
- add endpoint-level policy coverage across all tenant-owned modules,
- consider centralizing tenant membership resolution into a single current-access service if the codebase currently duplicates it.