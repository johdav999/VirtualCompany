# Goal
Implement `TASK-7.4.1` for `ST-104` by adding production-ready API health endpoints that report:
- application liveness/readiness
- PostgreSQL connectivity
- Redis connectivity
- object storage connectivity

The implementation should fit the existing `.NET` solution and ASP.NET Core API project, follow clean modular boundaries, and be safe for production use. Prefer built-in ASP.NET Core health checks where possible.

# Scope
In scope:
- Add health check registrations in the API startup/composition root.
- Expose at least:
  - a lightweight liveness endpoint for the app process
  - a readiness/dependency endpoint covering database, Redis, and object storage
- Ensure health responses are machine-readable JSON.
- Use configuration-driven dependency wiring.
- Keep endpoint behavior suitable for container/cloud hosting and orchestrators.
- Add or update tests if the repo already has an API/integration test pattern for endpoint behavior.

Out of scope:
- Full observability stack work beyond this task, including:
  - structured logging/correlation IDs
  - exception handling middleware
  - background job retry infrastructure
- Business audit events
- UI surfacing of health state
- Adding new infrastructure services not already referenced by the solution unless required for health check support

Assumptions to validate in the codebase before implementing:
- API project uses modern ASP.NET Core minimal hosting (`Program.cs`) or equivalent startup composition.
- Infrastructure project already contains registrations for PostgreSQL, Redis, and object storage abstractions/configuration.
- Object storage may be Azure Blob, S3-compatible, or another provider; implement health checks against the existing abstraction/provider rather than inventing a new storage stack.

# Files to touch
Inspect first, then modify only what is necessary. Likely files include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/*` files related to startup, dependency injection, endpoint mapping, or middleware
- `src/VirtualCompany.Infrastructure/*` files related to:
  - database registration
  - Redis registration
  - object storage registration
  - storage abstractions/options
- `src/VirtualCompany.Api/appsettings.json`
- `src/VirtualCompany.Api/appsettings.Development.json`
- any existing options/config classes for connection strings or storage settings
- test project files, if present, for API endpoint/integration coverage

If needed, add small focused files such as:
- `src/VirtualCompany.Api/Health/HealthCheckResponseWriter.cs`
- `src/VirtualCompany.Infrastructure/Health/ObjectStorageHealthCheck.cs`

Do not rename or broadly refactor unrelated files.

# Implementation plan
1. **Discover current composition and dependency setup**
   - Inspect `Program.cs` and any extension methods used for service registration.
   - Identify how PostgreSQL is configured:
     - EF Core `DbContext`
     - Npgsql connection
     - repository abstraction
   - Identify how Redis is configured:
     - `IConnectionMultiplexer`
     - distributed cache
     - custom client wrapper
   - Identify how object storage is configured:
     - provider type
     - client abstraction
     - options class
   - Check whether health checks packages are already referenced.

2. **Choose endpoint shape**
   - Implement two endpoints:
     - `/health/live` → app/process only, no external dependency checks
     - `/health/ready` → includes database, Redis, and object storage
   - Optionally expose `/health` as an alias to readiness only if that matches existing conventions in the repo.
   - Use tags/predicates so liveness stays fast and does not fail due to transient dependency issues.

3. **Register health checks**
   - In API startup, add `AddHealthChecks()`.
   - Register:
     - a self/app check, tagged for `live`
     - PostgreSQL check, tagged for `ready`
     - Redis check, tagged for `ready`
     - object storage check, tagged for `ready`
   - Prefer provider-native registrations if already available in the solution.
   - If no built-in object storage health check exists for the current provider, implement a custom `IHealthCheck` that performs a minimal safe operation, such as:
     - validating client creation and configuration
     - optionally checking container/bucket accessibility with a lightweight metadata/list/head request
   - Avoid expensive or mutating operations.

4. **Implement object storage health check carefully**
   - Reuse existing storage abstraction/client registration.
   - Do not upload/delete files for health probing.
   - Return:
     - `Healthy` when the provider is reachable and the configured bucket/container is accessible
     - `Degraded` or `Unhealthy` only with safe, non-secret diagnostic detail
   - Ensure cancellation tokens are respected.

5. **Add JSON response writer**
   - Configure `MapHealthChecks` with a custom response writer returning structured JSON, for example:
     - overall status
     - total duration
     - per-check entries with status, description, and optional data
   - Keep output concise and safe:
     - no secrets
     - no raw connection strings
     - no stack traces in response
   - Ensure content type is `application/json`.

6. **Map endpoints**
   - Add endpoint mappings in the API project:
     - `/health/live` using predicate/tag for self only
     - `/health/ready` using predicate/tag for readiness checks
   - If the API uses auth by default, explicitly allow anonymous access to health endpoints if operationally appropriate and consistent with current API conventions.
   - Keep endpoint names and routes stable.

7. **Configuration alignment**
   - Verify required configuration keys exist for:
     - database connection string
     - Redis connection string
     - object storage settings
   - Add placeholder/example config only if the repo convention includes local appsettings samples.
   - Do not hardcode environment-specific values.

8. **Testing**
   - If there is an existing API integration test project:
     - add tests for `/health/live` returning success when app boots
     - add tests for `/health/ready` shape and status behavior, using test doubles or conditional setup if external dependencies are not available
   - If no integration test harness exists, keep code testable and document manual validation steps clearly.
   - At minimum, ensure the solution builds cleanly.

9. **Implementation quality constraints**
   - Keep changes minimal and localized.
   - Follow existing naming, folder, and DI patterns.
   - Do not introduce broad architectural changes.
   - Do not leak infrastructure exceptions directly in HTTP responses.
   - Prefer extension methods if startup code is already organized that way.

10. **Document final behavior in code comments only where useful**
   - Add brief comments only for non-obvious health check tagging or custom object storage probing logic.
   - Avoid noisy comments.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests if present:
   - `dotnet test`

3. Run the API locally and verify endpoints:
   - `GET /health/live`
   - `GET /health/ready`

4. Confirm expected behavior:
   - `/health/live` returns HTTP 200 when the app is running, even if optional downstream dependencies are unavailable.
   - `/health/ready` returns:
     - HTTP 200 when app, database, Redis, and object storage are healthy
     - non-200 when required dependencies are unavailable, per ASP.NET Core health check behavior
   - Response body is valid JSON and includes per-dependency statuses.

5. Validate dependency coverage:
   - Database check actually exercises PostgreSQL connectivity.
   - Redis check actually exercises Redis connectivity.
   - Object storage check actually exercises the configured storage provider/client in a lightweight way.

6. Validate production safety:
   - No secrets or connection strings appear in responses.
   - No mutating storage operations are used for health checks.
   - Health endpoints remain anonymous only if intended and consistent with deployment needs.

7. If possible, simulate failure modes locally:
   - stop Redis or point to invalid Redis config and verify `/health/ready` degrades/fails appropriately
   - use invalid object storage config and verify safe unhealthy response
   - verify `/health/live` still succeeds during dependency outage

# Risks and follow-ups
- **Unknown object storage provider**
  - Risk: provider-specific health probing may differ.
  - Follow-up: implement against the existing abstraction/provider found in the repo, not assumptions from the architecture doc.

- **Missing health check packages**
  - Risk: additional NuGet packages may be needed for Npgsql/Redis/provider-specific checks.
  - Follow-up: add the smallest necessary package set only if not already present.

- **Auth/global middleware interactions**
  - Risk: global auth or exception middleware may interfere with health endpoints.
  - Follow-up: verify endpoint accessibility and response shape in the real pipeline.

- **Readiness semantics**
  - Risk: some teams want object storage as required, others optional.
  - Follow-up: treat database, Redis, and object storage as readiness dependencies for this task unless the existing codebase clearly models one as optional.

- **Test environment dependency availability**
  - Risk: integration tests may be flaky if they require real infrastructure.
  - Follow-up: prefer existing test patterns, use controlled doubles where practical, and avoid introducing brittle environment-coupled tests.

- **Future observability work**
  - Follow-up tasks likely remain for the rest of `ST-104`:
    - structured logging with correlation IDs and tenant context
    - safe exception mapping middleware
    - background worker failure logging and retry behavior
    - rate limiting hooks for sensitive endpoints