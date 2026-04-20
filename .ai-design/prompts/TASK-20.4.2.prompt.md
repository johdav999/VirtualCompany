# Goal

Implement backlog task **TASK-20.4.2 — Manual seed API with controlled replace mode and shared orchestration path** for **US-20.4 ST-FUI-412/ST-FUI-413**.

Deliver a production-ready implementation in the existing **.NET modular monolith** that:

- Adds a manual finance seed/regenerate API used by the `/finance` UI.
- Supports at minimum a clearly modeled `replace` mode.
- Requires explicit confirmation for overwrite when `replace` is requested.
- Ensures finance-dependent APIs handle missing seeded data safely via:
  - structured `not_initialized` responses, or
  - configured fallback seeding behavior.
- Routes both manual and fallback seeding through the **same idempotent orchestration service**.
- Emits technical logs and business/audit events for both paths.
- Avoids unhandled exceptions when finance data is missing.

Keep the implementation aligned with the architecture:
- ASP.NET Core backend
- CQRS-lite application layer
- tenant-scoped behavior
- shared orchestration service
- auditability as a domain feature
- safe API error handling

# Scope

In scope:

- Backend API contract(s) for manual finance seeding/regeneration.
- Seed mode modeling, with `replace` required and `append` only if already natural to support.
- Confirmation guard for destructive replace operations.
- Shared application/service orchestration path for:
  - manual trigger
  - fallback trigger from finance-dependent flows
- Idempotency protections for seed orchestration.
- Structured result model for uninitialized finance state.
- Updates to finance-dependent endpoints/services so missing data is handled safely.
- Audit/log emission for seed attempts, starts, completions, skips, failures, and fallback-triggered runs.
- Tests covering API behavior, orchestration reuse, and missing-data handling.
- Minimal UI/API support contract needed so `/finance` can determine whether to show:
  - `Generate finance data`
  - `Regenerate finance data`

Out of scope unless already partially present and low-cost:

- Large UI redesign in Blazor.
- Full append-mode UX if append mode does not already exist.
- New background job framework.
- Broad refactors unrelated to finance seeding.
- Mobile changes.

# Files to touch

Inspect first, then modify the most relevant existing files in these areas.

Likely backend/API:
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`

Likely web/UI contract surface if `/finance` already exists:
- `src/VirtualCompany.Web/**`

Likely tests:
- `tests/VirtualCompany.Api.Tests/**`

Also inspect for existing patterns before coding:
- finance controllers/endpoints
- seed/sample-data services
- audit event services
- exception/ProblemDetails mapping
- tenant resolution patterns
- command/query handlers
- existing idempotency or distributed lock helpers
- existing finance initialization checks

If migrations/schema changes are required, use the project’s established migration approach and update any relevant docs or migration artifacts.

# Implementation plan

1. **Discover existing finance seeding and initialization flow**
   - Find current finance module endpoints, services, and any seed/sample-data logic.
   - Identify how `/finance` currently determines seeded state.
   - Identify finance-dependent APIs that assume seeded data exists and may currently throw.
   - Find existing audit event and structured error response patterns.
   - Find whether append mode already exists anywhere; do not invent it unless it fits naturally.

2. **Define explicit seed contracts**
   - Introduce or refine request/response DTOs for manual seeding.
   - Model seed mode explicitly, e.g. enum/value object with at least:
     - `replace`
     - optional `append` only if implemented
   - Include an explicit confirmation field for destructive replace, such as:
     - `confirmReplace: true`
     - and/or a confirmation token/string if the codebase already uses that pattern
   - Add a response contract that returns:
     - current status
     - whether data existed before
     - mode used
     - whether operation started/performed/skipped
     - correlation/idempotency reference if available

3. **Implement a shared finance seed orchestration service**
   - Create or refactor to a single application service used by both:
     - manual API trigger
     - fallback trigger from finance-dependent requests
   - The service should:
     - be tenant-aware
     - be idempotent
     - detect current seeded state
     - enforce replace confirmation rules for manual destructive operations
     - no-op or safely reuse in-progress/completed work where appropriate
     - emit logs/audit events consistently
   - Keep HTTP concerns out of the orchestration service.

4. **Add controlled replace behavior**
   - If finance data already exists and mode is `replace`, require explicit confirmation.
   - Return a safe validation/business response when confirmation is missing.
   - Ensure replace semantics are clearly distinct from any append semantics in both API naming and response payloads.
   - If append is not implemented, do not expose it as supported.

5. **Add or update manual seed API endpoint(s)**
   - Add endpoint(s) under the existing finance API area, following current routing conventions.
   - Ensure the endpoint:
     - is tenant-scoped
     - authorizes appropriately
     - accepts the seed mode request
     - rejects replace without confirmation
     - calls the shared orchestration service
     - returns a structured response, not ad hoc strings
   - Also expose or update a finance status/readiness endpoint if needed so the UI can decide between:
     - `Generate finance data`
     - `Regenerate finance data`

6. **Handle missing finance data safely in dependent APIs**
   - Identify finance APIs that require seeded data.
   - Replace exception-prone assumptions with explicit readiness checks.
   - For each affected flow, implement configured behavior:
     - return structured `not_initialized` response, or
     - trigger fallback seeding through the same orchestration service
   - Ensure no unhandled exceptions occur when data is absent.
   - Keep fallback behavior explicit and testable; avoid hidden side effects without logging/audit.

7. **Define structured `not_initialized` response**
   - Reuse existing API error envelope conventions if present.
   - Include machine-readable fields such as:
     - `code: "not_initialized"`
     - `domain: "finance"`
     - `message`
     - optional `canTriggerSeed`
     - optional `fallbackTriggered`
     - optional `statusEndpoint` / `seedEndpoint`
   - Ensure consumers can distinguish this from generic validation or server errors.

8. **Emit audit and log events**
   - For both manual and fallback paths, emit:
     - seed requested
     - seed started
     - seed skipped/already initialized or idempotent reuse
     - seed completed
     - seed failed
   - Include tenant context and correlation IDs where supported.
   - Persist business audit events through the audit module/pattern already used in the codebase.
   - Keep technical logs separate from business audit records.

9. **Preserve idempotency and concurrency safety**
   - Prevent duplicate destructive or duplicate concurrent seed runs for the same tenant/context.
   - Reuse existing locking/idempotency primitives if available.
   - If no primitive exists, implement the smallest safe mechanism consistent with the architecture.
   - Make sure fallback-triggered requests do not stampede into multiple seed executions.

10. **Update UI-facing status contract only as needed**
   - If the web app already calls a finance status endpoint, extend it with a clear seeded/unseeded flag.
   - If no such endpoint exists, add a minimal query endpoint or include status in an existing finance summary response.
   - Keep UI changes minimal and contract-driven.

11. **Add tests**
   - Unit tests for orchestration service:
     - manual replace without confirmation rejected
     - manual replace with confirmation succeeds
     - idempotent repeated trigger behavior
     - fallback path uses same service
   - API/integration tests for:
     - manual seed endpoint request/response
     - structured `not_initialized` response
     - finance-dependent endpoint does not throw when uninitialized
     - fallback behavior if configured
     - audit/log side effects where testable
   - If web contract is touched, add focused tests for seeded-state action selection if the project already has that test style.

12. **Keep implementation clean and aligned**
   - Follow existing naming, folder, and handler patterns.
   - Prefer command/query handlers over controller-heavy logic.
   - Avoid leaking infrastructure concerns into domain/application contracts.
   - Do not introduce speculative abstractions beyond what this task needs.

# Validation steps

1. Inspect and build baseline:
   - `dotnet build`

2. Run relevant tests before changes if practical:
   - `dotnet test`

3. After implementation, verify:
   - Manual finance seed endpoint exists and is tenant-scoped.
   - `replace` mode is supported and clearly modeled.
   - Replace without explicit confirmation returns a safe structured validation/business response.
   - Replace with confirmation succeeds.
   - If append is present, it is clearly distinguished in API contract and behavior.
   - Finance status/readiness contract allows UI to choose generate vs regenerate.
   - Finance-dependent APIs return structured `not_initialized` or trigger fallback according to configuration.
   - Missing finance data no longer causes unhandled exceptions.
   - Manual and fallback paths call the same orchestration service.
   - Audit/log events are emitted for both paths.

4. Run full test suite:
   - `dotnet test`

5. If web contract changed, manually verify `/finance` behavior in the existing app flow if feasible:
   - unseeded tenant shows generate action
   - seeded tenant shows regenerate action
   - replace action requires explicit confirmation before overwrite

# Risks and follow-ups

- **Existing seeding logic may be scattered** across API, application, and infrastructure layers; consolidate carefully without breaking current flows.
- **Fallback behavior may be ambiguous** in current code/config. If no explicit configuration exists, implement the smallest clear default and document it in code comments or config.
- **Idempotency/concurrency** is the main correctness risk; avoid duplicate seed runs under concurrent requests.
- **Audit event schema/patterns** may already exist; reuse them rather than inventing parallel logging.
- **UI contract drift** is possible if `/finance` already depends on a specific response shape; preserve backward compatibility where reasonable.
- **Append mode** should not be half-exposed. Only support it if fully implemented and clearly differentiated.
- Follow-up work may include:
  - richer seed job status/progress reporting
  - explicit admin permissions for manual regenerate
  - background execution with polling if seeding becomes long-running
  - more formal idempotency keys and distributed locking
  - UI confirmation dialog polish and audit history surfacing