# Guarded internal Finance command tools

Prompt 7 keeps Laura's internal Finance command surface as an explicit allowlist. The versioned source of truth is
`FinanceExecuteToolReadinessCatalog` (`2026-09-01.prompt7.v1`). The trusted registry fails during construction when an
enabled Finance execute tool lacks a readiness contract, has no risk classification, or disagrees with its required
permission, risk tier, or approval behavior.

Every readiness entry names the owning Application contract and records authorization, risk, reversibility, approval,
target and freshness rules, idempotency, transaction behavior, external effects, retry and reconciliation behavior,
audit evidence, rollback or recovery, the authoritative after-state read, batch size, and materiality policy. It is
metadata and validation, not a second policy engine. P0 actor/effective-authority checks and the owning Finance service
are re-evaluated before mutation and remain authoritative.

## Bounded batch behavior

`finance.guarded_commands.categorize_transactions` is the only Prompt 7 batch command. It accepts 1–20 transaction
items. Each item contains a transaction ID, its expected current category, and the requested category. The shared risk
boundary resolves the exact company-scoped transactions, absolute amount exposure, and item count before policy
evaluation. The owning Finance read service is called again immediately before each item, followed by the existing
authoritative category command only when the expected state still matches.

Mixed batches are intentionally per-item transactional. A stale, missing, cross-company, duplicated, unsupported, or
otherwise ineligible item receives an explicit rejection and cannot mutate. Eligible items may proceed; the result
states whether it was partially applied and includes requested count, eligible count, mutation count, rejection count,
absolute amount exposure, and every item decision. Replaying an already-applied request produces unchanged or stale
decisions and does not repeat a category change.

## Result and recovery semantics

Successful guarded commands carry a `commandEffect` envelope with the exact bounded request, authoritative actual
result, after-state, item decisions, readiness blockers, external-effect class, retry classification, reconciliation
rule, and rollback or recovery route. Failures never claim a mutation occurred. Provider ambiguity and migration
recovery remain in their owning reconciliation workflows; accounting posting is corrected only through governed
reversal paths.

Payment initiation or release, credentials and consents, final statutory filing or sign-off, final close/lock/reopen or
year-end authority, self-approval, and ambiguous provider resolution remain permanently human-only. They are not
present in the command catalogue and cannot become available through higher autonomy or a generic endpoint, SQL,
browser, file, provider-call, or CRUD escape hatch.
