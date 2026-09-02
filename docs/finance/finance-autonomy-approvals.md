# Finance autonomy approval and human control

Autonomous Finance approvals are continuations of one exact persisted run step. They do not grant
new authority. A pending approval pauses the step and all dependent work; continuation occurs only
after the shared company tool executor revalidates the exact action and records a successful tool
attempt.

## Exact-action binding

The approval binding retains the company, run, plan and step identifiers; grant and grant-version
identifiers; capability and trigger; initiating human, delegated automation identity, and effective
authority; tool, action class, payload/target effect hash, and business idempotency key; evidence and
budget hashes; policy and catalogue versions; attempt number; tool-attempt identifier; and expiry.
Both the run binding endpoint and the P0 continuation path compare that context to current persisted
state. A changed plan, target, grant, policy, authority, evidence snapshot, budget reservation, or
attempt makes the continuation stale.

Approval cannot override an expired or revoked grant, an exhausted or unreconciled budget, a paused
or emergency-stopped control, an open circuit, stale evidence or records, failed tool eligibility, or
a human-only boundary. These checks happen again immediately before execution.

## Separation and decisions

Finance actions that require independent review cannot be approved by their initiating user, Laura,
or a delegated automation identity. Standing supplier automation is not an eligible substitute for
an independent Finance approver. The service also checks the current approval step's role and tenant
membership.

The supported terminal outcomes are `approved`, `rejected`, `changes_requested`, `cancelled`,
`expired`, `revoked`, and `superseded`. Decisions accept a client request identifier for idempotent
replay. Approval followed by a successfully executed exact attempt completes the waiting step and
queues only newly eligible dependents. Rejection, requested changes, expiry, or revocation blocks the
run. Cancellation cancels retained pending work, and supersession terminates the old revision. No
outcome silently creates a replacement approval.

Authorized managers can cancel a run or create a narrower revision from work that is awaiting
approval, blocked, or paused. A revision can retain only existing unfinished steps, cannot bypass a
removed dependency, is fully revalidated against current policy, and preserves lineage to its source
run. Added steps or broader scope require a new reviewed plan and, when necessary, a new grant.

## Escalation and recovery

The `FinanceAutonomyApprovals` worker polls bounded batches. Pending approvals, evidence gaps, open
circuits, reconciliation cases, dead-lettered repeated failures, and other blocked runs create
deduplicated high-priority work tasks and in-app notifications for the grant's configured escalation
role. If that role has no active member, owners and administrators are the fallback. Restarting the
worker does not duplicate the task or notification.

Tasks describe the reason and an explicit next action: approve in the approval inbox, refresh
evidence and create a new plan, reconcile the stable provider request, investigate/reset a circuit,
narrow, or cancel. Notifications and successful notification delivery are informational only; their
metadata records `notificationIsApproval=false`, and neither changes approval or business-action
state.

Exact-action expiry is enforced by both interactive decisions and the worker. Expiry denies the
linked tool attempt, blocks the run, preserves the audit chain, and directs a human to review current
evidence before creating any new request.

## Operations

Configuration section `FinanceAutonomyApprovals` controls `Enabled`, `PollIntervalSeconds`, and
`BatchSize`. Supported bounds are 2–3600 seconds and 1–100 candidates. Operators should use the run
history, exact approval context, tool attempt, policy/grant versions, budget reservation, evidence
hash, work task, notification, and correlated audit events as the recovery record. Raw payloads,
credentials, and provider responses must not appear in escalation summaries or logs.
