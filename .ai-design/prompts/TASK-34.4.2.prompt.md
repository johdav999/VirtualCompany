# Goal
Implement backlog task **TASK-34.4.2** for story **US-34.4**: enable approval-driven, **thread-aware email draft and send execution** through the real provider, with **activity logging**, **approval/execution state tracking**, and **retry-safe failure handling** so approved follow-up recommendations can be delivered without duplicate sends or duplicate activity records.

# Scope
Focus only on the parts of the story needed for this task:

- Execute approved **follow-up recommendations** of type:
  - create draft reply
  - send email reply
- Ensure execution is **thread-aware**:
  - use the originating conversation/thread metadata when drafting or sending
  - preserve provider thread/conversation linkage where supported
  - prefer reply semantics over new-message semantics
- Integrate with the **real email provider adapter** already present or scaffold the minimal provider-facing contract needed in Infrastructure if missing
- Persist and expose:
  - recommendation approval state
  - execution state
  - provider message/draft identifiers
  - failure details suitable for UI surfacing
- Create **business activity/audit records** for successful draft/send execution
- Make failures **safe to retry**:
  - no duplicate outbound activities
  - no duplicate provider sends caused by app-level retries where idempotency can be enforced
  - retries should transition failed execution records cleanly
- Keep implementation aligned with modular monolith boundaries:
  - Domain/Application own business rules
  - Infrastructure owns provider integration
  - API/Web consume application contracts
- Do **not** broaden into finance handoff implementation unless required by shared abstractions already touched
- Do **not** implement unrelated recommendation detection logic except what is necessary to execute an already-approved recommendation

# Files to touch
Inspect the solution first and update the exact files that match existing conventions. Expected areas:

- `src/VirtualCompany.Domain/**`
  - recommendation / follow-up / approval / activity entities and enums
  - execution state/value objects
  - domain rules for idempotent execution transitions
- `src/VirtualCompany.Application/**`
  - commands/handlers for approving and executing follow-up recommendations
  - provider-facing abstractions/interfaces
  - DTOs/view models returned to API/UI
  - retry/error classification logic
- `src/VirtualCompany.Infrastructure/**`
  - email provider adapter implementation
  - persistence mappings/repositories
  - outbox/background execution wiring if execution is async
  - structured logging around provider calls
- `src/VirtualCompany.Api/**`
  - endpoints for approval-triggered execution and retry
  - response contracts if API exposure is missing
- `src/VirtualCompany.Web/**`
  - minimal UI updates to surface execution status/failure/retry if this task already has a corresponding page/component
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for approval, execution, retry, and duplicate protection
- Potential migration location
  - follow existing migration approach referenced by `docs/postgresql-migrations-archive/README.md`

Also inspect for existing modules/namespaces related to:
- sales leads / deals
- recommendations
- approvals
- communications / inbox / email
- audit events
- tool executions
- outbox / background jobs

Prefer extending existing models over introducing parallel concepts.

# Implementation plan
1. **Discover existing implementation and map the domain**
   - Find current entities and flows for:
     - lead qualification
     - follow-up recommendations
     - approvals
     - email/inbox/provider integration
     - activity logging
   - Identify whether recommendation execution already exists partially.
   - Identify the canonical aggregate that should own execution state:
     - likely recommendation, approval-linked action, or workflow step
   - Reuse existing naming and status enums where possible.

2. **Define/extend domain state for execution**
   - Add explicit execution lifecycle fields for draft/send recommendations, such as:
     - `ApprovalStatus`
     - `ExecutionStatus` (`Pending`, `InProgress`, `Succeeded`, `Failed`, `RetryableFailed`, etc.)
     - `ExecutionAttemptCount`
     - `LastExecutionErrorCode`
     - `LastExecutionErrorMessage`
     - `ExecutedAt`
     - `ProviderThreadId`
     - `ProviderMessageId`
     - `ProviderDraftId`
     - `ActivityId` or equivalent link
     - `ExecutionIdempotencyKey`
   - Ensure domain rules prevent:
     - execution before approval
     - re-executing already-succeeded recommendations
     - creating multiple success activities for the same recommendation execution
   - If there is a separate execution table/entity, use that instead of overloading the recommendation row.

3. **Model thread-aware email execution contract**
   - In Application, define or extend a provider abstraction with explicit reply/thread semantics, e.g. conceptually:
     - create draft reply from approved recommendation
     - send reply from approved recommendation
   - Required request data should include:
     - tenant/company context
     - integration/account/mailbox identity
     - recipient data
     - subject/body
     - original message/provider thread identifiers
     - in-reply-to / references metadata if available
     - idempotency key
   - Required response data should include:
     - provider draft/message id
     - provider thread id
     - normalized status
     - provider metadata for audit/debugging

4. **Implement provider integration in Infrastructure**
   - Extend the real provider adapter to support:
     - draft reply creation
     - send reply
     - thread-aware metadata propagation
   - Use provider-native reply/thread APIs where available instead of composing a brand-new email.
   - Add defensive handling for provider responses:
     - transient failures
     - auth/configuration failures
     - validation/business failures
   - Log provider request correlation safely without leaking sensitive content beyond existing logging policy.

5. **Implement approval-triggered execution flow**
   - On approval of a recommendation:
     - verify recommendation type is executable email draft/send
     - verify approval policy allows this path
     - transition execution to `InProgress` or enqueue background work
   - Execute the provider action and persist results atomically as much as possible:
     - update execution state
     - store provider ids/thread ids
     - create activity log entry
     - create audit event
   - If architecture already uses outbox/background workers for side effects, prefer:
     - command updates business state
     - outbox event triggers worker execution
   - If synchronous execution is already the established pattern for this module, keep it consistent but preserve idempotency.

6. **Implement retry-safe behavior**
   - Introduce a stable idempotency key per recommendation execution attempt family, not per retry.
   - Before sending/drafting, check whether execution already succeeded.
   - If a prior provider result is known, do not create a second activity.
   - Distinguish:
     - transient retryable failures
     - permanent non-retryable failures
   - Add explicit retry command/endpoint that:
     - only allows retry from failed retryable states
     - increments attempt count
     - preserves same logical execution identity where needed
   - If provider supports idempotency headers/keys, use them.
   - If provider does not, enforce app-level duplicate suppression via persisted execution state and provider ids.

7. **Create activity and audit records**
   - On successful draft creation:
     - log a business activity like `email_draft_created`
   - On successful send:
     - log a business activity like `email_sent`
   - Include links to:
     - recommendation
     - deal/lead/contact if applicable
     - provider message/draft/thread ids
     - approval and executor actor
   - Add audit events for:
     - recommendation approved
     - execution started
     - execution succeeded
     - execution failed
     - retry requested / retry succeeded / retry failed

8. **Expose execution state to API/UI**
   - Ensure API responses for recommendation detail/list include:
     - approval status
     - execution status
     - last error summary
     - retry availability
     - provider linkage metadata where appropriate
   - Add or update endpoints for:
     - approve recommendation
     - execute if approval and execution are separate
     - retry failed execution
   - Update minimal UI surfaces to show:
     - approved + pending execution
     - sent/drafted success
     - failure with retry action
   - Keep UI changes scoped to existing recommendation/approval screens.

9. **Persistence and migration**
   - Add migration(s) for any new columns/tables/indexes.
   - Add indexes for common lookups such as:
     - recommendation by company/status
     - execution by recommendation id
     - idempotency key uniqueness scoped by tenant/provider/mailbox as appropriate
   - Follow existing migration conventions in the repo.

10. **Testing**
   - Add tests covering:
     - approved draft recommendation creates one provider draft and one activity
     - approved send recommendation sends one provider email and one activity
     - thread metadata is passed through
     - execution before approval is rejected
     - retryable provider failure is persisted and surfaced
     - retry succeeds without duplicate activity
     - duplicate approval/execution requests do not double-send
     - already-succeeded execution cannot be retried
   - Prefer integration-style tests around API + application + infrastructure fakes where possible.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`
2. Run tests before changes to establish baseline:
   - `dotnet test`
3. After implementation, run targeted and full tests:
   - `dotnet test`
4. Verify migration artifacts are correct per repo convention.
5. Manually validate the main flow through API or existing UI:
   - create or locate an approved follow-up recommendation of type draft
   - approve it
   - confirm a thread-aware provider draft is created
   - confirm execution state becomes succeeded
   - confirm one activity/audit record exists
6. Validate send flow:
   - approve send recommendation
   - confirm one provider send occurs
   - confirm provider message/thread ids are persisted
   - confirm one activity/audit record exists
7. Validate failure and retry:
   - simulate transient provider failure
   - confirm failure is logged and surfaced as retryable
   - trigger retry
   - confirm success without duplicate activity or duplicate provider send
8. Validate duplicate protection:
   - submit approval/execution/retry command twice
   - confirm only one successful outbound result and one success activity
9. If API contracts changed, verify serialization and UI rendering still work.

# Risks and follow-ups
- The repo may not yet have a fully implemented recommendation aggregate or email provider abstraction; if so, add the smallest cohesive extension rather than inventing a parallel subsystem.
- Provider-specific thread/reply behavior may differ; normalize in Infrastructure but preserve raw provider ids for diagnostics.
- True exactly-once send semantics depend partly on provider idempotency support; where unavailable, document the app-level guarantees and edge cases.
- If current approval flow is synchronous and tightly coupled, consider a follow-up task to move execution onto outbox/background workers for stronger resilience.
- UI support for surfacing retryable failures may be partial; implement minimal visibility now and note richer exception UX as follow-up.
- If activity logging schema is immature, prefer linking to existing audit/activity tables rather than creating a temporary custom log model.
- Follow-up tasks likely needed:
  - richer provider-specific email threading coverage
  - finance handoff retry/idempotency parity
  - notification fan-out for execution failures
  - observability dashboards for provider execution outcomes