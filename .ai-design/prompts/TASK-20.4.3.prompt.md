# Goal

Implement backlog task **TASK-20.4.3 — Add finance API middleware/service fallback for missing dataset detection and structured not-initialized responses** for story **US-20.4 ST-FUI-412/ST-FUI-413**.

Deliver a production-ready vertical slice in the existing **.NET modular monolith** so that finance-dependent API requests no longer fail when finance seed data is missing. Instead, they must either:

- return a **structured `not_initialized` response**, or
- trigger finance seeding through a **configured fallback path**,

while ensuring:

- no unhandled exceptions occur for missing finance data,
- manual and fallback seeding both use the **same idempotent orchestration service**,
- audit/log events are emitted consistently,
- the `/finance` UI can determine whether to show **Generate finance data** or **Regenerate finance data**.

Keep the implementation aligned with the architecture: ASP.NET Core backend, Blazor web frontend, PostgreSQL, tenant-scoped services, CQRS-lite, and auditability as a domain feature.

# Scope

In scope:

- Add backend detection for whether finance seed data is initialized for the current tenant/company.
- Add or extend a shared finance seeding orchestration service that supports at least:
  - `replace`
  - optional `append` only if already partially present in codebase and can be clearly distinguished
- Ensure the service is **idempotent** and reusable by:
  - manual UI/API trigger
  - automatic fallback path from finance-dependent APIs
- Add structured API response contract for missing finance data:
  - machine-readable status/code such as `not_initialized`
  - enough metadata for UI to render appropriate actions
- Add middleware/filter/service-layer handling so finance-dependent endpoints do not throw when data is absent.
- Add backend endpoint/query for current finance seeding state if not already present.
- Update `/finance` UI to show:
  - `Generate finance data` when not initialized
  - `Regenerate finance data` when initialized
- Add explicit confirmation UX before overwrite when manual trigger uses `replace`.
- Emit audit/business events and structured logs for:
  - manual seed requested
  - fallback seed requested
  - seed completed / skipped / failed
  - not-initialized response returned

Out of scope unless already trivial and clearly supported by existing code:

- Broad redesign of finance domain
- New background job framework
- Full append-mode UX if append mode does not already exist
- Large refactors unrelated to finance seeding
- Mobile app changes

# Files to touch

Inspect the repo first and then update the exact files that match existing patterns. Likely areas:

- `src/VirtualCompany.Api/**`
  - finance controllers/endpoints
  - middleware / exception mapping / endpoint filters
  - DI registration / options binding
  - response DTOs/contracts
- `src/VirtualCompany.Application/**`
  - finance seeding orchestration service
  - commands/queries/handlers
  - finance initialization state query
  - fallback policy service
  - audit event application services
- `src/VirtualCompany.Domain/**`
  - finance-related domain abstractions
  - enums/value objects for seed mode / initialization state / fallback behavior
- `src/VirtualCompany.Infrastructure/**`
  - persistence checks for finance dataset existence
  - repository implementations
  - audit/log persistence wiring if needed
- `src/VirtualCompany.Web/**`
  - `/finance` page/component
  - confirmation dialog/modal
  - API client calls
  - state-based action label rendering
- `src/VirtualCompany.Shared/**`
  - shared contracts if API/web share DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests for structured `not_initialized`
  - fallback behavior tests
  - no-unhandled-exception regression tests
- potentially application/integration test projects if present for finance services

Also inspect:

- `README.md`
- any finance module docs/config files
- existing options classes for feature flags/fallback behavior
- existing audit event patterns and exception handling patterns

# Implementation plan

1. **Discover existing finance flow before coding**
   - Find all finance endpoints, services, repositories, and UI pages.
   - Identify where finance-dependent requests currently fail when seed data is absent.
   - Identify whether there is already:
     - a finance seed service,
     - a seed endpoint,
     - a finance status endpoint/query,
     - append/replace semantics,
     - audit event infrastructure,
     - global exception mapping.

2. **Define explicit initialization model**
   - Introduce or reuse a clear tenant-scoped finance initialization concept, for example:
     - `NotInitialized`
     - `Initialized`
     - optional `Initializing` if already supported
   - Add a lightweight detection service such as:
     - `IFinanceInitializationService`
     - `GetStatusAsync(companyId, cancellationToken)`
     - `EnsureInitializedOrHandleFallbackAsync(...)`
   - Detection should be based on durable persisted finance dataset presence, not UI assumptions.

3. **Create/extend shared idempotent seed orchestration service**
   - Implement or refactor to a single application service used by both manual and fallback paths.
   - Service should accept:
     - tenant/company context
     - trigger source (`manual`, `fallback`)
     - seed mode (`replace`, optional `append`)
     - actor metadata if available
     - correlation ID
   - Idempotency requirements:
     - repeated requests should not duplicate data unexpectedly
     - replace should overwrite in a controlled way
     - append should only be exposed if semantics are already safe and clear
   - Return a structured result including:
     - status
     - mode
     - whether work was performed or skipped
     - resulting initialization state
     - any user-safe message

4. **Add configurable fallback behavior**
   - Introduce options/config for finance-dependent APIs, e.g.:
     - `ReturnNotInitialized`
     - `TriggerSeedThenContinue` or `TriggerSeed`
   - Prefer existing options/config conventions in the repo.
   - If synchronous seeding is too risky for request path, support:
     - trigger seed and return structured response indicating initialization has started/pending
   - Do not introduce long blocking request behavior unless existing architecture already supports it safely.

5. **Implement structured `not_initialized` API response**
   - Add a consistent response contract for finance-dependent endpoints when data is missing.
   - Example shape, adapt to repo conventions:
     - `code: "not_initialized"`
     - `message`
     - `module: "finance"`
     - `canGenerate: true`
     - `recommendedAction: "generate" | "regenerate"`
     - `supportedModes: ["replace"]` plus `append` only if implemented
     - `fallbackTriggered: bool`
   - Use proper HTTP status based on existing API conventions; prefer consistency over inventing a new pattern.
   - Ensure this response is machine-readable and stable for UI consumption.

6. **Prevent unhandled exceptions in finance-dependent requests**
   - Add a guard at the correct layer:
     - preferably service/application layer for finance-dependent use cases,
     - optionally endpoint filter/middleware for repeated endpoint pattern.
   - Convert missing-data conditions into:
     - structured `not_initialized` response, or
     - fallback seed invocation + safe response.
   - Do not rely on catching generic exceptions late if a deterministic pre-check can be done.

7. **Expose finance seeding state for UI**
   - Add or extend a query/endpoint that returns current finance initialization state for the tenant.
   - UI must be able to determine:
     - not initialized => show `Generate finance data`
     - initialized => show `Regenerate finance data`
   - Include supported seed modes and whether confirmation is required for replace.

8. **Implement manual trigger API**
   - Add or update finance seed endpoint for manual action.
   - Require explicit mode parameter:
     - at minimum `replace`
     - `append` only if truly supported
   - Ensure API contract clearly distinguishes modes.
   - Return structured result from the shared orchestration service.
   - Emit audit/log events with actor, tenant, mode, trigger source, outcome.

9. **Update `/finance` UI**
   - Load finance initialization state on page load.
   - Render action label based on state:
     - `Generate finance data`
     - `Regenerate finance data`
   - If user selects `replace`, require explicit confirmation before submitting.
   - If append exists, make it visually and textually distinct from replace.
   - Handle structured `not_initialized` responses from finance APIs gracefully:
     - show CTA to generate data
     - avoid generic error banners for this case

10. **Audit and logging**
    - Reuse existing business audit event patterns.
    - Emit events for:
      - manual seed requested
      - fallback seed requested
      - seed completed
      - seed skipped due to idempotency/no-op
      - seed failed
      - finance request returned `not_initialized`
    - Include tenant/company context, actor where available, trigger source, mode, correlation ID.
    - Keep technical logs separate from business audit records.

11. **Tests**
    - Add API tests covering:
      - finance-dependent endpoint returns structured `not_initialized` when dataset missing and fallback disabled
      - finance-dependent endpoint does not throw unhandled exception when dataset missing
      - fallback-enabled path invokes shared seed orchestration service
      - manual trigger uses same orchestration service as fallback
      - initialized state causes `/finance` UI-facing status endpoint to indicate regenerate
      - replace mode requires confirmation in UI logic/component tests if test setup exists
      - append mode only appears if actually implemented
    - Add application tests for idempotent orchestration behavior.

12. **Keep changes minimal and idiomatic**
    - Follow existing naming, folder structure, MediatR/CQRS patterns, DTO conventions, and DI registration style.
    - Do not create parallel abstractions if a finance seed service already exists; extend it.

# Validation steps

1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Backend behavior checks**
   - With finance dataset absent and fallback disabled:
     - call a finance-dependent API
     - verify safe structured `not_initialized` response
     - verify no unhandled exception / no 500 from missing data path
   - With finance dataset absent and fallback enabled:
     - call a finance-dependent API
     - verify shared seed orchestration service is invoked
     - verify response matches configured behavior
   - With finance dataset present:
     - call finance-dependent APIs
     - verify normal behavior unchanged

3. **Manual seed checks**
   - Call manual finance seed endpoint in `replace` mode
   - Verify:
     - success response is structured
     - audit/log events are emitted
     - repeated call behaves idempotently/safely per design

4. **UI checks**
   - Open `/finance`
   - Verify:
     - no data => `Generate finance data`
     - seeded => `Regenerate finance data`
   - Trigger regenerate in replace mode
   - Verify explicit confirmation is required before overwrite
   - If append mode exists, verify it is clearly distinct in UI and API payloads

5. **Regression checks**
   - Confirm tenant scoping is preserved
   - Confirm no unrelated finance endpoints changed behavior unexpectedly
   - Confirm correlation/audit metadata still flows through request path

# Risks and follow-ups

- **Risk: unclear existing finance seed architecture**
  - Mitigation: inspect current implementation first and extend rather than replace.

- **Risk: fallback seeding in request path may be slow**
  - Mitigation: prefer configurable behavior and safe structured response if synchronous completion is not appropriate.

- **Risk: duplicate or unsafe reseeding**
  - Mitigation: centralize all paths through one idempotent orchestration service.

- **Risk: UI/backend contract drift**
  - Mitigation: define one stable structured `not_initialized` response and one finance status contract.

- **Risk: append mode ambiguity**
  - Mitigation: only expose append if semantics already exist and are safe; otherwise support replace only.

- **Risk: missing audit consistency**
  - Mitigation: ensure both manual and fallback paths emit the same event family with different trigger sources.

Follow-ups to note in code comments or TODOs only if necessary:

- background/asynchronous seeding status tracking if fallback currently only triggers start
- richer initialization states such as `initializing` / `failed`
- dedicated integration tests for tenant isolation and audit persistence
- mobile handling of structured finance initialization responses if finance flows later surface there