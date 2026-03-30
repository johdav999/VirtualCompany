# Goal
Implement `TASK-7.3.5` for `ST-103 — Human user invitation and role assignment` by introducing a **database-backed outbox + background dispatcher** for invitation delivery in the existing .NET modular monolith.

The implementation should ensure invitation creation is part of the transactional request path, while actual delivery happens asynchronously and reliably via background processing. This should align with the architecture decision to use **database-backed outbox + background dispatcher** for reliable side effects.

# Scope
Include:
- Persisting invitation-related outbox messages when invitations are created or re-sent.
- Adding infrastructure to store, claim, dispatch, and mark outbox messages.
- Implementing a background worker that processes pending invitation delivery messages.
- Ensuring invitation delivery is **idempotent** and safe for retries.
- Logging and correlation support for operational visibility.
- Minimal integration with the invitation flow under `ST-103`, without over-expanding into a full notification platform.

Do not include unless already partially present and required to complete the task:
- Full email provider integration with production-grade templates.
- SMS/push delivery.
- Generic broker infrastructure beyond database-backed outbox.
- Full notification center UX.
- Large refactors unrelated to invitation delivery.
- Reworking the entire membership/invitation domain if a smaller extension is sufficient.

If invitation APIs/domain objects do not yet exist, implement only the minimum needed to support invitation creation + outbox enqueueing + background dispatch, while keeping boundaries clean.

# Files to touch
Inspect and update the relevant files in these areas as needed:

- `src/VirtualCompany.Domain/**`
  - invitation aggregate/entity/value objects if present
  - outbox message entity/model
  - domain events only if the codebase already uses them

- `src/VirtualCompany.Application/**`
  - invite user command/handler
  - resend invitation command/handler
  - application abstractions for invitation delivery
  - outbox enqueueing contract/service
  - DTOs for serialized outbox payloads

- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence for outbox table
  - migrations
  - repository or DbContext updates
  - background worker / hosted service
  - invitation dispatcher implementation
  - serialization/deserialization of outbox payloads
  - retry/claiming logic
  - logging

- `src/VirtualCompany.Api/**`
  - DI registration for outbox services and hosted worker
  - configuration binding for dispatcher polling/batch settings if needed

- `src/VirtualCompany.Web/**`
  - only if current invitation UI needs a small update to reflect async delivery state

- `README.md`
  - only if there is an established section for local infrastructure/runtime behavior that should mention the outbox worker

Likely concrete files include:
- `Program.cs`
- application command handlers for invite/reinvite
- infrastructure persistence classes
- `DbContext`
- EF Core entity configurations
- new migration files

# Implementation plan
1. **Inspect current invitation flow**
   - Find the existing implementation for:
     - invite teammate
     - pending membership creation
     - role assignment
     - re-invite/revoke if present
   - Determine whether invitation creation already persists:
     - invitation token/code
     - pending membership state
     - delivery metadata/status
   - Reuse existing patterns for commands, repositories, and background services.

2. **Add an outbox persistence model**
   - Introduce an outbox table/entity in infrastructure, backed by PostgreSQL.
   - Keep the schema pragmatic and aligned with the modular monolith:
     - `id`
     - `message_type`
     - `payload_json`
     - `company_id` if applicable
     - `correlation_id`
     - `created_at`
     - `available_at`
     - `status` or nullable processed fields
     - `attempt_count`
     - `last_error`
     - `claimed_at` / `claim_token` if using claim-based processing
     - `processed_at`
   - Prefer a design that supports safe retries and avoids duplicate dispatch under concurrent workers.

3. **Enqueue invitation delivery in the same transaction**
   - Update the invite and re-invite application flow so that when an invitation is created/reissued, an outbox message is written in the same database transaction as the invitation state change.
   - Do not send email directly from the request handler.
   - The outbox payload should contain only the data needed for dispatch, such as:
     - invitation id
     - company id
     - target email
     - assigned role
     - inviter user id if useful
     - delivery template/type
     - correlation id
   - If the system already uses domain events, it is acceptable to translate a domain event into an outbox record during save. Otherwise, write the outbox record directly from the application layer in a clean, explicit way.

4. **Define invitation delivery contract**
   - Add an abstraction for invitation delivery, e.g. an application/infrastructure boundary such as:
     - `IInvitationDeliveryDispatcher`
     - or `IInvitationSender`
   - The dispatcher should:
     - load the latest invitation state if needed
     - skip revoked/accepted/expired invitations
     - perform idempotent send behavior
     - update delivery metadata/status if such fields exist
   - If no real email provider exists yet, implement a safe stub that logs the delivery attempt clearly while preserving the outbox architecture.

5. **Implement background outbox dispatcher**
   - Add a hosted background service in infrastructure that:
     - polls for pending outbox messages
     - claims a batch atomically
     - dispatches each message by type
     - marks success/failure
     - increments attempts
     - stores error details safely
   - Keep the first version simple and robust:
     - configurable polling interval
     - configurable batch size
     - cancellation-token aware
     - structured logs with message id, company id, invitation id, correlation id
   - Ensure the worker can resume after crashes without losing messages.

6. **Implement message-type dispatching**
   - Add a dispatcher for invitation delivery message type(s), for example:
     - `company.invitation.created`
     - `company.invitation.resent`
   - Deserialize payloads into typed DTOs.
   - Validate payload shape defensively.
   - Unknown or malformed message types should be logged and marked failed in a controlled way.

7. **Add idempotency and retry behavior**
   - Invitation delivery must tolerate retries.
   - Use one or more of these patterns as appropriate to the current codebase:
     - delivery status fields on invitation entity
     - last sent timestamp
     - provider message id
     - dedupe key based on invitation id + delivery type
   - Retries should distinguish:
     - transient failures: leave for retry with incremented attempt count
     - permanent failures: mark failed after threshold or when payload/state is invalid
   - Do not create duplicate pending memberships or duplicate invitation records during retries.

8. **Persist delivery metadata**
   - If invitation entity/schema supports it, add fields such as:
     - `delivery_status`
     - `last_delivery_attempt_at`
     - `delivered_at`
     - `delivery_error`
   - If not present, add only the minimum necessary fields to support operational visibility and re-invite behavior.
   - Keep this focused on invitation delivery, not a generalized notification history system.

9. **Register services and configuration**
   - Wire up DI registrations in API startup.
   - Add configuration options for:
     - polling interval
     - batch size
     - max attempts
   - Use sensible defaults for local development.

10. **Add migration(s)**
   - Create EF Core migrations for:
     - outbox table
     - any invitation delivery metadata columns added
   - Ensure indexes support polling and claiming efficiently, such as on:
     - status/processed state
     - available time
     - message type if useful

11. **Add tests**
   - Add or update tests covering:
     - invite command writes invitation state and outbox message atomically
     - re-invite writes a new outbox message
     - dispatcher processes pending invitation message successfully
     - revoked/accepted invitation is skipped safely
     - failed dispatch increments attempts and preserves error
     - duplicate processing does not send twice when already delivered
   - Prefer unit tests for handler/dispatcher logic and integration tests for persistence behavior if the test project setup supports it.

12. **Keep boundaries clean**
   - Do not let background worker bypass application/domain rules.
   - Do not couple invitation delivery to UI concerns.
   - Keep tenant/company context explicit in queries and logs.
   - Follow existing project conventions over inventing a new framework.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of the normal workflow, generate/apply them and verify schema changes compile cleanly.

4. Manually validate the happy path:
   - create an invitation
   - confirm pending membership/invitation record is persisted
   - confirm an outbox row is created in the same transaction
   - run the app/worker
   - confirm the outbox row is processed
   - confirm invitation delivery status/metadata is updated
   - confirm logs show correlation and company context

5. Validate retry behavior:
   - force the invitation sender/dispatcher to fail once
   - confirm attempt count increments
   - confirm message remains retryable until threshold
   - confirm eventual success marks the message processed

6. Validate idempotency:
   - simulate duplicate worker execution or repeated processing attempt
   - confirm invitation is not delivered twice once already marked delivered/skipped

7. Validate state guards:
   - accepted invitation should not be re-delivered by stale outbox message
   - revoked invitation should not be delivered
   - malformed payload should fail safely with logs

# Risks and follow-ups
- **Current invitation model may be incomplete**: if invitation entities or delivery metadata do not exist yet, keep additions minimal and aligned with `ST-103`.
- **Concurrency/duplicate dispatch risk**: claiming logic must be atomic enough for multiple worker instances, even if only one runs locally today.
- **Transaction boundary risk**: ensure invitation persistence and outbox enqueueing share the same DbContext transaction.
- **Over-generalization risk**: do not build a full event bus; implement only what is needed for invitation delivery while leaving a clean path for future outbox-backed side effects.
- **Email provider uncertainty**: if no provider exists, use a stub/logging sender behind an interface so the architecture is complete without blocking on external integration.
- **Operational follow-up**:
  - future stories can reuse the outbox for notifications, approvals, workflow fan-out, and audit propagation
  - later enhancements may add dead-letter handling, exponential backoff, metrics, and broker extraction if scale requires it