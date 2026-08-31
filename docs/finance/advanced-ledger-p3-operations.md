# Advanced ledger P3 operations

This runbook covers the Release 5 advanced-ledger capabilities implemented by `financial-app-p3-prompts.md`. The native journal remains authoritative; every correction uses the owning workflow and `IAccountingPostingService` rather than database edits or a generic manual-journal workaround.

## Supported boundary

- Functional currency is the configured company base currency. Enabled foreign currencies require approved, reproducible observations against that currency. Inverse and cross rates are allowed only when the retained lookup policy produces an unambiguous result.
- Customer invoices, supplier bills, open items, settlements, and journal lines retain document and functional amounts plus exact rate identity. Unsupported currency/tax combinations stop before numbering or posting.
- Period-end revaluation supports configured monetary cash, AR, AP, and explicitly governed monetary accounts. A posted run is corrected by reversal and replacement.
- Dimensions support governed types, members, combinations, account requirements, external mappings, and percentage/fixed allocations. Posted assignment snapshots do not change after a rename.
- Schedules support fixed recurring journals, accrual reversal, and prepayment/deferral release with a maximum of 600 planned occurrences per retained version.
- Fixed assets support straight-line book depreciation, components, improvement, transfer, impairment, disposal, and linked reversal. This is not a Swedish tax-depreciation compliance claim.
- Native inventory quantity, valuation, and COGS accounting are unsupported. The versioned commerce event boundary rejects any event that requires those capabilities; quantity remains owned by the commerce system.

## Deployment and forward fix

1. Back up SQL Server and retain its checksum and migration history before deployment. If source evidence objects are configured externally, capture their versioned manifest at the same recovery point.
2. Apply the additive P3 migrations in order through `DatabaseInitializationService`: exchange-rate authority, document currency facts, settlement facts, revaluation, dimensions, schedules, fixed assets, accounting-administration governance, close orchestration, then close governance. `20260829201630_AddAccountingCloseOrchestration` is retained as the deployed upgrade identity; `AddAccountingCloseGovernance` must create only policy, readiness, waiver, reopen, and sign-off tables.
3. Run `dotnet ef migrations has-pending-model-changes` with `VirtualCompany.Persistence.Migrations` and `VirtualCompany.Api`. Do not enable P3 workers when the command reports drift.
4. Verify accounting readiness. A blocked rate-coverage or statutory signal is a release stop. Attention signals for revaluation, dimension conflicts, schedules, assets, series, or inventory require an explicit operator disposition.
5. Start rate refresh first, then schedule generation, revaluation scheduling, and asset maintenance. Verify one lease owner and stable idempotency identities before increasing batch size.

Never roll the database backward across posted P3 facts. If application rollback is required after migrations, keep the additive schema, disable P3 workers, preserve all posted facts and evidence, and deploy a compatible forward fix. Reversals and replacement runs remain business operations, not rollback mechanics.

Before rollout, run both a fresh database migration and a representative upgrade from `20260829183551_CompleteAccountingAdministrationGovernance`. Reject the candidate if either close migration replays an earlier column/table, if the EF model has drift, or if the application cannot read the pre-settlement schema during a rolling deployment. Retain migration IDs, database names, start/end times, and TRX evidence.

## Worker restart and replay

1. Stop new leases and record every in-flight rate refresh, revaluation run, schedule occurrence, depreciation item, report regeneration, and reversal identity.
2. Restart one worker replica first. Expired leases may be reclaimed by their original stable identity; posted/completed work must be observed and skipped, never renumbered or recreated.
3. Replay the same commands and prove one posting identity, one occurrence/event item, and at most one linked journal or reversal per business identity. A duplicate or changed checksum is a release stop.
4. Reconcile advanced readiness, all subledger control accounts, document/functional currency totals, worker failure rows, and the recovery checksum before restoring normal concurrency.

## Reporting capacity and close

Run the `small` or `medium` accounting performance profile against SQL Server with production-shaped ledger, dimension, schedule, asset, and dual-currency volumes. Retain timings and query evidence for trial balance, general ledger, control reconciliation, statutory exports, and report regeneration. Do not infer capacity from hermetic tests. Close remains blocked when any advanced readiness check is failed/stale, any required evidence hash is missing, or any control difference is non-zero.

## Rate outage

1. Stop provider refresh retries when the failure is permanent, authorization-related, or ambiguous; retain the safe failure and correlation identity.
2. Existing posted conversions remain valid and immutable. Do not substitute today's rate, `1.0`, or an unapproved manual value.
3. Use an approved manual rate set only under the configured separate-review policy, with source evidence, checksum, effective dates, and correction link.
4. Rerun readiness and exact historical lookup before resuming foreign-currency issue, posting, settlement, or revaluation.

## Schedule recovery

1. Open the blocked or failed occurrence and confirm schedule ID, version hash, posting date, source identity, lease owner/expiry, approval binding, and any linked journal.
2. If a journal exists, reconcile it before retrying. Never clear an occurrence or invent another idempotency key to force a second journal.
3. Correct the authoritative account, dimension, period, approval, or evidence problem and use regenerate-safe recovery. An expired lease can be reclaimed; a completed occurrence cannot.
4. Confirm released, reversed, remaining, and exception totals before close validation.

## Asset correction

1. Inspect the retained asset class version, source document, asset/component snapshot, book events, depreciation run, and journal links.
2. Reverse the specific posted depreciation, impairment, or disposal through the fixed-asset workflow. Do not edit accumulated balances or a posted event.
3. Apply the corrected prospective event and verify cost, accumulated depreciation, impairment, and net book value against the linked native-ledger accounts.
4. Legacy `FinanceAsset` records without sufficient book facts remain visible migration conflicts until explicitly mapped or excluded.

## Retention and recovery evidence

Retain exchange-rate checksums after protected raw payload expiry; retain every selected observation, conversion, posted document snapshot, allocation application, schedule version/occurrence, asset class/event/run, revaluation population/proposal/reconciliation, series gap explanation, audit event, and journal identity for the applicable accounting retention period. Recovery proof compares the SQL backup identity, external evidence manifest, row/control totals, and the P3 golden checksum before and after restore.

Restore SQL and external evidence objects to the same recovery point, verify the object manifest before enabling workers, and run `verify-accounting-recovery.ps1` with the expected manifest, database checksum, and advanced-control checksum. Then regenerate reports from immutable accounting facts and compare per-control hashes. A mismatched or unavailable object, subledger total, dual-currency total, report checksum, or golden checksum requires no-go with the named object/control and a forward-fix owner; partial recovery is never reported as success.
