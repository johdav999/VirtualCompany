# Sales meeting change review and delivery

Prompt 8 adds a tenant-scoped proposal boundary between meeting evidence and canonical Sales records or external follow-up actions. Generating a proposal never changes a Deal, Lead, Contact, mailbox, or calendar.

## Policy and review model

`SalesMeetingChangePolicy` owns the server-side allowlist and current policy version. Deal stage, probability, and next step plus allowed Lead and Contact fields require explicit confirmation by an authorized company member. Deal value, commercial commitments, customer communication, and calendar actions cannot participate in bulk approval. Customer minutes delivery and next-meeting scheduling always require an owner approval and durable outbox execution.

Each proposal records its typed value, captured before value and target version, meeting evidence sources and hash, rationale and confidence, policy result, review state, exact approval binding, execution evidence, provider outcome, idempotency key, and concurrency version. Editing any proposal clears the binding and requires a fresh review.

## API workflow

The routes are rooted at `api/sales/meeting-sessions/{sessionId}/change-proposals`:

- `POST /generate` accepts only typed target/action/field/value combinations and verified sources from that meeting.
- `GET /` and `GET /{proposalId}` return human-readable before/after projections and policy/execution state.
- `PUT /{proposalId}` edits a draft or reviewed proposal and invalidates its approval.
- `POST /{proposalId}/approve` confirms safe proposals or creates/synchronizes an exact owner approval request for gated proposals.
- `POST /approve-all-safe` reevaluates every selected proposal and skips anything gated, stale, non-draft, missing, or no longer allowed.
- `POST /{proposalId}/reject` records rejection.
- `POST /{proposalId}/execute` rechecks membership, policy, binding, approval, proposal version, and target version immediately before execution.

All endpoints derive company and user identity from authenticated server context. Cross-company IDs are never accepted as authority.

## Execution and operations

Canonical updates go through `ISalesMeetingCanonicalChangeCommandHandler`; controllers and AI output never mutate EF entities. The handler enforces its own typed target/field switch and optimistic target-version check. Successful execution stores before/after JSON and an audit event. Replays of an executed proposal return its existing result.

Approved customer minutes are delivered on `sales.meeting_customer_minutes.delivery_requested`. The dispatcher verifies the exact approved minutes version and business idempotency key, treats completed work as a no-op, retries retryable mailbox failures, stops on permanent/authentication failures, and places interrupted provider outcomes in reconciliation rather than blindly sending again. Approved next-meeting actions create a Sales-owned invitation and use the existing calendar invitation outbox dispatcher with an idempotency key derived from company, session, recipient, and approved proposal version.

Operators should monitor proposal states `failed`, `conflict`, and `reconciliation_required` alongside company outbox/background execution diagnostics. A conflict requires refreshed evidence and a new proposal or edit/reapproval. A reconciliation-required delivery must be checked in the provider before any retry.

Lifecycle telemetry is emitted through the `VirtualCompany.SalesMeetingChanges` meter. Monitor `sales.meeting.change.transitions` by action/outcome/status and `sales.meeting.change_delivery.outcomes` by channel/outcome. These dimensions are deliberately low-cardinality and contain no customer content.

## UI design gate

The typed Web client is registered for the review workspace. The Blazor review page and browser UAT remain subject to the mandatory approved-reference workflow in `docs/design.md`; production UI implementation must not begin until the sales meeting reference images are explicitly approved.
