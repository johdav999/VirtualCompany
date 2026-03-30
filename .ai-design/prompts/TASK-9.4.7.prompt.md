# Goal

Implement `TASK-9.4.7` for `ST-304 Grounded context retrieval service` by adding Redis caching for low-risk repeated grounded-context retrievals where appropriate.

The implementation prompt should direct a coding agent to:

- identify the existing grounded context retrieval flow in the .NET solution
- add a safe, tenant-aware Redis-backed cache layer for deterministic, low-risk retrieval results
- cache only retrieval components that are appropriate for reuse
- preserve permission/scope correctness and auditability
- avoid caching anything that could leak tenant data, bypass authorization, or make retrieval nondeterministic in unsafe ways

Because no explicit acceptance criteria were provided for this task, derive implementation behavior from the story, architecture, and backlog notes:
- retrieval must remain scoped by company/tenant and agent permissions
- returned context must stay prompt-ready and structured
- source references must still be available for downstream audit/explanation
- caching should be conservative, deterministic, and testable

# Scope

In scope:

- Add Redis caching to the grounded context retrieval service for repeated low-risk retrievals.
- Cache only retrieval outputs that are:
  - read-only
  - deterministic for the same scoped inputs
  - safe to reuse for a short TTL
  - not user-personalized beyond tenant/agent/scope inputs
- Ensure cache keys include all relevant isolation dimensions, such as:
  - company/tenant
  - agent
  - retrieval section/type
  - normalized query/request inputs
  - scope/access filters
  - versioning salt if needed
- Add configuration for enabling/disabling retrieval caching and TTLs.
- Add tests for:
  - tenant isolation
  - cache hit/miss behavior
  - bypass on unsafe/non-cacheable requests
  - invalidation/versioning behavior if applicable

Out of scope unless already trivial in the existing codebase:

- broad caching across unrelated modules
- changing retrieval ranking algorithms
- redesigning prompt assembly
- introducing a new distributed cache abstraction if one already exists and can be reused
- aggressive invalidation orchestration across all knowledge/memory writes unless there is already a simple hook point
- caching sensitive ephemeral conversation state or approval-sensitive execution state

Use a conservative approach: if a retrieval path is not clearly low-risk, do not cache it.

# Files to touch

Start by inspecting these likely areas and adjust based on actual code organization:

- `src/VirtualCompany.Application/**`
  - grounded context retrieval service interfaces and handlers
  - query/DTO models for retrieval requests and responses
  - any orchestration-facing application services
- `src/VirtualCompany.Infrastructure/**`
  - Redis cache implementation or distributed cache registration
  - serialization helpers
  - infrastructure DI wiring
  - repository/query services used by retrieval
- `src/VirtualCompany.Api/**`
  - configuration binding and service registration if cache settings are wired here
- `README.md`
  - only if there is an established pattern of documenting runtime configuration

Potential file patterns to look for:

- `*ContextRetriev*`
- `*RetrievalService*`
- `*Knowledge*`
- `*Memory*`
- `*Orchestration*`
- `*Redis*`
- `*Cache*`
- `DependencyInjection.cs`
- `Program.cs`
- `appsettings*.json`

If tests exist, update or add tests under the corresponding test projects for:
- application retrieval behavior
- infrastructure cache behavior
- integration tests if available

# Implementation plan

1. **Locate the grounded context retrieval path**
   - Find the service used by ST-304 that composes context from:
     - documents
     - memory
     - recent tasks
     - relevant records
   - Identify the main request/response contract and where source references are produced.
   - Determine whether retrieval is already split into subqueries/sections. Prefer caching at the section level if that reduces risk.

2. **Identify cacheable retrieval segments**
   - Mark as cacheable only segments that are low-risk and repeated, for example:
     - semantic document retrieval for the same normalized query and scope
     - memory retrieval for the same agent/company/query and stable filters
     - recent-history retrieval only if it is already bounded and acceptable to be slightly stale for a short TTL
   - Do **not** cache:
     - anything containing transient user-specific authorization decisions not represented in the key
     - mutable workflow/action state where staleness could affect approvals or execution safety
     - anything with nondeterministic ranking unless the request is normalized and deterministic enough for safe reuse

3. **Design a tenant-safe cache key strategy**
   - Create a dedicated cache key builder for grounded retrieval.
   - Include at minimum:
     - cache namespace prefix, e.g. `grounded-context`
     - schema/version token
     - `companyId`
     - `agentId` if applicable
     - retrieval section/type
     - normalized query text or request hash
     - relevant scope/access filters
     - top-k/limit parameters
     - any model/version discriminator that affects result shape
   - Ensure keys never omit tenant/company context.
   - Prefer hashing long normalized request payloads rather than storing raw text in keys.

4. **Add cache settings**
   - Introduce strongly typed options for retrieval caching, e.g.:
     - enabled flag
     - default TTL
     - per-section TTLs if useful
     - max payload size guard if practical
   - Bind from configuration in the API/infrastructure startup path.
   - Keep defaults conservative.

5. **Implement cache abstraction usage**
   - Reuse existing Redis/distributed cache infrastructure if present.
   - If no suitable abstraction exists, use the project’s standard .NET caching approach, preferably `IDistributedCache` backed by Redis already configured in infrastructure.
   - Add helper methods for:
     - get-or-create async
     - JSON serialization/deserialization
     - safe failure behavior
   - Cache failures must degrade gracefully to live retrieval, not fail the request.

6. **Integrate caching into retrieval flow**
   - Wrap only the selected low-risk retrieval segments or the final composed result if and only if the full request is clearly safe and deterministic.
   - Preserve existing behavior on cache miss.
   - Preserve source references exactly as before.
   - Ensure the response remains structured and prompt-ready.
   - Add lightweight logging/telemetry for cache hit/miss/bypass if there is an existing observability pattern.

7. **Handle invalidation conservatively**
   - Prefer short TTLs first.
   - If there are obvious existing write points for:
     - knowledge document ingestion completion
     - memory item creation/update/delete
     - task history updates used by retrieval
     then add targeted invalidation only if straightforward and low-risk.
   - If targeted invalidation is not already easy, use versioned keys or short TTLs rather than building a fragile invalidation system.
   - Document the chosen tradeoff in code comments where helpful.

8. **Protect correctness and security**
   - Verify that cache entries cannot be reused across tenants.
   - Verify that agent scope and access filters are part of the cache identity.
   - Do not cache raw secrets, tokens, or unsafe internal state.
   - Ensure authorization/scope checks still happen before or as part of determining cacheability and key composition.

9. **Add tests**
   - Unit tests for cache key composition:
     - different tenant => different key
     - different agent/scope/query => different key
   - Unit tests for retrieval caching behavior:
     - first request misses and stores
     - repeated request hits cache
     - non-cacheable request bypasses cache
     - cache deserialization failure falls back to live retrieval
   - Integration tests if feasible:
     - tenant isolation
     - Redis-backed path or distributed cache-backed path
   - Keep tests deterministic.

10. **Keep the implementation aligned with architecture**
    - Respect clean boundaries:
      - application defines retrieval behavior/contracts
      - infrastructure handles Redis/cache implementation details
    - Do not move prompt assembly into controllers/UI.
    - Keep the retrieval service deterministic and testable.

# Validation steps

1. Inspect and build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects, run them specifically for faster iteration.

4. Manually verify configuration wiring:
   - confirm retrieval cache options bind correctly
   - confirm app still starts when Redis is unavailable or cache is disabled, if that is the established resilience pattern

5. Validate behavior in code/tests for:
   - cache hit on repeated identical low-risk retrieval
   - cache miss on first request
   - cache bypass for unsafe/non-cacheable retrieval
   - different `companyId` never sharing entries
   - different agent scope/filter never sharing entries
   - source references preserved in cached responses
   - graceful fallback when cache read/write fails

6. Confirm no architectural regressions:
   - no tenant leakage
   - no direct DB access added from UI/API layers
   - no prompt assembly logic moved outside the application/orchestration boundary

# Risks and follow-ups

- **Risk: tenant or scope leakage through incomplete cache keys**
  - Mitigation: include company, agent, scope filters, and request hash in every key; add tests specifically for isolation.

- **Risk: stale retrieval results**
  - Mitigation: use short TTLs and cache only low-risk read paths; prefer conservative caching over broad caching.

- **Risk: caching nondeterministic or approval-sensitive data**
  - Mitigation: explicitly bypass caching for unstable or sensitive retrieval segments.

- **Risk: cache serialization drift**
  - Mitigation: version cache keys/prefixes and keep cached DTOs stable.

- **Risk: Redis dependency causing request failures**
  - Mitigation: fail open to live retrieval on cache errors.

Follow-ups to note if not completed in this task:
- targeted invalidation hooks on document ingestion completion and memory updates
- metrics dashboards for retrieval cache hit rate and latency savings
- cache payload size limits/compression if retrieval responses become large
- broader caching policy documentation for orchestration and retrieval subsystems