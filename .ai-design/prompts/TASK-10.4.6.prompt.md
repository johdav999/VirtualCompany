# Goal
Implement Redis-backed coordination primitives for resilient background execution under **TASK-10.4.6: Redis can coordinate locks and ephemeral execution state** in support of **ST-404 Escalations, retries, and long-running background execution**.

The coding agent should add a production-ready foundation in the .NET solution for:

- distributed locking for scheduled jobs and workflow/background runners
- short-lived execution state storage in Redis for long-running/background work
- tenant-aware and correlation-aware execution coordination
- safe lock acquisition/release semantics with TTLs
- idempotent-friendly coordination hooks for retries and worker execution
- health/registration wiring so Redis-backed coordination is usable by application/infrastructure services

This task is infrastructure/application focused, not UI focused.

# Scope
Include:

- A Redis coordination abstraction in application/infrastructure layers
- Distributed lock support with:
  - lock key generation
  - TTL/lease duration
  - owner token/value
  - safe release only by owner
  - optional renewal/heartbeat support if practical within current architecture
- Ephemeral execution state support with:
  - set/get/delete
  - TTL-based expiration
  - structured payload storage
  - tenant-scoped keys
  - correlation/execution identifiers
- Dependency injection registration and configuration
- Health-check integration if the project already has dependency health checks wired
- Logging with correlation and tenant context where available
- Unit/integration tests for key behavior
- Minimal documentation/comments where needed for future worker usage

Do not include:

- Full workflow engine implementation
- Full retry engine redesign
- UI changes
- New external broker infrastructure
- Large refactors unrelated to Redis coordination
- Business-specific escalation UX

Assume Redis is already an architectural dependency or planned dependency; if missing, add only the minimal package/configuration needed for this task.

# Files to touch
Inspect the solution first, then likely touch files in these areas as appropriate:

- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Api/**`
- `README.md` if a short infrastructure note is warranted
- test projects related to Application/Infrastructure if present

Likely concrete targets include:

- infrastructure dependency registration/extensions
- Redis client setup
- background execution coordination service implementations
- options/config classes
- health check registration
- tests for lock/state behavior

If equivalent files already exist, extend them instead of creating parallel patterns.

# Implementation plan
1. **Inspect existing architecture and conventions**
   - Review current DI registration, configuration binding, health checks, logging, and any existing Redis usage.
   - Identify whether the solution already uses `StackExchange.Redis`, `IDistributedCache`, or custom cache abstractions.
   - Reuse existing patterns for options, service registration, and tenant/correlation propagation.

2. **Define coordination abstractions in the application layer**
   Create small, focused interfaces that infrastructure can implement. Prefer names aligned with current conventions, for example:
   - distributed lock service/provider
   - ephemeral execution state store
   - optional execution coordination facade if the codebase prefers a single entry point

   The abstractions should support:
   - acquiring a lock by logical key with lease duration
   - releasing a lock only if held by the same owner token
   - optionally renewing/extending a lock lease
   - reading/writing/deleting ephemeral execution state by scoped key
   - TTL on execution state entries
   - cancellation tokens on async methods

3. **Design key format carefully**
   Use deterministic, namespaced Redis keys that are safe for multi-tenant SaaS. Prefer a format like:
   - `vc:lock:{companyId}:{category}:{resource}`
   - `vc:execstate:{companyId}:{executionId}`
   - or equivalent consistent naming

   Requirements:
   - tenant-aware where state is tenant-owned
   - suitable for scheduled global jobs if some locks are system-wide
   - easy to inspect operationally
   - avoid leaking sensitive payloads in keys

4. **Implement distributed locking in infrastructure**
   Use Redis atomic primitives via `StackExchange.Redis`:
   - acquire with `SET key value NX PX/EX`
   - release safely with compare-and-delete semantics, ideally via Lua script
   - renew safely with owner-token verification if renewal is implemented

   Lock behavior expectations:
   - lock value should be a unique owner token (GUID/ULID/string)
   - acquisition returns a handle/result indicating success/failure and owner token
   - release must not delete another worker’s lock
   - lease duration must always be required or defaulted from options
   - log acquisition failures at debug/information level, not as errors unless unexpected

5. **Implement ephemeral execution state store**
   Add a Redis-backed store for short-lived execution metadata for long-running jobs/workflows. Store JSON payloads with TTL.

   Suggested capabilities:
   - set state with TTL
   - get typed state
   - remove state
   - optionally update/refresh TTL
   - optionally check existence

   Suggested payload use cases:
   - execution heartbeat/progress
   - retry attempt metadata
   - worker ownership/correlation markers
   - transient orchestration context that should not live in SQL

   Keep this explicitly ephemeral and non-authoritative; PostgreSQL remains source of truth for durable business state.

6. **Add configuration/options**
   Introduce options for Redis coordination, such as:
   - key prefix
   - default lock lease duration
   - default execution state TTL
   - connect timeout/retry settings only if not already configured elsewhere

   Bind from configuration using existing app conventions. Do not hardcode environment-specific values.

7. **Register services in DI**
   Wire the new services in infrastructure registration and expose them to application/background worker consumers.
   If the solution already has a background worker module, ensure the services are available there without introducing circular dependencies.

8. **Integrate health checks if appropriate**
   If health checks already exist per ST-104 patterns, ensure Redis coordination depends on the same Redis connection and is covered by health reporting.
   Avoid duplicating Redis registrations.

9. **Add logging and observability**
   Ensure logs include:
   - lock key/category/resource where safe
   - company/tenant context where available
   - correlation/execution ID where available
   - acquisition success/failure
   - release/renew anomalies

   Do not log sensitive execution payload contents.

10. **Add tests**
   Add tests covering at minimum:
   - successful lock acquisition
   - failed acquisition when already locked
   - safe release only by owner
   - lock expiry behavior if testable
   - execution state set/get/delete
   - TTL/expiration behavior where practical
   - tenant key isolation behavior

   Prefer focused tests around abstractions and infrastructure behavior. If true Redis integration tests are too heavy for current setup, add unit tests around key generation/logic and note integration-test follow-up.

11. **Document intended usage**
   Add concise comments or README notes showing intended consumers:
   - scheduler singleton execution
   - workflow progression coordination
   - retry worker de-duplication
   - long-running task heartbeat/progress state

12. **Keep implementation incremental**
   Do not overbuild a full job framework. Deliver a reusable Redis coordination foundation that later tasks can consume.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run those specifically after implementation.

4. Verify configuration compiles cleanly:
   - no missing options binding
   - no duplicate Redis registrations
   - no broken DI graph

5. Manually review implementation against task intent:
   - distributed lock uses atomic acquire
   - release is owner-safe
   - execution state is TTL-based and ephemeral
   - keys are tenant-aware/namespaced
   - services are infrastructure-backed and injectable

6. If health checks exist, verify Redis health registration still works and does not create conflicting clients.

7. Confirm no UI/mobile changes were introduced.

# Risks and follow-ups
- **Risk: duplicate Redis abstractions**
  - The repo may already have caching or Redis wrappers. Reuse/extend existing patterns instead of adding a second competing client abstraction.

- **Risk: lock correctness**
  - Naive delete-on-release can break distributed safety. Use owner-token compare-and-delete semantics.

- **Risk: TTL mismatch**
  - Too-short leases can cause duplicate execution; too-long leases can delay recovery. Make lease durations configurable.

- **Risk: overusing Redis for durable state**
  - Keep Redis limited to coordination and ephemeral execution state. Durable workflow/task truth must remain in PostgreSQL.

- **Risk: tenant leakage in keys**
  - Ensure tenant-owned execution state and locks include tenant scope where applicable.

- **Risk: missing renewal support**
  - If long-running jobs exceed lease duration, add a follow-up task for lock heartbeat/renewal if not implemented now.

Suggested follow-ups after this task:
- integrate lock usage into scheduler/workflow runners
- add execution heartbeat/progress reporting for long-running jobs
- add retry coordination/idempotency helpers
- add operational dashboards/metrics for lock contention and stale execution state