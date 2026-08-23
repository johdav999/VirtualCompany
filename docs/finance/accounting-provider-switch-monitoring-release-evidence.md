# Accounting-provider switch monitoring release evidence

This document records the production evidence for financial migration prompt 10. It complements `accounting-operations-runbook.md`; it does not replace the native-ledger Prompt 10 evidence in `accounting-release-evidence.md`.

## Delivered controls

- Activation atomically starts a 7–30 day durable monitoring run and moves the switch into `monitoring`.
- A leased, bounded worker records ten post-activation checks, stable incident fingerprints, deduplicated finance tasks, bounded retries, safe failure state, and company/correlation identifiers.
- Closure requires the elapsed window, a successful check at or after the end of that window, no queued or leased check, no open issue, a current evidence hash, finance-approver approval, and explicit operator completion. Non-blocking exceptions require explanation, scope, financial impact, and an evidence reference before approval is requested.
- Scheduled monitoring continues while closure approval is pending. Every later manual queue, worker claim, check, or failure changes the closure evidence; stale approval can never close the switch and must be replaced from current evidence.
- Former-authority posting attempts are blocked by the authority policy and now produce durable monitoring audit evidence.
- Pre-activation cancellation/source restore remains available. Post-activation rollback is prohibited; a blocking discrepancy can create a new corrective cutover at a future monthly boundary.
- Accounting view permission protects monitoring and operations reads. Accounting admin protects execution, retry, exception, closure, and corrective-cutover mutations. Every query and relationship retains company scope.
- The migration workspace shows current monitoring evidence, responsible ownership, safe next actions, provider recovery links, closure controls, and operator health categories. English and Swedish resources are included.

## Persistence and compatibility

Migration `AddAccountingProviderSwitchMonitoring` adds runs, immutable check history, incidents, worker indexes, concurrency versions, closure evidence, task/approval links, and tenant-scoped switch, activation, approval, and corrective-switch foreign keys. It is additive and updates the model snapshot. The migration must apply through `VirtualCompany.Persistence.Migrations` with `VirtualCompany.Api` as the startup project for both local and Docker SQL Server restore/run flows.

## Verification record

The implementation run produced the following evidence:

| Check | Result |
|---|---|
| API Release build | Passed on 2026-08-22; 0 errors. |
| Web Release build | Passed on 2026-08-22; 0 errors. |
| Full solution Release build | Passed on the final redo build with 0 errors; only existing warning diagnostics remain. |
| EF migration generated with tenant-scoped foreign keys | Passed; `AddAccountingProviderSwitchMonitoring`, its designer, and the model snapshot are checked in. |
| Focused finance/API/Web tests | Passed on the redo: all 170 Finance tests (1 SQL Server test skipped by its opt-in guard), 7 provider-switch monitoring/cutover/API tests, and 17 migration workspace/API-client/authority Web tests. The Finance total includes 5 monitoring domain tests and pending-approval continuity coverage. |
| Pending-model-change check | Passed: EF reported no changes since the last migration. |
| Local SQL Server migration compatibility | Passed against an isolated SQL Express database from the initial baseline through `AddAccountingProviderSwitchMonitoring`; the isolated database was removed after verification. The run also corrected unsupported filtered-index predicates and multiple-cascade paths in the preceding provider-switch migrations. |
| Local and Docker SQL Server restore proof | Passed on the redo. A temporary database was migrated from the initial baseline through `AddAccountingProviderSwitchMonitoring`, backed up with checksum, restored through both `restore-local-sql-db.ps1` and `restore-virtualcompany-db.ps1`, and validated with `DBCC CHECKDB`. Both restores reported the same final migration, 33 provider-switch tables, 70 provider-switch foreign keys, and 142 provider-switch indexes. The run exposed and fixed the non-admin local restore path: a workspace backup is now copied to a uniquely named shared staging file when SQL Server cannot read it directly, and the staged file is always removed. The fixed path passed a second functional restore. All temporary databases and backup copies were removed after verification. PowerShell parser validation also passed for both restore scripts and `verify-accounting-recovery.ps1`. |
| UI verification | The screenshot-first ImageGen reference was visually inspected, and rendered component tests passed for monitoring evidence, incidents, recovery actions, operations health, and localization. An authenticated live-browser check remains a release-environment check because this workspace has no authenticated seeded monitoring dataset. |
| Complete solution test invocation | Ran on the redo on 2026-08-22 and returned exit code 1. The prompt 10 focused suites passed, but the repository-wide API project remains red: 1,807 passed and 205 failed of 2,012; the Web contract project also retains 14 unrelated failures. Failures span pre-existing/non-monitoring areas including dashboard expectations, simulation configuration, SQLite/SQL Server assumptions, finance seed/query behavior, agent policy fixtures, and unrelated test isolation. This is a release stop but does not invalidate the focused monitoring evidence above. |

## Release and recovery decision

Do not enable the monitoring worker until the migration is present, `/health/ready` is healthy, and the operations read model can be queried with company-scoped accounting-view access. Stop release for an unresolved blocking incident, stale approval, exhausted retry, stale freeze, ambiguous provider outcome, missing archive evidence, pending EF model changes, or failed restore validation.

Application rollback keeps the additive schema and disables the worker. Do not delete monitoring evidence or restore former authority after target activity. Prefer a forward fix or a separately approved corrective cutover. A database rollback requires the coordinated pre-release SQL/object backup and explicit accounting treatment of every post-backup write.
