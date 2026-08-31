# Finance agent actor authorization

Finance tools use `IFinanceAgentAuthorizationService` as the authoritative actor boundary. The boundary runs before agent guardrails and before any Finance provider or record access, and it runs again when an approved action continues.

## Permission model

- `read` and `recommend` require `finance.view`.
- Every `execute` requires `finance.edit`.
- Invoice approval also requires `finance.approve`.
- Paid supplier bill expense posting also requires `finance.accounting.admin`.
- Accounting-provider migration execution also requires `finance.integrations.manage`.

Controller policies remain defense in depth. A company membership or agent identity alone does not authorize a Finance tool.

## Background delegation

Background Finance execution requires a persisted `finance_agent_delegation_authorities` row. The authority is bound to one company, agent, delegated user, issuer, originating workflow, Finance capability, allowed action classes, scopes, and expiry. Revoked, expired, missing, or mismatched authority is denied without falling back to the agent identity. The delegated user must retain an active company membership and the permissions required by the tool at execution time.

Do not put delegation identifiers in prompts or treat them as bearer secrets. Workers obtain the identifier from trusted persisted workflow state and pass it with the matching workflow instance ID.

## Audit and diagnosis

Every decision writes `finance.agent_tool.authorization_evaluated` against the correlated tool execution ID. Safe metadata includes the actor type, membership state, required policies and permissions, stable reason code, policy version, workflow/delegation references, and correlation ID. Request payloads and target existence are not recorded in the authorization event.

For a denial, inspect the authorization event by company and execution ID, then use `authorizationReasonCode` to distinguish missing permissions, missing actors, and delegation expiry or mismatch. Change membership or issue a new bounded delegation; never edit an expired or mismatched authority into validity.

The maintained authority matrix is generated in-process by `FinanceAgentAuthorityMatrix.Build`. It joins every registered Finance tool/action with its company policies, actor permissions, effective-authority grant, risk tier, approval behavior, external side-effect class, and owning parameterized regression test. `FinanceAgentAuthorityMatrixTests` fails on missing, duplicate, or drifted rows; do not maintain a second hand-authored tool list.

Two payload-safe counters support operational triage:

- `finance.agent.authority.decisions` is tagged only with `tool.name`, `action.type`, `decision.outcome`, and `reason.code`.
- `finance.agent.authority.approval_decisions` is tagged only with `tool.name`, `decision.outcome`, and `reason.code`, including `approval_required` and `stale` outcomes.

Do not add company IDs, user IDs, approval IDs, target IDs, payload values, hashes, delegation IDs, or idempotency keys as metric tags.

## Permission-change procedure

1. Restrict or remove the membership permission, configured grant, or versioned role-policy entry first. Revoking a delegation is the immediate background-execution control.
2. Resolve the effective authority again and confirm its hash changed and the affected row is no longer usable.
3. Treat all prepared actions, schedules, and pending approvals carrying the old hash as stale. Do not copy the new hash into an old record.
4. Run the focused Finance authority, risk-policy, execution-flow, and approval-chain suites before resuming.
5. Create a new reviewed approval or delegation only when the reduced authority is intentional and verified.

## Approval recovery

For `finance_approval_binding_missing`, `finance_approval_binding_mismatch`, expiry, stale authority, stale target, stale policy/threshold evidence, or stale integration state, close the request and create a new one from current state. An operator must never edit a stored binding, payload hash, target snapshot, authority version, delegation ID, approval ID, or idempotency key into validity. Ambiguous provider outcomes enter reconciliation and must not be blindly replayed.

## Emergency restriction

To stop Finance agent execution without changing historical evidence, disable assignments for the affected agent, remove or deny its Finance execute grants, revoke active Finance delegation rows, and disable the affected integration when provider writes are in scope. Preserve approvals, attempts, audits, and outbox rows. Confirm the counters show denied/stale outcomes and zero successful provider dispatches, then run the P0 release gate before re-enabling. Database row deletion and manual approval mutation are not emergency controls.

## P0 release gate

Run `scripts/verify-finance-agent-p0.ps1`. The script runs the focused P0 suites, Release build, hermetic test projects, localization contracts, migration model check, and the available SQL lane; it writes the exact commands, revision, test counts, failures, working-tree manifest checksum, and evidence checksum to `artifacts/finance-agent-p0/`. Any failed, skipped, or prerequisite-missing mandatory lane is a `no_go`; security checkpoints are never averaged.

## Migration and recovery

Migration `20260831064356_AddFinanceAgentDelegationAuthority` adds only the delegation-authority table and indexes. Apply it through the normal SQL Server EF migration pipeline before enabling background Finance execution.

Rollback is safe before any workflow depends on delegated execution: migrate to the preceding migration, which drops only the new table. After delegation rows are in use, prefer forward recovery: disable affected background workflows, export the safe authority identifiers and bindings needed for diagnosis, correct the application or data through an approved migration, and resume only after authorization tests pass. Dropping the table after workflows depend on it makes those executions fail closed and loses delegation audit support, so it is not an operational recovery path.
