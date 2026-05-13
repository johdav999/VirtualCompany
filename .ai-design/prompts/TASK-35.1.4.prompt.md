# Goal
Implement TASK-35.1.4 by wiring inbound email reply tracking and deal creation domain/integration events so that any contact currently in an active campaign sequence has all pending future sequence steps cancelled within 1 minute, with tenant-safe behavior, idempotent processing, persisted auditability, and test coverage.

# Scope
In scope:
- Identify the existing sales campaign/sequence execution model, scheduled step records, contact linkage, email reply correlation, and deal creation flow.
- Add or extend internal event contracts/handlers for:
  - inbound reply received for a contact and correlated to campaign/sequence context
  - deal created for a contact
- Cancel all pending future sequence steps for the affected contact in active sequences.
- Ensure cancellation happens asynchronously and reliably via the app’s existing background/event/outbox patterns.
- Persist enough state to prevent duplicate cancellation side effects and support observability/audit.
- Add automated tests covering both reply-triggered and deal-triggered cancellation paths.
- Keep implementation tenant-scoped and aligned with modular monolith boundaries.

Out of scope:
- Building the full campaigns UI, sequence builder UI, or launch flow.
- Reworking email provider integration beyond what is needed to consume existing reply correlation data.
- Reworking deal creation UX or CRM domain beyond emitting/handling the needed event.
- Broad refactors unrelated to cancellation behavior.

# Files to touch
Inspect first, then update the actual relevant files you find in these areas:

- `src/VirtualCompany.Domain/**`
  - sales/campaign entities, sequence step entities, execution status enums, domain events
  - deal/contact entities if domain events belong here
- `src/VirtualCompany.Application/**`
  - command/query handlers for campaigns, sequence execution scheduling, inbox/reply processing, deal creation
  - event handlers / notification handlers
  - DTOs and contracts for internal events
- `src/VirtualCompany.Infrastructure/**`
  - EF Core configurations, repositories, background dispatchers, outbox/event plumbing
  - email integration webhook/inbox processor
- `src/VirtualCompany.Api/**`
  - webhook endpoints or controllers if inbound reply/deal events enter here
- `src/VirtualCompany.Web/**`
  - only if existing UI/state badges need to reflect cancelled pending steps and there are already related views
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- any corresponding application/domain test projects if present

Also inspect:
- existing migrations approach and whether a new migration is required
- any existing audit/outbox/event abstractions
- any existing status names for scheduled sequence steps (`Pending`, `Scheduled`, `Queued`, `Cancelled`, etc.)

# Implementation plan
1. **Discover the current model before coding**
   - Find the entities/tables representing:
     - campaigns
     - sequences and sequence steps
     - per-contact scheduled executions
     - email sends and reply correlation
     - contacts
     - deals
   - Determine the canonical record that represents a “pending future step for a contact”.
   - Determine how “active sequence” is represented:
     - campaign status
     - execution status
     - contact enrollment status
   - Determine whether inbound replies are already converted into an internal event or just persisted.
   - Determine whether deal creation already emits a domain/application event.

2. **Define the cancellation rule clearly in code**
   Implement one shared application service or domain service for:
   - `CancelPendingSequenceStepsForContactAsync(tenant/companyId, contactId, reason, occurredAt, correlation data...)`
   
   Behavior:
   - Find all active sequence enrollments/executions for the contact within the tenant.
   - Find all future steps that are still pending/scheduled/not-yet-sent.
   - Mark them cancelled with:
     - cancellation reason (`ReplyReceived` or `DealCreated`)
     - cancelled timestamp
     - optional source event/correlation reference
   - Do not alter already sent/completed/failed/cancelled steps.
   - Be idempotent: repeated processing of the same event must not create inconsistent state.
   - If there is a parent enrollment/contact-sequence status, update it appropriately if all future work is cancelled.

3. **Wire inbound reply event handling**
   - If an internal event already exists for reply receipt, subscribe to it.
   - Otherwise, introduce an application-level notification/event emitted after inbound reply persistence/correlation succeeds.
   - Event payload should include at minimum:
     - tenant/company id
     - contact id
     - message/reply id
     - correlated campaign id and/or sequence execution id if available
     - occurred at
   - Handler should invoke the shared cancellation service.
   - Prefer handling after reply correlation is persisted so cancellation is based on authoritative data.

4. **Wire deal creation event handling**
   - If deal creation already emits a domain event/application notification, consume it.
   - Otherwise, emit one from the successful deal creation path.
   - Event payload should include:
     - tenant/company id
     - deal id
     - contact id (or resolved primary contact id)
     - occurred at
   - Handler should invoke the same shared cancellation service with reason `DealCreated`.

5. **Use existing reliability patterns**
   - If the codebase uses MediatR notifications + outbox/background dispatcher, follow that pattern.
   - If domain events are persisted to an outbox, ensure these handlers run asynchronously and reliably.
   - Ensure processing can complete within 1 minute under normal operation:
     - avoid request-thread-only handling if current architecture expects background processing
     - keep query/update path efficient and scoped
   - Add idempotency guards if the same inbound webhook or deal event can be delivered multiple times.

6. **Persist cancellation metadata**
   If the current schema lacks fields, add the minimum necessary columns/properties, such as:
   - step execution status = cancelled
   - cancelled at
   - cancellation reason
   - cancellation source reference / correlation id (optional if pattern exists)
   
   Keep schema changes minimal and aligned with existing naming conventions.
   If a migration is needed, add it in the project’s established migration location/process.

7. **Audit and observability**
   - Reuse existing audit/business event logging if present.
   - Record a concise audit event for bulk cancellation due to:
     - inbound reply
     - deal creation
   - Include tenant, contact, campaign/enrollment context, and count of cancelled steps where practical.
   - Add structured logs with correlation IDs if the project already uses them.

8. **Guardrails and edge cases**
   Handle these cases explicitly:
   - contact has no active sequence: no-op
   - reply/deal event arrives after all steps already completed/cancelled: no-op
   - multiple active campaigns for same contact: cancel pending future steps across all affected active sequences unless current domain explicitly restricts to one
   - duplicate reply/deal events: idempotent no-op on already cancelled steps
   - tenant mismatch or missing contact resolution: safe no-op or handled failure per existing conventions
   - reply correlated to a message but contact missing: do not cross-tenant infer

9. **Tests**
   Add/extend tests for:
   - reply received for contact in active sequence cancels future pending steps only
   - deal created for contact in active sequence cancels future pending steps only
   - already sent/completed steps remain unchanged
   - duplicate event processing is idempotent
   - no active sequence results in no changes
   - tenant isolation is preserved
   - if there is an API/webhook path test, verify event ingestion leads to persisted cancellation state

10. **Keep changes focused**
   - Do not redesign campaign architecture.
   - Reuse existing enums, repositories, and event abstractions where possible.
   - Prefer one shared cancellation path used by both triggers.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations were added, verify they apply cleanly using the project’s existing migration workflow.

4. Manually validate in code/tests that:
   - inbound reply processing emits or triggers the cancellation flow
   - deal creation emits or triggers the cancellation flow
   - pending future steps transition to cancelled state
   - completed/sent steps are untouched
   - repeated processing does not double-cancel or error
   - tenant scoping is enforced in queries/updates

5. If there are existing API/integration tests for email webhooks or deal creation, extend them and confirm end-to-end behavior.

6. In the final implementation notes/PR summary, include:
   - where the shared cancellation logic lives
   - which events now trigger it
   - any schema changes made
   - any assumptions about “pending future steps” and “active sequence”

# Risks and follow-ups
- The exact sales campaign schema may differ from the task wording; inspect first and adapt to the existing aggregate boundaries rather than inventing parallel models.
- Reply correlation may currently be message-centric rather than contact-centric; if contact resolution is indirect, ensure cancellation only occurs after authoritative contact linkage is established.
- Deal creation may support multiple contacts/participants; if so, document whether only primary contact or all linked contacts should trigger cancellation.
- If there is no existing outbox/event pipeline in this area, a minimal synchronous handler may pass tests but could weaken the “within 1 minute reliably” requirement; prefer the established async pattern if available.
- Bulk cancellation queries must be tenant-filtered and efficient; watch for N+1 updates.
- UI reflection of cancelled steps may still need a separate follow-up if current pages do not surface this state.
- If acceptance criteria imply campaign-level state changes after all contacts are cancelled, that is a likely adjacent follow-up but not required unless existing logic depends on it.