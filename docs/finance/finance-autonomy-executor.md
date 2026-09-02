# Finance autonomy executor and recovery

The Finance autonomy executor processes only persisted, queued run steps. It does not plan work,
grant authority, or call Finance command services and providers directly. Every dispatch passes
through the existing durable, policy-enforced company tool executor.

## Execution boundary

Each polling pass is bounded and rotates across companies before taking additional work from the
same company. A step must win its company-scoped database lease and budget reservation before the
worker performs object reads or tool dispatch. Long calls heartbeat through a separate database
scope, so the provider call never shares an open database transaction.

Immediately before dispatch, the claim path rechecks the active grant/version, emergency controls,
budget, evidence hash, dependencies, capability policy, effective agent authority, and tool policy.
The durable tool executor then independently rechecks the persisted human actor, membership,
delegation context when supplied, agent authority, risk policy, Finance eligibility, and approval
requirements. A denial is permanent for that attempt and is never converted into a blind retry.

The immutable business idempotency key is derived from company, run, and step identity unless the
reviewed plan supplied a narrower key. The same key is used as the tool correlation across lease
expiry, process restart, and bounded retries; worker IDs, lease tokens, attempt numbers, and request
correlation IDs never replace it.

## Outcome classification

- Successful structured outcomes complete the step and retain the tool-attempt reference and a
  hash plus safe summary of the actual effect.
- Approval-required outcomes bind the durable approval to the leased step and move the run to
  `awaiting_approval`.
- Authorization, policy, schema, and other permanent denials block the step with a stable reason.
- Read/recommend infrastructure failures return to `queued` until the plan's maximum attempt count;
  the final release becomes `dead_lettered`.
- Any ambiguous execute/provider failure moves to `reconciling`. It is never retried automatically.
- An expired read/recommend lease can be recovered for a bounded retry. An expired execute lease is
  treated as possibly dispatched and therefore enters reconciliation before another provider call.

## Objects and provider recovery

A run source with `sourceType=object_artifact` uses `entityId` as its company-scoped storage key and
`contentHash` as the required SHA-256 digest. The worker checks the company storage prefix, existence,
and digest before tool dispatch. Missing, cross-company, or corrupt evidence blocks the step and can
never produce a successful run.

An authorized company manager resolves a reconciling step with one of four explicit outcomes:

- `confirmed_applied`: complete with an effect and retain the safe provider reference;
- `confirmed_no_effect`: complete without recording a business effect;
- `confirmed_not_applied`: requeue only when the original bounded attempt allowance remains;
- `permanent_failure`: fail without retry.

The reconciliation command requires the current step version and an effect hash. Provider references
are bounded safe identifiers, not raw payloads, credentials, or response bodies.

## Operations

Configuration section `FinanceAutonomyExecutor` controls `Enabled`, `PollIntervalSeconds`, and
`BatchSize`. Supported bounds are 2–3600 seconds and 1–100 candidates. Shutdown cancels polling and
turns an interrupted execute acknowledgement into reconciliation; read/recommend work remains a
bounded retry. Database rollback leaves no winning lease or budget reservation. Outbox/audit failure
continues to feed the Finance autonomy circuit breaker and cannot be reported as business success.

Operators should investigate the run history, step attempts, tool-attempt audit link, evidence hashes,
business idempotency key, reconciliation reference, circuit state, and budget reservation before
resuming or replaying work. Logs and metrics contain status/reason codes and counts only, never tool
payloads, object contents, credentials, or raw provider responses.
