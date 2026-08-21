# Accounting operations and recovery runbook

This runbook covers the native, company-scoped accounting ledger. Posted journal lines remain the financial source of truth. Provider records, source documents, report snapshots, and simulation records are supporting workflow or evidence; they must never be used to replace posted ledger totals.

Country-neutral mode supplies basic bookkeeping behavior only. It does not establish local tax or statutory compliance. Before release in a jurisdiction, record a locally validated policy pack and retain the validation decision as audit evidence.

## Access and safety

- Readiness and migration status require `finance.accounting.view`.
- Starting migration, resolving a migration conflict, and recovery verification require `finance.accounting.admin`.
- Always use the company-specific route. A company identifier from a document, provider response, or incident ticket is not authorization.
- Preserve the database, object-storage snapshot, correlation identifier, and safe failure output before repair. Never edit posted journal rows or voucher numbers directly.
- Repair accounting history with an evidence-backed correction in an open period. Historical migration may fill only facts that are deterministic from existing evidence.

## Initial accounting activation

1. Complete accounting setup with base currency, fiscal-year start, authority, policy pack/version, chart template, account roles, voucher series, and rounding policy.
2. Confirm the setup status is ready. Country-neutral setup must remain visibly marked as not supplying country-specific compliance.
3. For an empty company, start the historical migration check once. A `not_required` result is expected and is not a blocker.
4. Review the operations response at `GET /internal/companies/{companyId}/finance/accounting/operations`.
5. Do not enable posting until the configuration signal is ready and every blocking migration conflict is resolved.

## Historical migration

Start migration with a stable operator idempotency key. The worker claims bounded batches with a lease and progresses through inventory, accounts, journals, and reports. Repeating the same key returns the same run; an expired lease can be reclaimed safely.

The migration may deterministically fill account class and normal balance, voucher identity, posting/document dates, base currency, source version, idempotency identity, source mapping, policy-pack version, posting identity, and voucher sequence state. It inventories conflicting bank reconciliation state and ambiguous provider outcomes. Known source tax amounts without immutable journal tax facts remain conflicts.

Do not mark a conflict resolved merely to make readiness green. Correct or attach the underlying evidence first, then record a plain-English resolution. Start a new migration run when the source record has changed. Typical conflict actions are:

| Conflict | Operator action |
| --- | --- |
| Accounting configuration missing | Complete setup from locally reviewed facts; do not infer currency, authority, or pack. |
| Account semantics ambiguous | Classify the chart account and normal balance with an accountant. |
| Journal source missing or mismatched | Link one verified business source and retain its immutable version. |
| Currency ambiguous | Confirm the base-currency amounts from source evidence. |
| Voucher ambiguous | Map the historical voucher; never allocate a replacement over the old record. |
| Policy version unknown | Record the pack that was effective on the posting date; never apply the current pack retroactively. |
| Tax facts unknown | Reconstruct only from reviewed source evidence and the historical pack. |
| Reconciliation conflict | Review the transaction, payments, source mapping, suspense entry, and posting state together. |
| Provider outcome ambiguous | Query the provider by stable identity, then reconcile as sent or not sent. Do not blindly retry. |
| Evidence missing | Restore or attach the verified document and confirm its SHA-256 content hash. |

Each completed run produces one cutover report per fiscal period. Review opening balance, debit/credit totals, receivable, payable, bank and suspense balances, tax-fact count, trial-balance comparison and mismatch count, provider references, evidence links, snapshots, issues, and the report checksum.

## Readiness and alert response

The accounting operations response reports these bounded signals:

- configuration and selected policy-pack validity;
- historical migration state and unresolved conflicts;
- recent failed accounting audit outcomes;
- approvals pending longer than seven days;
- suspense-account balance;
- open reconciliation follow-up;
- draft journals that affect close review;
- accounting export and provider-reconciliation backlog;
- failed or blocked report snapshot regeneration;
- duplicate source-version or idempotency posting identities.

`blocked` prevents release or cutover. `attention` is operator-visible and must be assessed for the affected period before close. Metrics use the `VirtualCompany.Accounting` meter and include migration run, record, conflict, recovery, and duration measurements. Structured logs include company, migration run, phase, conflict reason/entity, provider or source where relevant, and correlation identifiers; document contents and credentials are excluded.

## Posting incidents

1. Capture company, correlation ID, source type/id/version, action, period, and safe reason code.
2. Check whether the command already produced a `LedgerPostingIdentity`. A lost response with the same payload must resolve to the existing journal.
3. If the same identity has a different payload, stop and reconcile the caller's source version. Do not change the persisted payload hash.
4. Confirm the period is open, authority allows the action, approval version is current, all accounts are active/posting-enabled, and debit equals credit at configured precision.
5. For a transient SQL failure, retry the identical command. For a permanent policy failure, correct the proposal. For an unknown provider outcome, follow provider ambiguity handling below.
6. Never update a posted journal. Use a linked reversal or adjusting entry in an open period.

## Voucher-sequence incidents

- A rolled-back transaction may leave no committed voucher; a committed voucher number is never reused.
- Query the voucher series, fiscal year, `VoucherSequence`, and posted journals together.
- If a duplicate series/year/number is reported, stop posting for the company, preserve the database, and run recovery verification. Treat it as an integrity incident.
- If the sequence is behind deterministic historical vouchers, rerun historical migration. Do not lower a sequence or renumber posted history.
- Validate concurrency behavior with the SQL Server-backed voucher tests before deployment after any sequence change.

## Reconciliation and suspense

- Resolve unmatched and partial bank items from the reconciliation surface. An unexplained amount must remain open or in suspense.
- Reclassify suspense with an evidence-backed journal; do not mutate the original posted entry.
- For a conflict posting state, inspect payment links, cash-ledger links, source mappings, follow-up records, and source version as one unit.
- A non-zero suspense signal or open reconciliation follow-up is an explicit close review item.

## Period close failures

1. Run the close checklist and retain its report/checksum.
2. Resolve drafts, unbalanced legacy conflicts, stale approvals, suspense, reconciliation, snapshot failures, and provider/export ambiguity.
3. Regenerate statements and trial-balance snapshots after in-scope corrections.
4. Close and lock only after reports reconcile. Reopening is a separate authorized and audited operation.
5. Corrections after lock go to a valid open period unless an authorized reopen is completed.

## Policy-pack upgrades

- Preview the upgrade and record a future effective date.
- Validate required account roles, report mappings, rounding, tax behavior, and export support.
- Historical journals retain their original pack key/version and immutable facts.
- Do not claim a pack is statutory-compliant without local review evidence.
- If the effective date or historical pack is uncertain, retain a migration conflict instead of assigning the new pack retroactively.

## Provider ambiguity and export recovery

- Native ledger authority does not depend on a provider connection. Providers remain adapters.
- Provider writes require the established approval, durable execution, stable identity, bounded retry, and reconciliation boundary.
- Timeout, unknown outcome, or provider-success/local-failure must end as `reconciliation_required`, not success.
- Search the provider by stable source identity. Reconcile as exported only with provider evidence; reconcile as not sent only when absence is confirmed. Retry only the confirmed-not-sent case.
- Stale credentials, missing scope, validation failures, and permanent failures require operator correction. Do not hide or continuously retry them.

## Coordinated database and document backup

Accounting evidence spans SQL Server and the configured `CompanyDocuments:Storage:RootPath` (by default `src/VirtualCompany.Api/App_Data/object-storage`). Use a write-consistent backup window:

1. Put accounting writes, migration workers, document uploads, report regeneration, and provider exports into a controlled maintenance pause.
2. Run `verify-accounting-recovery.ps1` with `-VerifyObjectContent -RequireReady -WriteChecksumPath <path>` for every company in scope. Retain the checksum output with the backup manifest.
3. Take a SQL Server full backup. Record its SHA-256, SQL Server version, database name, latest EF migration, UTC start/end, and backup identifier.
4. Snapshot or archive the entire configured object-storage root during the same pause. Preserve relative storage keys, metadata, ACL/encryption configuration, and retention policy. Record the archive/snapshot SHA-256 and object count.
5. Record both artifacts and every pre-backup accounting checksum in one immutable manifest. A database backup without its matching object snapshot is incomplete.
6. Resume writes only after both artifacts and the manifest are durable.

For hosted object storage, use its versioned snapshot mechanism rather than copying through the API. The database and object snapshot must share the same maintenance-window identifier.

## Restore verification

Restore into an isolated target first. The existing SQL paths remain supported:

- Docker SQL Server: `./restore-virtualcompany-db.ps1 -BackupPath <backup> -DatabaseName <isolatedName>`
- Local SQL Server: `./restore-local-sql-db.ps1 -BackupPath <backup> -DatabaseName <isolatedName>`

Then:

1. Verify the SQL backup and object archive hashes against the manifest.
2. Restore the matching object snapshot to the configured storage root without merging it with unrelated or newer objects.
3. Apply EF migrations using the Persistence.Migrations project and API startup project.
4. Start the API and require `/health/ready` to pass, including database and configured object-storage health.
5. Run `verify-accounting-recovery.ps1 -CompanyId <id> -VerifyObjectContent -RequireReady -ExpectedChecksum <preBackupChecksum>` for every restored company.
6. Require valid voucher uniqueness, balanced lines, source mappings, evidence hashes, journal audit references, snapshots, provider references, and the exact deterministic checksum.
7. Compare every period cutover report and financial statement to the retained release/backup evidence.
8. Do not promote the restore if any object is missing, any hash or checksum differs, a provider outcome is ambiguous, or readiness is not `ready`.

Recovery verification writes an audit event and returns no document content. A failed verification is a safe, explicit stop condition.

## Deployment, rollback, and forward-fix

Deploy additively: back up database and documents, apply the EF migration, deploy API/worker code with the migration worker disabled if a staged cutover is required, confirm health, enable the worker, start company migrations, resolve conflicts, compare cutover reports, then enable accounting writes. New empty companies may proceed directly after setup.

Do not roll back by dropping the Prompt 10 tables or reversing posted backfills. If application rollback is necessary, keep the additive schema and stop the worker. Prefer a forward-fix. Restore only from the coordinated pre-deployment database/document pair when the release decision explicitly requires data rollback and all post-backup writes are accounted for.
