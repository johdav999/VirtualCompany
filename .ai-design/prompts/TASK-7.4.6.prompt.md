# Goal
Implement **early rate limiting hooks** for the platform’s likely high-risk request paths, specifically **chat** and **task-related API endpoints**, in support of **TASK-7.4.6 / ST-104 Baseline platform observability and operational safeguards**.

This task is about establishing the **foundation and extension points**, not building a fully tuned production throttling strategy. The implementation should:
- add ASP.NET Core rate limiting infrastructure,
- define named policies for chat/task endpoints,
- wire those policies into endpoint/controller routing,
- ensure responses are safe and observable,
- keep the design **tenant-aware and extensible** for future Redis-backed/distributed enforcement.

Because ST-104 emphasizes operational safeguards, the implementation should favor:
- safe defaults,
- structured logging,
- correlation-friendly diagnostics,
- minimal disruption to existing endpoint behavior.

# Scope
In scope:
- Add ASP.NET Core rate limiting registration in the API project.
- Create **named rate limiting policies** for:
  - chat endpoints,
  - task endpoints,
  - optionally a conservative default/general API policy if it helps structure.
- Apply policies to the relevant endpoints/controllers with minimal invasive changes.
- Add a consistent **429 Too Many Requests** response shape or handler.
- Include structured logging when rate limiting is triggered.
- Keep implementation compatible with current modular monolith architecture and future Redis-backed scaling.
- Add/update tests if there is an existing API/integration test pattern.

Out of scope:
- Full per-plan/per-subscription throttling.
- Redis-backed distributed rate limiting implementation unless the project already has a clean abstraction ready.
- UI/mobile behavior changes beyond preserving API contract expectations.
- Broad rate limiting across every endpoint in the system.
- Business audit events for throttling unless there is already a clear technical logging pattern; this is an operational safeguard, not a business-domain audit feature.

Assumptions to validate in the codebase:
- The API is ASP.NET Core and may use either controllers or minimal APIs.
- Chat/task endpoints may already exist under modules such as communication/task/workflow.
- Tenant context and correlation ID handling may already exist; reuse them if present.
- Redis may exist in infrastructure, but this task should not require distributed enforcement yet.

# Files to touch
Inspect first, then update only what is necessary. Likely files include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/*` files where service registration and middleware are configured
- Endpoint/controller files for chat/task APIs, likely under areas resembling:
  - `src/VirtualCompany.Api/Controllers/**`
  - `src/VirtualCompany.Api/Endpoints/**`
- If configuration is centralized:
  - `src/VirtualCompany.Api/appsettings.json`
  - `src/VirtualCompany.Api/appsettings.Development.json`
- If shared API response models exist:
  - `src/VirtualCompany.Shared/**`
  - or API contracts under `src/VirtualCompany.Api/**`
- If there is an existing observability or middleware folder:
  - `src/VirtualCompany.Api/Middleware/**`
  - `src/VirtualCompany.Api/Infrastructure/**`
- Test projects if present in the solution:
  - any `*.Tests` project covering API behavior

Before coding, identify:
1. where endpoints are mapped,
2. whether controllers or minimal APIs are used,
3. whether there is an existing error response envelope,
4. whether tenant context is already resolved in middleware/claims/services.

# Implementation plan
1. **Discover current API structure**
   - Find chat-related endpoints and task-related endpoints.
   - Determine whether they are implemented as MVC controllers, endpoint groups, or minimal APIs.
   - Identify any existing middleware for exception handling, correlation IDs, tenant resolution, and logging.
   - Identify whether the solution already references `Microsoft.AspNetCore.RateLimiting` or uses built-in .NET rate limiting primitives.

2. **Add rate limiting service registration**
   - In API startup (`Program.cs` or equivalent), register ASP.NET Core rate limiting via `AddRateLimiter(...)`.
   - Define named policies such as:
     - `chat`
     - `tasks`
     - optionally `api-burst` or `default`
   - Prefer a simple built-in limiter such as **fixed window** or **sliding window** for the initial hook.
   - Keep values conservative and easy to tune from configuration if practical.
   - If configuration binding is straightforward, introduce settings like:
     - permit limit,
     - window duration,
     - queue limit,
     - queue processing order.
   - If config plumbing would add too much complexity, use clearly documented constants and leave TODO comments for config extraction.

3. **Choose a partitioning strategy appropriate for “early hooks”**
   - Prefer partitioning by the most stable available caller identity:
     - authenticated user ID if available,
     - otherwise tenant/company ID if available,
     - otherwise remote IP as fallback.
   - If feasible, compose a partition key like:
     - `company:{companyId}:user:{userId}`
     - fallback `ip:{remoteIp}`
   - Do **not** block implementation on perfect tenant-aware partitioning if the current auth/tenant plumbing is incomplete; use a safe fallback and document it.
   - Keep the partition key generation encapsulated in a helper/local function so future Redis/distributed migration is easy.

4. **Add rejection handling**
   - Configure `OnRejected` for the rate limiter.
   - Return HTTP `429 Too Many Requests`.
   - Reuse the project’s standard error envelope if one exists; otherwise return a minimal JSON payload such as:
     - error code,
     - message,
     - optional retry hint.
   - Include `Retry-After` header if practical based on limiter metadata.
   - Log a structured warning with available context:
     - path,
     - policy name if available,
     - user ID,
     - tenant/company ID,
     - correlation/trace ID.

5. **Wire middleware into the pipeline**
   - Ensure `app.UseRateLimiter()` is added in the correct order in the ASP.NET Core pipeline.
   - Preserve existing auth/tenant middleware ordering as much as possible.
   - If partitioning depends on authenticated identity, ensure authentication runs before rate limiting if required by the chosen approach.
   - Be careful not to break health endpoints or internal infrastructure endpoints.

6. **Apply policies to chat and task endpoints**
   - For controllers:
     - use `[EnableRateLimiting("chat")]` and `[EnableRateLimiting("tasks")]` on controllers/actions as appropriate.
   - For minimal APIs:
     - use `.RequireRateLimiting("chat")` / `.RequireRateLimiting("tasks")`.
   - Apply only to the relevant endpoints:
     - direct agent chat/message send endpoints,
     - task creation/update/execute endpoints most likely to be abused or expensive.
   - Avoid applying to read-only low-risk endpoints unless clearly appropriate.
   - Do not rate limit health checks.

7. **Keep the design observability-friendly**
   - Ensure rate-limited requests are visible in logs as technical events.
   - Reuse existing correlation ID/tenant logging enrichers if present.
   - Add concise comments where future work is expected:
     - distributed enforcement with Redis,
     - per-plan quotas,
     - endpoint-specific tuning,
     - separate policies for reads vs writes.

8. **Add tests**
   - If API integration tests exist, add coverage that:
     - repeated requests to a protected endpoint eventually return 429,
     - unprotected endpoints are unaffected,
     - health endpoints remain accessible,
     - response shape for 429 is consistent.
   - If no integration test harness exists, add the smallest reasonable unit/integration coverage around:
     - policy registration helper,
     - partition key generation,
     - rejection response behavior.
   - Do not create an oversized test framework just for this task.

9. **Document implementation notes in code**
   - Add short comments/TODOs indicating:
     - this is an early hook,
     - current limiter is in-memory/local-instance,
     - future production scale should move to Redis/distributed coordination per architecture guidance.

Implementation guidance:
- Favor built-in ASP.NET Core rate limiting over custom middleware.
- Keep changes localized to API composition and endpoint annotations.
- Avoid introducing domain/application-layer dependencies for this task.
- Do not leak internal exception details in 429 responses.
- If there is already a centralized problem-details implementation, integrate with it rather than inventing a new response contract.

# Validation steps
Run the relevant local validation after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual/API validation:
   - Start the API locally.
   - Identify one chat endpoint and one task endpoint protected by the new policies.
   - Send repeated requests quickly and confirm:
     - initial requests succeed,
     - threshold-exceeding requests return `429 Too Many Requests`,
     - response body is safe and consistent,
     - `Retry-After` is present if implemented.
   - Confirm non-protected endpoints still behave normally.
   - Confirm health endpoints are not rate limited.

4. Logging validation:
   - Trigger a 429 and verify a structured warning/error log is emitted with request path and available tenant/user/correlation context.

5. Pipeline validation:
   - Confirm authentication/authorization still works on protected endpoints.
   - Confirm no startup errors from middleware ordering or missing package references.

Include in your final implementation summary:
- which endpoints were protected,
- which policies were added,
- what partition key strategy was used,
- whether the limiter is local-memory only,
- any follow-up recommendations.

# Risks and follow-ups
Risks:
- **Incorrect middleware ordering** could cause partitioning to miss authenticated identity or interfere with auth.
- **Overly aggressive limits** may degrade developer testing or normal usage.
- **In-memory limiter only** will not enforce globally across multiple app instances.
- **Endpoint discovery gaps** may leave some chat/task routes unprotected if routing is spread across modules.
- **Tenant partition ambiguity** may cause throttling to be user-based, tenant-based, or IP-based depending on current plumbing.

Follow-ups to note:
- Move limiter settings to configuration if initially hardcoded.
- Add **Redis-backed/distributed rate limiting** aligned with the architecture’s Redis role.
- Introduce separate policies for:
  - chat send vs chat read,
  - task create/update vs task query,
  - authenticated API vs anonymous/public endpoints.
- Consider plan-aware quotas and tenant-level burst controls later.
- Add dashboards/metrics for:
  - rate-limited request counts,
  - top affected endpoints,
  - top affected tenants/users,
  - 429 trends over time.
- Revisit whether background-triggering endpoints need stricter limits than ordinary CRUD endpoints.