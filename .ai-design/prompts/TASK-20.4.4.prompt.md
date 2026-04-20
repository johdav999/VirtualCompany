# Goal

Implement backlog task **TASK-20.4.4 — Record audit events and logs for manual regenerate and fallback-triggered seeding** for story **US-20.4 ST-FUI-412/ST-FUI-413**.

Ensure that both:
- **manual finance seed/regenerate actions** from `/finance`, and
- **automatic fallback-triggered seeding** when finance-dependent APIs detect missing seeded data

use the **same idempotent seed orchestration service** and emit:
- **business audit events** persisted through the audit domain/module, and
- **structured technical logs** with correlation and tenant/company context.

This task is specifically about **auditability and observability**, not inventing a separate seeding path.

# Scope

In scope:
- Add or extend audit event emission for:
  - manual finance seed trigger
  - manual finance regenerate trigger
  - fallback-triggered finance seeding
  - seed orchestration completion/failure outcomes where appropriate
- Add structured logging around the same flows
- Ensure manual and fallback paths both call the same orchestration service
- Include enough metadata in audit/log records to distinguish:
  - trigger source (`manual` vs `fallback`)
  - mode (`replace`, and `append` only if already implemented)
  - actor type/id where available
  - company/tenant context
  - outcome/status
  - correlation/idempotency identifiers if available
- Preserve safe behavior for finance-dependent APIs when data is missing:
  - structured `not_initialized` response or configured fallback behavior
  - no unhandled exceptions

Out of scope unless required by existing code coupling:
- Building the full `/finance` UI from scratch
- Designing a new seeding architecture separate from the existing orchestration service
- Broad audit UI work beyond what is necessary for persistence/contracts/tests
- Implementing append mode if it does not already exist

# Files to touch

Inspect first, then update only the minimal necessary set. Likely areas:

- `src/VirtualCompany.Api/...`
  - finance endpoints/controllers/minimal API handlers
  - exception/response mapping for finance-dependent APIs
- `src/VirtualCompany.Application/...`
  - finance seeding orchestration service
  - commands/handlers for manual trigger
  - fallback handling service/pipeline
  - audit event application services/contracts
- `src/VirtualCompany.Domain/...`
  - audit event domain models/constants if strongly typed
  - finance seeding enums/value objects if needed
- `src/VirtualCompany.Infrastructure/...`
  - audit event persistence implementation
  - logging integration
  - repository updates if audit metadata needs persistence changes
- `src/VirtualCompany.Web/...`
  - only if needed to pass trigger source/mode metadata from `/finance`
- `tests/VirtualCompany.Api.Tests/...`
  - API tests for manual trigger and fallback behavior
- Additional test projects/files under `tests/...`
  - application/service tests for audit/log emission and shared orchestration usage

Also inspect:
- existing audit event schema/migrations or archived migration docs under `docs/postgresql-migrations-archive/`
- any existing finance seeding code paths to avoid duplicating orchestration logic

# Implementation plan

1. **Discover existing finance seeding flow**
   - Find current finance seed/regenerate endpoints, handlers, and services.
   - Identify the canonical/idempotent finance seed orchestration service.
   - Identify where finance-dependent APIs currently detect missing seeded data and whether fallback behavior already exists.
   - Identify current audit event and logging patterns used elsewhere in the codebase.

2. **Define/standardize event semantics**
   - Reuse existing audit infrastructure if present.
   - Add clear action names/constants for finance seeding events, for example:
     - `finance.seed.manual_requested`
     - `finance.seed.fallback_requested`
     - `finance.seed.completed`
     - `finance.seed.failed`
   - Keep naming aligned with existing conventions if the project already has a pattern.
   - Ensure audit payload/metadata can capture:
     - `triggerSource`
     - `mode`
     - `seedStateBefore` if available
     - `fallbackBehavior`
     - `correlationId`
     - `idempotencyKey` if available
     - `reason` for fallback trigger
   - Do not expose chain-of-thought; rationale should remain concise and operational.

3. **Route manual and fallback through the same orchestration service**
   - Refactor only if necessary so both paths invoke the same application service method.
   - Prefer a single request model such as a finance seed orchestration command with fields like:
     - company/tenant id
     - trigger source
     - mode
     - actor context
     - correlation/idempotency context
   - Ensure idempotency behavior remains intact.

4. **Emit business audit events**
   - On manual trigger:
     - create audit event recording actor, action, target, trigger source, mode, and initial outcome such as requested/started
   - On fallback trigger:
     - create audit event recording system actor, action, target, trigger source, and why fallback was invoked
   - On completion/failure:
     - emit outcome audit event or update pattern consistent with existing audit design
   - If the audit model supports `data_sources_used`, populate only when meaningful; otherwise keep concise metadata in structured payload fields.

5. **Add structured technical logging**
   - Add `ILogger` logs at key points:
     - manual request received
     - fallback seeding initiated due to missing data
     - orchestration skipped due to idempotency/already initialized
     - orchestration completed
     - orchestration failed
   - Include structured properties, not interpolated-only strings, for:
     - company id
     - actor id/type
     - trigger source
     - mode
     - correlation id
     - request/operation id
   - Follow existing logging conventions from ST-104 observability work.

6. **Preserve safe missing-data behavior**
   - Where finance-dependent APIs require seeded data:
     - return structured `not_initialized` when fallback is disabled, or
     - trigger fallback seeding according to configuration and return the expected safe response
   - Ensure missing data paths do not throw unhandled exceptions.
   - If exceptions are currently used internally, catch/map them into safe API/application responses.

7. **Update contracts only where necessary**
   - If manual trigger API already exists, extend request/response models minimally to carry mode and distinguish regenerate intent.
   - If append mode exists, ensure audit/log metadata distinguishes it clearly.
   - Do not introduce breaking API changes unless unavoidable.

8. **Add tests**
   - Application tests:
     - manual trigger emits audit event(s)
     - fallback trigger emits audit event(s)
     - both paths call the same orchestration service
     - idempotent no-op/stable path still logs/audits appropriately if expected by design
   - API tests:
     - finance-dependent endpoint returns structured `not_initialized` when applicable
     - fallback path does not throw unhandled exception
     - manual regenerate path records expected audit behavior
   - Prefer asserting persisted audit records and observable outcomes over implementation details.

9. **Keep implementation aligned with architecture**
   - Business audit events belong to the audit/explainability domain, not only app logs.
   - Technical logs remain separate from business audit persistence.
   - Respect tenant scoping throughout.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify code paths:
   - Manual finance seed/regenerate endpoint calls shared orchestration service
   - Fallback-triggered seeding path calls the same service
   - No duplicate seeding logic remains in controllers/handlers

4. Verify audit behavior:
   - Confirm audit records are created for:
     - manual trigger
     - fallback trigger
     - completion/failure if implemented in current audit pattern
   - Confirm audit records include tenant/company context and trigger source

5. Verify logging behavior:
   - Confirm structured logs exist for request/start/skip/complete/failure points
   - Confirm correlation/company context is included where the project supports it

6. Verify safe API behavior:
   - Finance-dependent API with missing data:
     - returns structured `not_initialized` when fallback is off, or
     - safely triggers fallback behavior without unhandled exception
   - Replace-mode manual regenerate still requires confirmation behavior to remain intact if already implemented elsewhere

# Risks and follow-ups

- **Risk: duplicate audit emission**
  - If both controller and service emit events, you may create duplicate records. Prefer emitting from the shared orchestration/application layer.

- **Risk: inconsistent event naming**
  - Reuse existing audit action naming conventions if present; do not invent a parallel taxonomy.

- **Risk: weak tenant context propagation**
  - Ensure company/tenant id is passed into both audit persistence and logs for all paths, especially fallback/system-triggered flows.

- **Risk: fallback path bypasses normal actor context**
  - Use `actor_type = system` and null/system actor id as appropriate, rather than faking a human actor.

- **Risk: over-coupling tests to internals**
  - Assert externally visible behavior and persisted audit outcomes rather than exact private method calls, except where a shared orchestration abstraction can be cleanly mocked.

- **Follow-up**
  - If audit detail views do not yet surface these events clearly, a later task can expose finance seeding history in the audit UI.
  - If append mode is planned but not implemented, leave audit/log structures extensible without shipping ambiguous UI/API behavior now.