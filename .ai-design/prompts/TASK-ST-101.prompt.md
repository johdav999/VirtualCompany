# Goal
Implement **TASK-ST-101 — Tenant-aware authentication and membership** for the .NET solution so that:

- users can authenticate through an abstraction that supports future SSO providers,
- users can belong to one or more companies via memberships,
- every tenant-owned API request is resolved against a **company context**,
- authorization can use persisted membership roles,
- cross-tenant access is blocked with **forbidden/not found** behavior,
- the implementation aligns with the architecture and backlog guidance for:
  - shared-schema multi-tenancy,
  - `company_id` enforcement,
  - ASP.NET Core policy-based authorization.

Use the backlog story acceptance criteria for ST-101 as the effective acceptance criteria, even though the task header says none were provided.

# Scope
In scope:

- Add or complete domain/application/infrastructure/API support for:
  - `User`
  - `Company`
  - `CompanyMembership`
- Implement authentication plumbing with a provider abstraction suitable for local/dev auth now and future external SSO later.
- Implement tenant/company resolution for authenticated requests.
- Persist and load memberships and roles.
- Enforce tenant scoping for tenant-owned API access.
- Add authorization policies/handlers that can evaluate membership and role in company context.
- Add at least minimal endpoints or API surface to:
  - get current authenticated user,
  - list current user’s company memberships,
  - select/use a company context for requests,
  - verify access to a company-scoped resource path.
- Add tests covering tenant isolation and membership-based authorization.

Out of scope unless required to make ST-101 coherent:

- Full onboarding wizard (ST-102)
- Invitation flows (ST-103)
- Full SSO integration
- Mobile-specific work
- Broad UI polish beyond minimal web/API support needed to exercise auth and tenant context
- Row-level security in PostgreSQL, unless already partially present
- Advanced permission matrix beyond role-based membership checks

# Files to touch
Inspect the existing solution first and then touch only the relevant files. Expected areas include:

- `src/VirtualCompany.Domain/**`
  - entities/value objects for users, companies, memberships
  - domain enums/constants for membership roles/status
- `src/VirtualCompany.Application/**`
  - auth abstractions/interfaces
  - current user / tenant context services
  - commands/queries for current user memberships
  - authorization requirements/handlers or application-facing policy contracts
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext and entity configurations
  - repositories/query services
  - auth provider implementation for dev/local
  - request tenant resolution implementation
  - persistence migrations
- `src/VirtualCompany.Api/**`
  - authentication setup
  - authorization policy registration
  - middleware or filters for company context resolution
  - endpoints/controllers for auth/me/memberships
  - problem details / forbidden / not found behavior
- `src/VirtualCompany.Web/**`
  - only if needed for minimal sign-in/company selection flow or authenticated testing surface
- `README.md`
  - update local auth/testing instructions if behavior changes
- tests project(s), if present:
  - add unit/integration tests for auth, membership resolution, and tenant isolation

If a migrations folder exists, add a migration there. If test projects do not exist, create the smallest appropriate test coverage in the existing testing pattern used by the repo.

# Implementation plan
1. **Inspect the current architecture in code**
   - Determine:
     - whether authentication already exists,
     - whether EF Core and PostgreSQL mappings already exist,
     - whether there is an existing `DbContext`,
     - whether there are existing user/company entities,
     - whether API uses controllers, minimal APIs, or endpoint groups,
     - whether authorization policies are already registered.
   - Preserve existing conventions and module boundaries.

2. **Model the core tenant access domain**
   - Ensure the following core entities exist and are persisted:
     - `User`
       - `Id`
       - `Email`
       - `DisplayName`
       - `AuthProvider`
       - `AuthSubject`
       - timestamps
     - `Company`
       - at least `Id`, `Name`, timestamps
     - `CompanyMembership`
       - `Id`
       - `CompanyId`
       - `UserId`
       - `Role`
       - `PermissionsJson` if already supported by conventions
       - `Status`
       - timestamps
   - Add enums/constants for:
     - membership roles: `owner`, `admin`, `manager`, `employee` at minimum
     - membership status: `pending`, `active`, `revoked` or equivalent
   - Add unique constraints as appropriate, especially around:
     - user identity provider subject,
     - membership uniqueness per `(company_id, user_id)`.

3. **Implement authentication abstraction**
   - Add an application-facing abstraction such as:
     - `IAuthProvider` or equivalent for provider-specific identity mapping,
     - `ICurrentUserAccessor` for current authenticated principal,
     - `ICompanyContextAccessor` for resolved tenant/company context.
   - Keep provider abstraction generic so future SSO/OIDC can plug in without changing domain logic.
   - For now, implement a practical local/dev auth mode if production auth is not yet wired:
     - e.g. JWT bearer, cookie auth, or a development header-based auth provider only if clearly isolated to development.
   - Map authenticated principal claims to:
     - internal user record,
     - provider name,
     - provider subject,
     - email/display name if available.

4. **Resolve and persist users on sign-in**
   - On successful authentication, ensure the system can:
     - find existing user by provider subject or email strategy consistent with current conventions,
     - create/update the user record as needed,
     - expose a stable internal user ID for downstream authorization.
   - Avoid coupling sign-in to company creation.

5. **Implement company membership queries**
   - Add application query/service to return the authenticated user’s memberships.
   - Return enough information for company selection and authorization:
     - company id
     - company name
     - role
     - membership status
   - Only active memberships should be usable for tenant access.

6. **Implement company context resolution**
   - Add a request-scoped company context resolver.
   - Support resolving company context from one explicit source chosen by existing API conventions, preferably:
     - route value,
     - header such as `X-Company-Id`,
     - or claim/session if a selected company is persisted.
   - Prefer explicit request scoping for APIs.
   - Validate that:
     - the user is authenticated,
     - the user has an active membership for the requested company.
   - Store resolved company context in a request-scoped accessor for downstream use.

7. **Enforce tenant scoping in API and application layers**
   - For every tenant-owned endpoint touched in this task:
     - require authenticated user,
     - require resolved company context,
     - verify active membership before processing.
   - Add policy-based authorization in ASP.NET Core, for example:
     - `RequireCompanyMembership`
     - `RequireCompanyRole`
   - Implement authorization handlers that use:
     - current user,
     - resolved company context,
     - persisted membership role/status.
   - Ensure unauthorized access to another company returns:
     - `403 Forbidden` when the company is known but access is denied, or
     - `404 Not Found` when hiding resource existence is more appropriate for resource fetches.
   - Be consistent and document the rule in code comments/tests.

8. **Enforce `company_id` filtering for tenant-owned data access**
   - Add or update repository/query patterns so tenant-owned queries always filter by resolved `company_id`.
   - Do not rely only on controller checks.
   - If there are existing tenant-owned entities/endpoints, update them to use the company context accessor.
   - If no tenant-owned resource exists yet for demonstration, add a minimal protected sample/read endpoint that proves company scoping behavior.

9. **Expose minimal API surface**
   - Add endpoints such as:
     - `GET /api/auth/me`
       - returns current user profile and authentication state
     - `GET /api/auth/memberships`
       - returns current user’s active memberships
     - optional `POST /api/auth/select-company` if the app uses persisted selected company context
   - Add at least one company-scoped endpoint pattern, e.g.:
     - `GET /api/companies/{companyId}/access`
       - returns membership/access summary for the current user in that company
   - Keep responses simple and aligned with existing DTO conventions.

10. **Database and EF Core updates**
    - Add/update EF Core configurations for:
      - `users`
      - `companies`
      - `company_memberships`
    - Ensure foreign keys and indexes exist.
    - Add migration(s).
    - Match PostgreSQL-friendly types and naming conventions already used by the project.

11. **Testing**
    - Add unit and/or integration tests for:
      - authenticated user with multiple memberships can list memberships,
      - request with valid company context and active membership succeeds,
      - request for company without membership is denied,
      - role is available to authorization checks,
      - tenant-owned query filters by `company_id`,
      - inactive/pending membership cannot access tenant-scoped endpoint.
    - Prefer integration tests for end-to-end auth + authorization + tenant resolution behavior.

12. **Documentation**
    - Update `README.md` with:
      - how to run auth locally,
      - how to call membership endpoints,
      - how company context is supplied on requests,
      - expected forbidden/not found behavior.

Implementation notes:

- Follow **clean architecture boundaries**.
- Prefer **policy-based authorization** over ad hoc role checks in controllers.
- Keep auth provider details isolated from domain/application logic.
- Use **shared-schema multi-tenancy** with strict `company_id` enforcement.
- Do not introduce unnecessary microservice-style complexity.
- If the repo already has patterns for MediatR/CQRS-lite, endpoint groups, result wrappers, or problem details, use them consistently.

# Validation steps
Run these after implementation:

1. Restore/build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. Verify database changes
   - confirm migration compiles and applies cleanly
   - confirm tables/indexes/constraints for users, companies, memberships exist

4. Manual/API verification
   - authenticate as a test user
   - call `GET /api/auth/me`
   - call `GET /api/auth/memberships`
   - verify multiple memberships are returned when seeded
   - call a company-scoped endpoint with a company the user belongs to
   - verify success
   - call the same endpoint with a different company ID
   - verify `403` or `404` per the implemented rule
   - verify inactive/pending membership is rejected

5. Authorization verification
   - verify membership role is available in authorization handler/policy evaluation
   - verify role-gated policy passes/fails correctly for at least two roles

6. Tenant isolation verification
   - verify tenant-owned queries include `company_id` filtering
   - verify no endpoint touched by this task can return another company’s data for an authenticated user without membership

# Risks and follow-ups
- **Auth approach mismatch risk:** the repo may already have a preferred auth mechanism. Reuse it rather than introducing a conflicting stack.
- **Incomplete tenant enforcement risk:** checking membership only at the API layer is insufficient; ensure query/repository filtering also uses resolved company context.
- **Ambiguous 403 vs 404 behavior:** choose a consistent rule and lock it down with tests.
- **Future SSO compatibility:** keep provider-specific claim mapping isolated behind abstractions.
- **Membership lifecycle gaps:** invitation/acceptance/revocation flows are out of scope here and should be completed in ST-103.
- **Permission granularity:** this task should expose membership roles to authorization, but richer permission JSON evaluation can follow later.
- **Selected company UX:** if no persisted company selection exists yet, explicit per-request company context is acceptable for now.
- **Potential follow-up tasks:**
  - ST-102 company creation should create initial owner membership
  - ST-103 invitation and role assignment flows
  - ST-104 correlation IDs, tenant-aware logging, and operational safeguards
  - optional PostgreSQL row-level security evaluation later for defense in depth