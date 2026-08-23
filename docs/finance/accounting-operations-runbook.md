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

## Accounting-system switch plans

Provider-switch plans are separate from historical native-ledger migration runs and from the accounting-authority timeline. Creating or editing a plan must leave the current `AccountingAuthorityPeriod` unchanged, must not pause posting or exports, and must not call a provider.

- Accounting viewers may list, read, and inspect allowed actions at `GET /internal/companies/{companyId}/finance/accounting/provider-switches`.
- Accounting administrators may create a draft, update its plan with the current expected version, or cancel it before target activation.
- Only one non-terminal switch may exist per company. Resolve the existing plan rather than deleting its history.
- A switch must use an existing future monthly fiscal period and its source must match the authority that covers that boundary.
- External endpoints carry a provider key; Virtual Company endpoints do not. Direction is derived from the endpoints and is never maintained separately.
- A stale version or illegal state transition is rejected without changing the plan and is recorded in business audit history.
- Cancellation is terminal and leaves the source authoritative. After target activation begins, recovery or a later controlled cutover is required instead of cancellation.

The `AddAccountingProviderSwitchLifecycle` migration is additive. Both local SQL Server and Docker SQL Server use the same `VirtualCompany.Persistence.Migrations` history. Restore with the existing local or Docker script, then apply pending EF migrations with `VirtualCompany.Persistence.Migrations` as the migrations project and `VirtualCompany.Api` as the startup project; no provider-specific database step is required.

### Read-only switch assessment

Accounting administrators queue assessment at `POST /internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/assessments` with the current switch version and a stable idempotency key. The request changes a draft to `assessing`, but performs no provider write and no ledger commit. Accounting viewers can read the latest or a specific assessment and its capability, dataset, and gap endpoints.

The assessment worker claims one bounded work item with a lease, persists every provider page and cursor, and can resume after a process restart. Configure it under `AccountingProviderSwitchAssessment` (`Enabled`, `PollIntervalSeconds`, `ClaimBatchSize`, `PageSize`, `LeaseSeconds`, and `MaximumAttempts`). Keep the page size bounded. Do not run provider inventory inline in an HTTP request.

Interpret inventory states literally:

| State | Meaning and action |
| --- | --- |
| `confirmed_absent` | The adapter completed a supported query and returned no records. |
| `not_returned` | The current adapter cannot verify a complete dataset. Do not treat it as empty. |
| `not_authorized` | A scope or valid connection is missing. Reconnect with the required scope, then replay. |
| `unsupported` | The endpoint explicitly does not support the dataset. Select a migration treatment or different target. |
| `unknown` | No safe conclusion is available. Correct provider/configuration health and replay. |

Capabilities are evidence, not marketing claims: `supported`, `partial`, `unsupported`, or `unknown`, with a safe explanation and required scope. The Fortnox adapter uses only existing paged read endpoints and the connected account's granted scopes. Rate-limit and timeout failures use bounded retries; authorization and validation failures require operator action. Tokens and provider payloads are never stored in assessment evidence.

Gap severity is deterministic and strategy-dependent. Full-history migration is strict about documents and historical evidence; opening balances and open items may allow an old attachment limitation when the source archive remains available. Account/tax mappings, open items, allocations, currency, numbering, duplicate identities, period locks, missing scope/configuration, unsupported target features, reconciliation, and aggregate mismatches remain explicit. A completed assessment with blocking gaps allows only gap resolution and replay; it does not change accounting authority.

The `AddAccountingProviderSwitchAssessment` migration adds company/switch-scoped assessment, capability, dataset, and gap tables with composite tenant foreign keys, leases, concurrency versions, bounded evidence fields, and uniqueness constraints. Apply it through the same migrations project for local and Docker SQL Server; no separate provider database step is needed. During rollback, stop the assessment worker and retain the additive evidence tables for audit and forward-fix.

### Normalized staging and mapping review

After a completed assessment, accounting administrators stage provider-neutral records under the switch `staging` endpoint. Staging is evidence only: it must never create a journal, invoice, bill, payment, provider request, or authority change. Each record retains its extraction batch, source identity and version, source and normalized hashes, bounded sanitized evidence, and one explicit disposition.

- Replaying the same source version reuses the stable staged identity. A later source version supersedes, but does not delete, the earlier evidence.
- Content changes invalidate affected mapping approvals. Create a new mapping preview; do not reuse or manually relink the stale approval.
- Exact non-material identifiers may be accepted only by deterministic backend policy. Material, ambiguous, exclusion, transformation, and manual-exception decisions use the normal approval inbox with the mapping version, affected record hashes, count, total, and evidence hash bound into the request.
- `missing`, `unsupported`, `conflicting`, `awaiting_evidence`, and `blocked` dispositions prevent progress. Full-history exclusions are additionally restricted by dataset policy.
- The completeness endpoint reconciles current staged counts to the latest completed source assessment. The switch cannot enter plan approval until every expected record has one current valid disposition.
- Evidence JSON is bounded and sanitized. Never submit provider payloads, tokens, credentials, authorization headers, or unneeded personal data.

The `AddAccountingProviderSwitchStaging` migration adds relational staged-record, mapping-set, mapping-decision, and affected-record tables. The stable source-version and current-source indexes enforce replay safety on SQL Server. Apply the same EF migration history after either the local SQL Server or Docker SQL Server restore flow; no environment-specific DDL or data rewrite is required. If application rollback is necessary, keep these additive audit tables and disable mapping actions until a forward-fix is deployed.

### Rehearsal, reconciliation, and cutover-plan approval

Once staging is complete and the latest assessment has no blocking gaps, an accounting administrator may queue a non-authoritative rehearsal. The rehearsal worker uses a leased, idempotent run and records an immutable input snapshot before calculating controls. A provider preview is used only when its adapter explicitly supports one. Fortnox currently has no non-authoritative migration preview API, so Fortnox-target rehearsals are labelled `local_target_simulation`; they validate production-normalized data without provider writes and must never be described as Fortnox acceptance.

Every rehearsal persists per-dataset counts/totals and separate deterministic checks for debit/credit equality, account/currency trial balance, customer and supplier open items, tax, bank reconciliation, opening equity, disposition completeness, duplicates, unknown provider outcomes, evidence coverage, and source freshness. Currency tolerances are explicit. Calculated failures cannot be overridden. Manual evidence is accepted only for a check marked non-calculable and must include an authorized recorder, explanation, evidence reference, applicable input hash, timestamp, and optional future expiry.

Cutover-plan generation is allowed only from a completed, passing, current rehearsal. Each plan is a new immutable version containing the source and target, effective period, strategy, dataset summary, mapping and gap hashes, accepted manual evidence, simulation disclosure, freeze window, recovery boundary, participants, and source snapshot hash. The plan hash is recalculated before approval is requested or recognized. Any staging, mapping, gap, strategy, totals, or source-snapshot change makes the plan stale and requires a new rehearsal and plan.

Plan approval uses the normal approval inbox and is separate from later activation approval. The approval request is bound to the immutable plan ID, version, hash, source snapshot, strategy, and freeze window. Approval makes the switch eligible for target preparation only; it does not freeze posting, start authority migration, write to a provider, create ledger entries, or change accounting authority.

The `AddAccountingProviderSwitchRehearsal` migration adds only company-scoped rehearsal, input, dataset, check, manual-evidence, immutable-plan, and plan-approval tables. Apply it through `VirtualCompany.Persistence.Migrations` after either local SQL Server or Docker SQL Server restore. For application rollback, stop the rehearsal worker and preserve these additive evidence tables for audit and forward-fix.

### External-provider to Virtual Company preparation

An approved, current cutover plan targeting Virtual Company may queue native target preparation. The preparation worker rechecks the immutable plan and deterministic internal readiness before doing work. Readiness covers accounting setup state, the effective open monthly period, required chart and control roles, base currency, active voucher series, tax rules, unsupported dimensions, blocking assessment gaps, and the policy-pack compliance disclosure.

Preparation creates switch-scoped candidates and validation evidence only. It must not create posted journals, invoices, bills, payments, allocations, bank reconciliations, or internal accounting authority. Journal candidates use the production posting validator in non-authoritative preview mode, which skips only the current-authority requirement; period, account, currency, balance, tax/dimension facts, voucher series, actor, and evidence rules remain enforced. Rejected candidates and archive dependencies remain visible and block activation where applicable.

Every accepted candidate keeps its source version, immutable source and evidence hashes, stable idempotency key, and a provider-source external reference. Replays reuse the existing candidate and reference. If the provider identity already belongs to a native record, preparation records the match and creates no duplicate candidate or business record. Earlier periods or unsupported source features may remain in the source archive only when that dependency is bound to the approved plan and is non-blocking for its strategy.

The `AddAccountingProviderSwitchPreparation` migration adds company-scoped preparation, readiness-check, native-candidate, validation, and archive-dependency tables with SQL Server uniqueness and tenant foreign keys. Apply the identical EF migration history after either `restore-local-sql-db.ps1` or `restore-virtualcompany-db.ps1`; no environment-specific DDL is required. For application rollback, disable `AccountingProviderSwitchPreparation` worker execution and preserve the additive evidence tables for audit and forward-fix.

### Virtual Company or external-provider to external-provider target preparation

For an approved current plan targeting an external provider, queue a target transfer batch rather than calling the provider from the request. The target-transfer worker rechecks plan approval, source snapshot, normalized staging completeness, mapping/disposition approval versions, target connection, and required scopes before it creates any outbound request. External-to-external migrations use only the normalized staged record contract; a source-provider payload must never be passed to the target adapter.

Each batch is bound to the company, switch, immutable plan ID/version/hash, target provider, package hash, and operator idempotency key. Each item derives its stable identity from company, switch, plan version, target provider, dataset, source identity/version, and action. Duplicate delivery therefore reuses the same batch, provider write request, and approval instead of creating a second target object.

Target operations have one explicit execution boundary:

- `preview_only` validates provider-neutral evidence and performs no provider write;
- `preparatory_non_posting` creates target master data only after a separate Finance integration-write approval;
- `final_authoritative` is provider-validatable but remains `held_for_cutover` until the final cutover workflow executes it.

Fortnox target preparation currently supports accounts, customers/suppliers, projects/cost centers, opening and historical vouchers, open customer/supplier documents, credits, payments, and allocations according to the connected scopes. Tax, currency, journal-line, and bank facts may be retained as preview evidence where Fortnox has no independent non-posting object. A document-binary migration stops with an actionable capability gap until a verified Inbox upload contract or approved source-archive treatment exists; it must not be silently omitted.

Immediately before an approved preparatory write, execution rechecks the plan, staged source version and hashes, mapping version, provider payload hash, and approval ID. Timeout, provider-success/local-failure, or any unknown outcome changes the item and batch to `reconciliation_required`; do not replay it. Search the provider using the stable source identity, then use the reconciliation endpoint only after the provider confirms success or confirms absence. Final authoritative items are not executable in this phase and accounting authority remains unchanged.

The `AddAccountingProviderSwitchTargetTransfer` migration adds additive company-scoped batch, item, attempt, and provider-acknowledgement tables. SQL Server uniqueness covers batch idempotency, immutable package identity, item stable identity, write request, attempt number, and acknowledgement. Apply the same EF history after either the local or Docker SQL Server restore scripts; no provider- or environment-specific DDL is required. For rollback, disable `AccountingProviderSwitchTargetTransfer`, preserve the evidence tables and existing approval/write records, and forward-fix rather than deleting ambiguous provider history.

## Final provider-switch cutover

Schedule the final cutover only from an approved, current cutover plan whose rehearsal, native preparation or external transfer package, mappings, dispositions, connections, and source assessment are all ready. The coordinator remains asynchronous until a human explicitly starts the freeze or the scheduled worker reaches the approved freeze time. Source accounting stays authoritative before that boundary.

At freeze, the coordinator rechecks the plan approval and switch version, source and target connections, blocking gaps, assessment freshness, pending exports, and uncertain provider writes inside a serializable transaction. It captures an immutable final source snapshot twice and requires both reads to match. Any change from the approved rehearsal snapshot is a safe stop: resume normal source operation, refresh staging and assessment, run another rehearsal, and approve a replacement plan. This release deliberately blocks an unapproved delta rather than applying it silently.

After a stable freeze, only the affected fiscal boundary enters `migration` authority. The target phase then follows one of two paths:

- External to internal: validated native candidates remain inert until activation. On approved activation, journal candidates are posted through the native posting service and every materialization receives a durable source-candidate receipt in the same database transaction as authority/configuration/switch activation.
- External or internal to external: final provider commands remain held until cutover. Each immutable command gets its own durable Finance integration-write approval, stable idempotency identity, attempt history, and provider acknowledgement. A timeout or unknown provider outcome requires provider reconciliation and must never be replayed blindly.

Final reconciliation persists checks for snapshot stability, approved-plan binding, target acknowledgements, deterministic financial controls, and complete source dispositions. Failed checks remain stored even when the execution becomes blocked. Activation uses a separate approval whose evidence binds the final snapshot hash and totals, reconciliation hash, provider acknowledgement hashes, and exact switch version. Changing any bound evidence invalidates the approval.

Activation commits internal materialization, authority-period completion, accounting configuration authority, provider-switch state, cutover state, and audit evidence atomically in SQL Server. Provider-side effects are outside that transaction and therefore complete earlier through the durable approval/attempt/reconciliation boundary. The legacy manual authority endpoints reject switch-backed validation or completion claims; only persisted coordinator evidence can mark a switch successful.

Use the cutover read endpoint to inspect status, current step, safe next actions, immutable snapshot, checks, approval state, retry safety, and reconciliation requirements. Cancellation is allowed only before freeze. A blocked execution may be retried only when its persisted state declares retry safe. Recovery before any target activity restores source authority without deleting evidence. Once target activity is recorded, automatic rollback is refused and the execution reports `corrective_cutover_required`; reconcile the target and complete a deliberate corrective cutover instead.

The `AddAccountingProviderSwitchFinalCutover` migration is additive. It adds company-scoped cutover execution, final snapshot, final check, activation-approval binding, and native-materialization tables, plus immutable provider-command fields on target-transfer items. Apply it through the normal Persistence.Migrations project after either local SQL Server or Docker SQL Server restore; the model and DDL are identical in both environments. On application rollback, disable `AccountingProviderSwitchCutover`, keep all new tables and provider evidence, stop only the cutover worker, and forward-fix. Do not down-migrate after target activity or delete approval, attempt, acknowledgement, snapshot, or audit history.

Cutover telemetry uses the existing `VirtualCompany.Accounting` meter. Monitor `accounting.provider_switch.cutovers`, `accounting.provider_switch.blocks`, `accounting.provider_switch.reconciliations`, and `accounting.provider_switch.stage_duration`. Metric tags contain only status, direction, provider key, stage, reason code, and safety flags; logs contain company/switch/execution/correlation identifiers but no source documents, provider payloads, credentials, or tokens.

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

The local script first uses SQL Server's configured backup directory. When a non-admin operator cannot write there and the SQL Server service cannot read the supplied user/workspace path, the script stages one uniquely named copy under the shared documents directory, uses it for the restore, and removes it in a `finally` block. Do not manually weaken repository or SQL Server directory permissions to work around backup access.

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

Do not roll back by dropping the native-accounting operations tables or reversing posted backfills. If application rollback is necessary, keep the additive schema and stop the worker. Prefer a forward-fix. Restore only from the coordinated pre-deployment database/document pair when the release decision explicitly requires data rollback and all post-backup writes are accounted for.

## Accounting-provider switch monitoring and closure

Activation starts one durable, company-scoped monitoring run. The default window is 14 days and may be configured from 7 through 30 days. `AccountingProviderSwitchMonitoring` controls the polling interval, worker claim size, lease, retry limit, window, and check cadence. Checks and incidents are retained accounting evidence; closure does not delete them.

Each successful pass records provider sync health, projection integrity, invoice completeness, mapping integrity, connection/scopes, bank reconciliation, blocked writes to the former authority, financial controls, ambiguous external outcomes, and archive availability. A repeated issue updates the same incident fingerprint and does not create another task. A new material issue creates a high- or critical-priority finance task for the migration owner. Laura's migration workspace uses the same current evidence and must not describe a stale pass as current.

Operator response:

1. Open the migration workspace and compare the latest pass, incident, task, and correlation ID.
2. Reconnect provider access only when the connection/scopes check requires it. Reconcile an ambiguous provider outcome before retrying; never retry an outcome that may already have succeeded.
3. Use operator retry only after the failure cause is corrected. Exhausted retries remain stopped until that explicit action.
4. A non-blocking exception may be accepted only with explanation, exact scope, financial impact, and retained evidence reference. Blocking incidents cannot be accepted as exceptions.
5. Submit closure only after the configured window ends, a successful pass has run at or after that boundary, no check is queued or in progress, and every issue is resolved or documented as an accepted non-blocking exception. Closure evidence is hashed into a fresh finance-approver request.
6. Monitoring remains scheduled while approval is pending. A manually queued check, later worker claim, successful pass, or failure changes the evidence and makes the approval stale; request a replacement approval from the latest evidence.
7. Close only from the current approved evidence. The switch then becomes `completed` while all monitoring evidence remains queryable.

Recovery boundaries are deliberately asymmetric. Before target activation, use the existing cancel or source-authority recovery action. After authoritative target activity, do not restore the former source. A blocking post-activation discrepancy may create a new corrective switch at a future monthly boundary; the new switch starts in `draft` and must pass the full assessment, plan, approval, preparation, cutover, and monitoring controls independently.

The operations read model reports stuck workflows, expired approvals, stale freezes, exhausted retries, ambiguous outcomes, and unreconciled totals. Treat any non-zero critical category as a release or closure stop. Metrics and structured logs are emitted under `VirtualCompany.Accounting` with company, switch, monitoring run, check sequence, failure code, and correlation ID; credentials, tokens, and document contents are excluded.

For backup and restore, include the three `accounting_provider_switch_monitoring_*` tables, approval and task rows, audit events, and matching archive/object evidence in the coordinated SQL/object manifest. Both local SQL Server and Docker SQL Server use the same EF migration. After restore, require the latest migration-history row, tenant-scoped foreign keys, unique incident fingerprints, retained closure approval/evidence hash, and a successful readiness check before enabling the monitoring worker.
