# Accounting close readiness and lock runbook

## Purpose

This runbook covers the governed P4 close workflow layered on the existing immutable journal,
reporting snapshot, and fiscal-period lock authority. A completed checklist is necessary but is
never sufficient to lock a period.

## Normal close

1. Complete the generated close tasks and attach every required company-scoped evidence document.
2. Prepare readiness. The retained snapshot covers bank, AR, AP, VAT/tax, suspense, finance
   approvals, provider/delivery backlog, document gaps, exports, schedules/accruals, currency
   revaluation, fixed assets, dimensions, and close tasks. Record its evidence hash.
3. Resolve every non-waivable blocker. Accounting integrity, reconciliation, tax, subledger, task,
   and approval blockers cannot be bypassed by checklist completion or an AI recommendation.
4. For an explicitly waivable operational exception, propose a waiver against the exact check hash,
   amount, evidence document checksum, expiry, and reason. A different finance approver must approve
   the central approval request. Refresh readiness after approval.
5. Submit the ready snapshot for review. A reviewer other than its preparer/submitter approves the
   exact evidence hash.
   If the close scope or ownership changes instead, cancel the current snapshot with a retained reason
   and prepare a new one; a cancelled snapshot cannot be submitted, approved, or locked.
6. Lock using the approved snapshot version and hash. The service repeats all authoritative checks
   in a serializable transaction. If journals, reconciliations, tasks, approvals, waivers, or backlog
   evidence changed, it marks the snapshot stale and locks nothing.
7. Confirm the fiscal-period history, immutable close sign-off, reporting snapshots, audit event,
   and queued post-close report-regeneration execution share the close/snapshot identifiers.

## Stale and failed readiness

- `stale` means retained evidence changed after preparation or approval. Do not retry lock with the
  old hash. Refresh, resolve blockers, submit, and obtain a new independent approval.
- `failed` means readiness evaluation did not complete. Review safe failure text and correlated
  technical logs, repair the dependency, then refresh. Never manually edit the snapshot status.
- An expired waiver cannot support lock. Propose a new waiver against the current check/evidence
  version or resolve the exception.
- A waiver also stops applying when its central approval is no longer approved or its evidence
  document hash changes. Refresh readiness and obtain a new exact approval instead of reusing it.
- An idempotency conflict means the key was reused with different input. Preserve the original
  operation and use a new key for a genuinely new business action.

## Controlled reopen

1. Start from the exact locked readiness snapshot.
2. Record a detailed reason, bounded scope, and the intended correction path. The request expires.
3. A company owner/admin other than the requester reviews it.
4. Execute using the approved request version and prior snapshot hash. The service rechecks the
   approval, expiry, locked snapshot, and current fiscal-period lock inside a serializable transaction.
5. Confirm reopen history retains the requester, reviewer, executor, prior snapshot/hash, scope,
   reason, and correction path. Posted journals and historical reporting snapshots remain immutable.
6. Make corrections through linked reversals/replacements or the documented future-period path,
   then run a new close preparation and approval cycle.

## Recovery and investigation

- Use correlation ID, close instance ID, readiness snapshot ID, fiscal period ID, and the stable
  reason code when investigating.
- Never delete a readiness snapshot, sign-off, waiver, reopen request, accounting-period history,
  audit event, or completed reporting snapshot to repair state.
- If lock outcome is ambiguous, inspect fiscal-period lock state, close sign-off, accounting-period
  history, and transaction/audit records before retrying the same idempotency key.
- Restore testing must verify the policy, readiness checks, waivers, sign-offs, reopen requests,
  reporting snapshots, period history, audit evidence, and background execution rows together.
