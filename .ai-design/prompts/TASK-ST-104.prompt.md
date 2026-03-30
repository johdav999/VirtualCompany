# Goal
Implement `TASK-ST-104` for the .NET multi-project solution by establishing baseline platform observability and operational safeguards for the SaaS foundation.

This task should deliver:
- health endpoints for core dependencies
- structured logging with correlation IDs and tenant context
- safe global exception handling for APIs
- background job failure logging with retry support/hooks
- early rate-limiting hooks for chat/task style endpoints
- clear separation between technical logging and future business audit events

Use the backlog story and notes as the source of truth, especially:
- API exposes health endpoints for app, database, Redis, and object storage dependencies
- structured logs include correlation IDs and tenant context where applicable
- unhandled exceptions are captured and mapped to safe API responses
- background job failures are logged and retryable
- separate technical logs from business audit events
- add rate limiting hooks early for chat/task endpoints
- use outbox for reliable side effects

# Scope
In scope:
- Add ASP.NET Core health checks in the API project
- Expose health endpoints suitable for liveness/readiness style checks
- Include checks for:
  - application/process
  - PostgreSQL
  - Redis
  - object storage or an object-storage health abstraction if concrete storage is not yet implemented
- Add request correlation ID handling:
  - accept incoming correlation ID if provided
  - generate one if absent
  - return it in response headers
  - enrich logs/scopes with correlation ID
- Add tenant context enrichment to logs where tenant/company context is available
- Add centralized exception handling middleware or equivalent ASP.NET Core exception handler
- Map unhandled exceptions to safe ProblemDetails-style API responses without leaking internals
- Add logging around background worker execution/failures and define retry-safe behavior where workers already exist
- Add rate limiting registration and policy placeholders/hooks for future chat/task endpoints
- Add configuration structure and documentation for observability settings
- Add tests where practical for middleware/endpoint behavior

Out of scope:
- Full business audit event implementation
- Full distributed tracing platform integration
- External monitoring dashboards/alerts
- Production deployment/IaC changes beyond app-level configuration
- Implementing a full outbox if not already present, though code should not conflict with that direction
- Deep object storage provider implementation if storage is not yet wired; use an abstraction or degraded health check registration pattern

# Files to touch
Touch only the files needed after inspecting the repo. Likely candidates include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/appsettings.json`
- `src/VirtualCompany.Api/appsettings.Development.json`
- `src/VirtualCompany.Api/...` middleware/extensions folder(s) for:
  - correlation ID middleware
  - tenant log enrichment middleware
  - exception handling middleware or exception handler
  - health check registration extensions
  - rate limiting registration extensions
- `src/VirtualCompany.Infrastructure/...` for:
  - dependency health check implementations
  - object storage health abstraction/check
  - Redis/Postgres registration helpers if infrastructure owns them
  - background worker logging/retry behavior if workers live here
- `src/VirtualCompany.Application/...` only if needed for worker abstractions or retry contracts
- test projects associated with API/infrastructure if present
- `README.md` if there is a setup/run section that should mention health endpoints or correlation IDs

Before editing, inspect the solution structure and use the existing conventions/namespaces/folders. Prefer extending existing composition root and middleware patterns over introducing parallel patterns.

# Implementation plan
1. Inspect the current solution structure
   - Open `Program.cs` and identify how services, middleware, auth, tenant resolution, logging, and background services are currently wired.
   - Search for:
     - existing health checks
     - existing middleware
     - tenant/company context accessors
     - background workers / hosted services
     - Redis/Postgres/object storage abstractions
     - existing ProblemDetails or exception handling
     - existing rate limiting setup
   - Reuse existing abstractions if present.

2. Add observability configuration model
   - Introduce strongly typed options if the project already uses options binding.
   - Include settings for:
     - health endpoint paths
     - correlation header name, defaulting to something standard like `X-Correlation-ID`
     - optional rate limiting toggles/policies
   - Keep config minimal and aligned with current appsettings style.

3. Implement health checks
   - Register ASP.NET Core health checks in the API composition root.
   - Add:
     - self/app health check
     - PostgreSQL health check using the configured connection string
     - Redis health check if Redis is configured
     - object storage health check:
       - if a storage abstraction exists, implement a lightweight check against it
       - if not, create a small abstraction-friendly health check that reports degraded/unhealthy based on configuration/availability without overcommitting to a provider
   - Tag checks appropriately for liveness/readiness if the app uses tags.
   - Map endpoints such as:
     - `/health`
     - `/health/live`
     - `/health/ready`
   - Ensure responses are safe and machine-readable.

4. Add correlation ID middleware
   - Create middleware that:
     - reads incoming correlation ID header if present
     - generates a new ID if absent
     - stores it in `HttpContext.Items` or a dedicated accessor
     - adds it to response headers
     - begins a logging scope containing the correlation ID
   - Keep the implementation framework-native and lightweight.
   - If there is already a request context abstraction, integrate with it instead of duplicating state.

5. Enrich logs with tenant context
   - Identify how tenant/company context is resolved in the app.
   - Add middleware or logging scope enrichment after tenant resolution so logs include:
     - `CompanyId` / `TenantId` when available
     - possibly user ID if already safely available and consistent with conventions
   - Do not invent tenant resolution logic here; only consume existing context.
   - Ensure logs remain safe when no tenant is resolved.

6. Add centralized exception handling
   - Implement global exception handling using the project’s preferred ASP.NET Core pattern:
     - `UseExceptionHandler`
     - custom middleware
     - `IExceptionHandler` if the target framework supports it and fits the codebase
   - Log unhandled exceptions with correlation ID and tenant context.
   - Return safe `ProblemDetails` responses:
     - generic message for unexpected server errors
     - include correlation ID in extensions or response metadata if appropriate
     - do not leak stack traces, connection strings, SQL, or internal implementation details
   - If there are known domain/application exception types already in the codebase, map them consistently without broadening scope too much.

7. Add rate limiting hooks
   - Register ASP.NET Core rate limiting services.
   - Define named policies intended for future high-risk endpoints such as chat/task APIs.
   - If no such endpoints exist yet, keep policies registered but not globally disruptive.
   - Prefer configuration-driven values.
   - Document intended usage in code comments or README if helpful.

8. Improve background worker operational safeguards
   - Find existing `BackgroundService` / hosted services / worker loops.
   - Ensure each worker execution path:
     - logs start/stop/failure with correlation-friendly context where possible
     - catches and logs exceptions at the worker boundary
     - does not silently die on unhandled exceptions
     - supports retry behavior for transient failures if retry logic already exists or can be added safely
   - If there is no worker framework yet, add a small reusable pattern/helper for future workers rather than inventing a full job system.
   - Keep distinction clear:
     - technical logs for failures/retries
     - no business audit event implementation in this task

9. Keep outbox direction compatible
   - Do not implement a full outbox unless already partially present.
   - Ensure any background dispatch/retry logging is compatible with future outbox-backed side effects.
   - Avoid direct fire-and-forget patterns in request handlers if you encounter them; if changing them is too broad, leave a focused TODO note.

10. Add tests
   - Add or update tests for:
     - correlation ID middleware behavior
     - exception handler returns safe response shape
     - health endpoints respond successfully when dependencies are mocked/configured
     - tenant/correlation logging scope behavior if testable without brittle logger assertions
   - Prefer focused unit/integration tests using existing test patterns in the repo.

11. Update documentation
   - Update `README.md` or relevant docs with:
     - health endpoint paths
     - correlation ID header behavior
     - any required config for Redis/object storage health checks
     - note that technical logs are separate from future business audit events

12. Final quality pass
   - Keep code idiomatic for the existing solution.
   - Avoid introducing heavy observability frameworks unless already present.
   - Ensure null-safe behavior when optional dependencies are not configured in local development.
   - Keep naming consistent with the architecture’s `company` terminology while tolerating `tenant` in technical plumbing where already established.

# Validation steps
1. Restore/build the solution
   - Run:
     - `dotnet build`

2. Run tests
   - Run:
     - `dotnet test`

3. Manually verify API startup
   - Start the API project.
   - Confirm the app starts cleanly with the new middleware and service registrations.

4. Verify health endpoints
   - Call:
     - `/health`
     - `/health/live`
     - `/health/ready`
   - Confirm:
     - healthy response when dependencies are available
     - sensible degraded/unhealthy behavior when a dependency is intentionally misconfigured
     - no sensitive internals are exposed

5. Verify correlation ID behavior
   - Send a request without a correlation header and confirm:
     - response includes generated correlation header
     - logs include the same correlation ID
   - Send a request with a correlation header and confirm:
     - same value is propagated to response/logs

6. Verify tenant context logging
   - Call a tenant-scoped endpoint with valid company context.
   - Confirm logs include tenant/company identifier where available.
   - Confirm non-tenant endpoints still log safely without errors.

7. Verify exception handling
   - Trigger an unhandled exception in a safe local/dev scenario or via a test endpoint if one exists.
   - Confirm:
     - response is a safe ProblemDetails payload
     - no stack trace/internal details are returned
     - logs contain the exception plus correlation ID

8. Verify background worker safeguards
   - If workers exist, trigger or simulate a worker failure.
   - Confirm:
     - failure is logged
     - worker does not silently terminate
     - retry behavior occurs or is clearly supported by the implemented pattern

9. Verify rate limiting registration
   - Confirm the app registers named policies successfully.
   - If any endpoint is wired to a policy, verify throttling behavior and safe responses.

# Risks and follow-ups
- The repo may not yet have concrete Redis or object storage implementations.
  - In that case, implement health checks via abstractions or conditional registration rather than blocking the story.
- Tenant resolution may not be fully established yet.
  - Only enrich logs from existing resolved context; do not create speculative tenant plumbing in this task.
- Background job infrastructure may be minimal at this stage.
  - Prefer resilient hosted-service boundaries and reusable retry/logging patterns over introducing a full scheduler/job framework.
- Logging provider choice may not yet be standardized.
  - Use `ILogger` + scopes and structured message templates so the app remains provider-agnostic.
- Avoid conflating technical observability with domain auditability.
  - Business audit events belong to later stories such as audit/explainability.
- If rate limiting could break current development flows, keep policies opt-in and configuration-driven.
- Follow-up candidates after this task:
  - OpenTelemetry tracing/metrics
  - centralized log sink integration
  - outbox dispatcher observability
  - worker dashboard/exception inbox
  - SLO/alert definitions
  - business audit event implementation