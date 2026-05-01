# Goal
Implement **TASK-32.4.3 — Build `FortnoxWriteCommandService` with approval workflow integration, audited execution for allowed write operations, and safe failure handling** in the existing .NET modular monolith.

The implementation must ensure that **all MVP Fortnox write operations are approval-gated before any external API call**, produce **business audit events for approved executions**, and fail safely without leaking secrets or causing duplicate/unsafe writes. The work should align with **US-32.4 Fortnox integration UI, approval-gated writes, and production readiness** and fit the architecture patterns already defined: modular monolith, CQRS-lite, approval module integration, auditability as a domain feature, and policy-enforced external tool execution.

# Scope
Include the following in this task:

- Build or complete a dedicated application/infrastructure service named `FortnoxWriteCommandService` or equivalent interface + implementation.
- Route all MVP Fortnox write operations through this service.
- Ensure the service:
  - creates an approval item before any outbound Fortnox write call,
  - stores a clear payload summary and target company context for approvers,
  - executes only after explicit approval,
  - emits audit events after approved execution,
  - excludes tokens and sensitive Fortnox secrets from approval/audit payloads,
  - handles duplicate execution prevention and safe retries/failures.
- Integrate with the existing approval domain/module rather than inventing a parallel approval mechanism.
- Add/update DTO mapping, payload hashing, and redaction/sanitization helpers as needed.
- Add or update UI indicators only if required to support approval-gated Fortnox writes or Fortnox-linked record protection.
- Ensure records sourced from Fortnox are visibly marked and protected from simulation overwrite paths where relevant to this task.
- Add unit tests for the Fortnox write flow and any touched Fortnox integration primitives.
- Add opt-in integration test hooks for real Fortnox API calls only when explicit environment variables are present.

Also verify acceptance-criteria-adjacent dependencies if already partially implemented:
- OAuth URL generation
- token refresh
- encryption
- DTO mapping
- duplicate prevention
- sync cursor updates
- approval creation

Do **not** expand into a full new Fortnox feature set beyond what is necessary for the write-command workflow and its immediate UI/domain integration.

# Files to touch
Inspect the solution first and then touch the minimum correct set. Likely areas:

- `src/VirtualCompany.Application/**`
  - Fortnox integration application services
  - approval orchestration commands/handlers
  - audit event creation services
  - DTOs / command models / result models
- `src/VirtualCompany.Domain/**`
  - Fortnox integration entities/value objects
  - approval-related domain contracts
  - audit event contracts
  - source-link / external-link metadata for Fortnox-linked records
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox API client adapter
  - token/encryption handling
  - write command execution implementation
  - persistence for idempotency / execution records / payload hash
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for initiating Fortnox write requests and approval-triggered execution callbacks if needed
- `src/VirtualCompany.Web/**`
  - Finance Settings → Integrations Fortnox card if missing/incomplete
  - approval UX surfaces for Fortnox write summaries
  - UI markers for Fortnox-sourced records and simulation overwrite prevention
- `tests/VirtualCompany.Api.Tests/**`
  - API and integration-facing tests
- Add test projects/files under existing test structure as appropriate for:
  - application tests
  - infrastructure tests
  - web/UI tests if present in repo conventions
- `docs/design/references/fortnox-integration-settings.png`
  - only if UI is new/refactored and a reference screenshot is required by acceptance criteria

Before editing, identify the actual existing Fortnox-related files and follow local naming/module conventions.

# Implementation plan
1. **Discover existing Fortnox, approval, and audit architecture**
   - Search for:
     - `Fortnox`
     - `Approval`
     - `AuditEvent`
     - `tool_executions`
     - integration settings UI
     - source metadata / external record links
   - Determine whether there is already:
     - a Fortnox OAuth/token service,
     - sync service,
     - approval aggregate/service,
     - audit event writer,
     - integration settings page/card,
     - simulation action pipeline.
   - Reuse existing patterns and avoid introducing a parallel architecture.

2. **Define the write-command abstraction**
   - Introduce or refine:
     - `IFortnoxWriteCommandService`
     - request DTO(s) for supported MVP write operations
     - result DTO(s) representing:
       - approval created,
       - awaiting approval,
       - executed,
       - rejected,
       - failed safely,
       - duplicate/no-op.
   - Model the write request so it includes:
     - `CompanyId`
     - initiating actor/user context
     - target Fortnox entity type
     - internal entity reference if applicable
     - sanitized payload
     - payload summary for humans
     - idempotency key or deterministic duplicate key
     - correlation ID.

3. **Implement approval-first behavior**
   - For every MVP write operation:
     - compute a sanitized payload summary,
     - compute a payload hash from a canonicalized sanitized payload,
     - create an approval request before any external API call,
     - persist enough metadata so the approved action can be executed later without ambiguity.
   - Approval item must clearly show:
     - target company
     - entity type
     - direction = outbound / write to Fortnox
     - concise payload summary
     - linked internal record if any.
   - Ensure no Fortnox API write is called during approval creation.

4. **Integrate approval decision to execution**
   - Hook approved decisions to execution through the existing approval workflow:
     - either via command handler invoked after approval,
     - or background worker / domain event / outbox consumer depending on current architecture.
   - On approval:
     - reload the pending write command,
     - verify tenant/company scope,
     - verify not already executed,
     - refresh token if needed,
     - execute the Fortnox API write,
     - persist execution outcome and external reference.
   - On rejection/expiry/cancellation:
     - mark the write request terminal and do not execute.

5. **Add duplicate prevention and idempotency**
   - Prevent duplicate external writes caused by:
     - repeated user clicks,
     - retries,
     - approval replay,
     - worker restarts.
   - Use a stable idempotency strategy such as:
     - company + operation type + target entity + payload hash + pending/executed state,
     - and/or explicit idempotency key.
   - If a duplicate pending approval already exists, return that instead of creating a new one.
   - If already executed, return a safe already-completed result and do not call Fortnox again.

6. **Implement audited execution**
   - After approved execution, create a business audit event containing:
     - approver
     - entity type
     - direction
     - summary
     - payload hash
     - outcome
     - target/internal references where available.
   - Exclude:
     - access tokens
     - refresh tokens
     - client secrets
     - authorization headers
     - raw sensitive Fortnox secrets.
   - Keep rationale concise and operational; no chain-of-thought.

7. **Implement safe failure handling**
   - Fail closed:
     - no approval => no write
     - ambiguous state => no write
     - missing tenant scope => no write
     - missing/invalid token => safe failure
   - Distinguish:
     - transient external/API failures,
     - permanent validation/business failures,
     - policy/approval failures.
   - Persist safe failure state and user-facing summary without leaking secrets.
   - Ensure retries do not create duplicate writes.
   - Log technical details in app logs, but keep business audit sanitized.

8. **Protect Fortnox-linked records from simulation overwrite**
   - Identify the record types touched by Fortnox sync/write flows.
   - Ensure UI and/or command handlers clearly mark records sourced from Fortnox.
   - Prevent simulation actions from overwriting Fortnox-linked records:
     - ideally in backend validation/guardrails,
     - optionally reinforced in UI disabled states/messages.
   - Add a clear user-facing explanation.

9. **Complete or adjust Finance Settings → Integrations Fortnox card if needed**
   - Verify the Fortnox card supports states:
     - Not connected
     - Connecting
     - Connected
     - Syncing
     - Needs reconnect
     - Error
   - Verify actions:
     - Connect Fortnox
     - Sync now
     - Reconnect
     - Disconnect
     - View sync history
   - If UI is missing or materially refactored:
     - generate the required reference screenshot using OpenAI image.2 API,
     - save to `/docs/design/references/fortnox-integration-settings.png`,
     - ensure implementation follows `/docs/style.md` and `/docs/design.md`.

10. **Testing**
   - Add unit tests for:
     - approval creation before external write,
     - no external call before approval,
     - approved execution path,
     - duplicate prevention,
     - payload hashing and sanitization,
     - audit event creation with secret exclusion,
     - safe failure behavior,
     - OAuth URL generation,
     - token refresh,
     - encryption,
     - DTO mapping,
     - sync cursor updates if touched.
   - Add opt-in integration tests for real Fortnox API only when explicit env vars are set.
   - Skip safely when env vars are absent.

11. **Implementation quality constraints**
   - Follow existing solution conventions, namespaces, DI registration, and test style.
   - Keep tenant scoping explicit.
   - Prefer typed contracts over raw JSON blobs except where existing patterns require JSON persistence.
   - Do not expose secrets in logs, exceptions, approval summaries, or audit events.
   - Keep changes cohesive and production-ready.

# Validation steps
1. **Codebase inspection**
   - Confirm all Fortnox write entry points now route through `FortnoxWriteCommandService` or the equivalent central abstraction.
   - Confirm there is no direct Fortnox write call bypassing approval for MVP operations.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Behavioral verification**
   - Initiate a Fortnox write request and verify:
     - an approval item is created,
     - no external API write occurs yet,
     - approval shows target company and payload summary.
   - Approve the item and verify:
     - exactly one external write occurs,
     - audit event is created,
     - audit event includes approver, entity type, direction, summary, payload hash,
     - audit event excludes tokens/secrets.
   - Retry the same request and verify duplicate prevention.
   - Reject/expire approval and verify no external write occurs.
   - Force a transient Fortnox failure and verify safe failure state and no secret leakage.
   - Verify Fortnox-linked records cannot be overwritten by simulation actions.

5. **UI verification**
   - Check Finance Settings → Integrations Fortnox card for required states/actions.
   - If UI changed materially, confirm screenshot exists at:
     - `/docs/design/references/fortnox-integration-settings.png`

6. **Opt-in real API integration tests**
   - Only if explicit env vars are configured, run the real Fortnox integration tests.
   - Otherwise confirm they are skipped and do not fail CI/local runs.

# Risks and follow-ups
- Existing Fortnox integration may already have partial write paths that bypass approvals; ensure all MVP write paths are centralized.
- Approval execution timing may race with retries/background workers; idempotency and terminal-state checks are critical.
- Payload canonicalization must be stable, or duplicate detection and payload hashing will be unreliable.
- UI-only protections for Fortnox-linked records are insufficient; backend enforcement is required.
- If approval/audit modules are immature, this task may expose missing shared abstractions that should be added carefully rather than hardcoded in the Fortnox module.
- Real Fortnox API behavior may differ from mocks around validation, token refresh, and duplicate semantics; keep integration tests opt-in but meaningful.
- If schema changes are required for pending write commands, execution records, or payload hashes, add migrations consistent with repo conventions and keep them minimal.
- Follow-up tasks may be needed for:
  - broader Fortnox sync history UX,
  - richer approval inbox presentation,
  - webhook reconciliation,
  - stronger outbox-driven execution orchestration,
  - expanded protection of Fortnox-linked entities across more simulation flows.