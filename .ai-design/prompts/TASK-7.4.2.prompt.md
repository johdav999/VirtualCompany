# Goal
Implement TASK-7.4.2 for ST-104 by ensuring structured technical logs consistently include:
- a correlation ID for request/job traceability
- tenant/company context where applicable in the current execution scope

The implementation should fit the existing .NET modular monolith, keep technical logging separate from business audit events, and work for both HTTP request handling and background worker execution paths.

# Scope
In scope:
- Add or complete correlation ID propagation for ASP.NET Core requests
- Enrich structured logs with correlation ID and tenant/company context
- Ensure tenant context is included only when available/applicable
- Make the enrichment available to API request logs, application/service logs, exception logs, and background worker logs
- Add safe defaults for non-tenant or pre-tenant flows
- Add/update tests for the enrichment behavior where practical

Out of scope:
- Business audit event persistence
- Full distributed tracing/OpenTelemetry rollout unless already partially present
- Major logging provider replacement
- UI changes
- New product features beyond logging context propagation

Assumptions to verify in the codebase before implementation:
- Logging is likely using Microsoft.Extensions.Logging, possibly with Serilog or built-in structured logging
- Tenant resolution likely exists from ST-101 and can be reused
- There may already be middleware for exception handling or request context that should be extended rather than duplicated

# Files to touch
Inspect first, then update only the minimum necessary set. Likely candidates:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/...` request pipeline files:
  - middleware
  - filters
  - exception handling
  - tenant resolution
- `src/VirtualCompany.Application/...` shared abstractions if a request/tenant context abstraction exists
- `src/VirtualCompany.Infrastructure/...` logging or background worker infrastructure
- `src/VirtualCompany.Api/appsettings*.json` if logging output templates or enrichers are configured there
- Test projects related to API/infrastructure logging behavior if present

Potential new files if needed:
- `src/VirtualCompany.Api/Middleware/CorrelationIdMiddleware.cs`
- `src/VirtualCompany.Api/Middleware/LoggingScopeMiddleware.cs`
- `src/VirtualCompany.Application/Abstractions/IExecutionContextAccessor.cs`
- `src/VirtualCompany.Infrastructure/Logging/...` helpers for log scope enrichment
- test files covering middleware/context behavior

Do not invent these exact paths if the repository already has equivalent constructs; prefer extending existing request context, tenant context, or logging infrastructure.

# Implementation plan
1. **Inspect existing observability and context plumbing**
   - Review `Program.cs` and startup registration for:
     - logging provider setup
     - middleware ordering
     - exception handling
     - health checks
     - background worker registration
   - Search for existing concepts:
     - correlation ID
     - request ID
     - trace ID
     - tenant context
     - company context
     - logging scope
     - `BeginScope`
     - Serilog enrichers
     - hosted services / background jobs

2. **Choose the least invasive context propagation approach**
   - Prefer standard ASP.NET Core and `ILogger.BeginScope(...)` if no richer logging framework is already established
   - If Serilog is already configured, use enrichers plus `LogContext.PushProperty(...)` where appropriate
   - Reuse `HttpContext.TraceIdentifier` if suitable, or support a request header such as `X-Correlation-ID` with fallback generation
   - Standardize on one correlation ID source for logs:
     - incoming header if valid
     - otherwise generated value
   - Ensure the correlation ID is also written back to the response header for supportability

3. **Implement correlation ID middleware**
   - Add middleware early in the pipeline
   - Behavior:
     - read correlation ID from request header if present
     - validate/sanitize length and characters
     - otherwise generate a new ID
     - store it in `HttpContext.Items` and/or a request context accessor
     - set response header
     - create a logging scope containing at minimum:
       - `CorrelationId`
       - optionally `TraceIdentifier` if distinct
   - Avoid breaking existing trace/activity behavior if `Activity.Current` is already used

4. **Add tenant/company log enrichment**
   - Reuse the existing tenant/company resolution mechanism from ST-101
   - After tenant resolution occurs, ensure logs emitted downstream include:
     - `CompanyId` or `TenantId` using the project’s canonical naming
   - If tenant context is unavailable, do not emit fake values; either omit the property or set null only if the logging framework handles it cleanly
   - Prefer a single canonical property name across the codebase:
     - use `CompanyId` if the domain consistently uses company/workspace
     - otherwise use `TenantId`
   - If both are useful, avoid ambiguity unless already established in code

5. **Ensure exception logs include the same context**
   - Update existing global exception middleware/handler so unhandled exception logs are emitted within the active scope
   - Confirm safe API responses remain unchanged except possibly for returning the correlation ID in headers
   - If problem details are used, consider including correlation ID in extensions only if that aligns with existing API conventions

6. **Cover background workers and non-HTTP execution**
   - For hosted services/background jobs, create a scoped logging context per execution unit
   - Include:
     - generated correlation ID for the job run or work item
     - tenant/company context when the job is tenant-scoped
   - If jobs process outbox/messages/tasks, propagate any existing correlation ID from the payload/metadata when available
   - If no metadata exists yet, generate one and log consistently for that execution path

7. **Normalize logging usage in key code paths**
   - Review a few representative logs in:
     - API controllers/endpoints
     - application services
     - background workers
     - exception handling
   - Ensure they use structured logging placeholders rather than string interpolation where touched
   - Do not refactor the whole codebase; only fix touched areas necessary to validate the feature

8. **Add tests**
   - Add focused tests for:
     - correlation ID generated when absent
     - correlation ID preserved when provided
     - response header contains correlation ID
     - tenant/company context is added when resolved
     - no tenant/company context is added for anonymous/pre-tenant endpoints where not applicable
   - If direct log capture tests are too heavy for the current test setup, test the middleware/context accessor behavior and any helper methods deterministically

9. **Document conventions in code comments if needed**
   - Keep comments brief
   - Clarify:
     - canonical header name
     - canonical log property names
     - when tenant context should be present

Implementation notes:
- Prefer middleware ordering like:
  1. correlation ID
  2. exception handling
  3. routing/auth/tenant resolution as appropriate
  4. tenant-aware logging scope if separate
- If tenant resolution happens after authentication/authorization, ensure the tenant scope is established as soon as the company context is known
- Avoid storing mutable global state; use request scope, async-local accessors, or logging scopes only

# Validation steps
1. Restore/build/test
   - `dotnet build`
   - `dotnet test`

2. Manual API validation
   - Run the API locally
   - Call a health or simple endpoint without `X-Correlation-ID`
   - Verify:
     - response includes generated correlation ID header
     - logs contain `CorrelationId`
   - Call the same endpoint with `X-Correlation-ID: test-corr-123`
   - Verify logs and response preserve that value

3. Tenant-aware validation
   - Call a tenant-scoped endpoint with a resolved company context
   - Verify logs include the canonical tenant/company property
   - Call a non-tenant or pre-auth endpoint
   - Verify logs do not incorrectly attach tenant context

4. Exception-path validation
   - Trigger a known failing endpoint or temporary local test path
   - Verify unhandled exception logs still include correlation ID and tenant/company context when applicable
   - Verify API response remains safe and does not leak internals

5. Background worker validation
   - Run a tenant-scoped background process if available
   - Verify logs for a single execution share a correlation ID
   - Verify tenant/company context appears for tenant-scoped work items

6. Structured log validation
   - Confirm touched logs use structured properties, not concatenated strings
   - If using Serilog/JSON console, inspect emitted JSON fields
   - If using default console logging, confirm scopes are visible/configured in development as needed

# Risks and follow-ups
- **Unknown existing logging stack**
  - Risk: the repo may already use Serilog, OpenTelemetry, or custom middleware
  - Mitigation: extend existing patterns instead of layering duplicate mechanisms

- **Tenant resolution timing**
  - Risk: tenant context may not be available early enough for all request logs
  - Mitigation: establish correlation scope first, then enrich with tenant context immediately after resolution; accept that very early startup/request logs may only have correlation ID

- **Background job metadata gaps**
  - Risk: current job payloads may not carry correlation IDs
  - Mitigation: generate per-execution IDs now; follow up later to propagate IDs through outbox/messages/tasks end-to-end

- **Log property naming inconsistency**
  - Risk: mixed use of `TenantId`, `CompanyId`, `WorkspaceId`
  - Mitigation: inspect domain conventions and standardize on the canonical property in touched code

- **Testing log output directly can be brittle**
  - Risk: provider-specific assertions may be fragile
  - Mitigation: prefer testing middleware/context behavior and only capture logs where the test harness already supports it

Suggested follow-ups beyond this task:
- Add end-to-end correlation propagation through outbox, workflow, and tool execution records
- Align with ST-502 note to persist correlation IDs across prompt, tool, task, and audit records
- Consider OpenTelemetry tracing and log/trace correlation in a later observability story