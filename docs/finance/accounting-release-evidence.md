# Native accounting Prompt 10 release evidence

Evidence date: 2026-08-20 (Europe/Stockholm)

This document records the verification performed for Prompt 10. It is repository-backed evidence for the native, country-neutral accounting slice; it is not a claim that unrelated Mobile or pre-existing Web test failures are green.

## Scope and delivered behavior

- Company-scoped, bounded historical migration for account semantics, journal identity, vouchers, source/version links, policy-pack provenance, known tax facts, evidence, reconciliation state, and provider ambiguity.
- Durable migration runs, leases, bounded retries, process-death recovery, progress counts, versioned conflicts, audit events, and one cutover report per fiscal period.
- No ambiguous historical fact is invented. Ambiguous account, source, currency, voucher, pack, tax, evidence, reconciliation, and provider outcomes remain operator-visible conflicts.
- Readiness signals cover configuration/pack validity, migration state, failed audit outcomes, stale approvals, suspense, reconciliation, close drafts, export/provider backlog, snapshot failures, and duplicate source/idempotency identities.
- Coordinated SQL/document recovery verification covers balanced journals, voucher uniqueness, source/evidence links, SHA-256 object content, audit references, snapshots, provider references, and a deterministic evidence checksum.
- Legacy and simulation companies without native accounting configuration retain payment/reconciliation evidence without being forced to create a journal. Strict native settlement posting starts only after setup establishes accounting configuration; migration then provides the evidence-preserving cutover path.
- Optional providers remain adapters. Unknown provider outcomes are reconciliation work, never implicit success.

## Migrations and compatibility

Migration order relevant to the final cutover:

1. `20260820135900_EnsureBankPostingStateConstraintBeforeConvergence` is an idempotent compatibility bridge for an older executable migration whose target model contained a constraint that its `Up` method did not create.
2. `20260820182937_AddAccountingAuthorityContextToProviderWrites` adds the accounting date, authority operation, authority period, and lookup index used by provider writes.
3. `20260820193309_CompleteAccountingOperationsRecovery` adds migration runs, conflicts, cutover reports, indexes including the filtered single-active-run constraint, and corrects the bank import row company foreign key to avoid a SQL Server multiple-cascade-path failure.

`dotnet ef migrations list` discovered all three migrations in the expected order. `dotnet ef migrations has-pending-model-changes` returned `No changes have been made to the model since the last migration.`

The empty database path passed on:

- Docker SQL Server Developer Edition 16.0.4245.2.
- Local SQL Server Express Edition 16.0.1000.6.

The representative restore started at `20260819150634_ImplementNativeLedgerKernel` and upgraded through every Prompt 3-10 migration on both engines. This is a real upgrade path, not an already-current backup.

## Configuration, authorization, and side effects

`AccountingMigration` configuration defaults:

- enabled: `true`
- poll interval: 15 seconds
- batch size: 50
- claim batch size: 4
- lease: 60 seconds
- maximum consecutive attempts: 3

Readiness and migration status use `finance.accounting.view`. Starting migration, resolving conflicts, and recovery verification use `finance.accounting.admin`. API tests verify employee denial and cross-tenant denial. Background queries explicitly retain company predicates even when query filters are bypassed for worker execution.

Migration side effects are SQL-only and audited. It does not call accounting providers. Provider records with unknown outcomes become conflicts. Structured logs and the `VirtualCompany.Accounting` meter carry company, migration run, phase, safe reason/source identifiers, and correlation identifiers; secrets and document content are excluded.

## Exact build and test evidence

Secrets are omitted from commands below. The SQL-backed tests used an environment-provided development connection string to an isolated database created and dropped by the test.

| Command | Result |
| --- | --- |
| `dotnet build src/VirtualCompany.Api/VirtualCompany.Api.csproj -c Release --no-restore -v:minimal` | Passed; 15 existing nullability warnings in Sales/Operations/API code, 0 errors. |
| `dotnet build src/VirtualCompany.Web/VirtualCompany.Web.csproj -c Release --no-restore -v:minimal` | Passed; 0 warnings, 0 errors. |
| `dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj -c Release --no-restore -v:minimal` with Docker SQL connection | Passed 83; failed 0; skipped 0. Includes the empty SQL migration, posting atomicity, sequence concurrency, rollback, tenant keys, immutability, two policy-pack fixtures, Prompt 10 migration, transient persistence recovery, and object recovery checks. |
| `dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Accounting\|FullyQualifiedName~ManualJournal\|FullyQualifiedName~ReportingPeriodClose\|FullyQualifiedName~FinancePeriodReporting\|FullyQualifiedName~BankTransaction\|FullyQualifiedName~Reconciliation\|FullyQualifiedName~FinancePayment\|FullyQualifiedName~PaymentDomainModel' -v:minimal` | Passed 105; failed 0; skipped 0. |
| `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj -c Release --no-restore` with the eight accounting surface/client classes selected | Passed 18; failed 0; skipped 0. |
| `dotnet test tests/VirtualCompany.Finance.Tests/VirtualCompany.Finance.Tests.csproj -c Release --no-restore --no-build --filter 'FullyQualifiedName~AccountingPostingSqlServerIntegrationTests' -v:minimal` with local SQL Express | Passed 1; failed 0; skipped 0; empty local database created, migrated, verified, and dropped. |
| PowerShell parser validation of `restore-local-sql-db.ps1`, `restore-virtualcompany-db.ps1`, and `verify-accounting-recovery.ps1` | All parsed without errors. |

The broad `VirtualCompany.sln` Release build was also attempted. It built the accounting/API/Web projects but ended with two unrelated Mobile environment errors: missing `net9.0-maccatalyst/maccatalyst-arm64` restore assets and denied writes beneath the global Android MAUI package cache. The focused production API and Web builds above are green.

The full Web test project was attempted before the accounting-focused selection: 242 passed and 113 failed. Those failures are outside the Prompt 10 surfaces and are concentrated in existing query-bound bUnit harnesses, older navigation expectations, localization service registration, and dashboard timing. Prompt 10 adds no new accounting UI page; its new typed operations client is included in the green 18-test accounting selection.

## Failure-injection coverage

| Boundary | Verified outcome |
| --- | --- |
| Duplicate command / same idempotency key | Returns the same journal or migration run; different payload is an explicit idempotency conflict. |
| Concurrent voucher allocation | SQL Server test returns one stable journal and enforces sequence concurrency. |
| Transaction rollback | No committed voucher/journal or partial source identity survives the forced failure. |
| Process death / expired migration lease | The same run resumes below the attempt limit; repeated lease expiry ends in `accounting_migration_lease_recovery_exhausted`. |
| Persistence transient/retry boundary | A one-shot timeout injected at the EF persistence boundary requeues the same migration run with attempt 1, retains the completed durable checkpoint without replay, and resets the consecutive-failure state after the next successful batch. |
| Provider timeout | Classified as unknown outcome and reconciliation required. |
| Provider success / local failure | Classified separately as provider-success/local-failure and reconciliation required. |
| Stale approval/source version | API tests reject with conflict and create no partial journal. |
| Cross-tenant identifier | Finance/API authorization and SQL tenant-key tests reject the operation. |
| Locked/closed period | Posting tests return an explicit period policy failure. |
| Policy-pack change | Historical journal pack provenance remains immutable; pack validation and two contrasting synthetic pack fixtures pass. |
| Ambiguous history | The source row remains unchanged and one durable operator conflict is produced. |

## Coordinated recovery rehearsal

The source database was backed up using SQL Server `COPY_ONLY`, `CHECKSUM`, `COMPRESSION`, and `RESTORE VERIFYONLY`:

- backup size: 26,451,968 bytes
- backup SHA-256: `3581CD69C41D188B43043DD20ED2A8330B41295AE37391ACF339B01C9E19A710`
- SQL backup result: 18,138 pages processed; backup set valid

The matching object-storage snapshot contained six files:

- manifest SHA-256: `F878378E34ABA3FDB88265F106A5D1098A35CFE97ADDB48241A9C549EFB5BD02`
- archive SHA-256: `97D2A997250CE38B6D1B4D44A5B80CBF708D02CB91D9D54862EDF91CAF7E1100`
- restored manifest comparison: exact match

Docker and local SQL restore rehearsals each:

1. restored the representative backup into a uniquely named isolated database;
2. applied migrations from Prompt 3 through `20260820193309_CompleteAccountingOperationsRecovery`;
3. passed `DBCC CHECKDB`;
4. contained all three Prompt 10 tables and the latest migration-history row;
5. retained one database document reference with the exact restored storage key and length (2,833 bytes);
6. matched that object to SHA-256 `F75539ADB2C1D7E34F2DCC673E9D7869086C26D2DB52CFED1258356313663AE3`.

The local restore script was hardened to use either `Invoke-Sqlcmd` or `sqlcmd`. When a non-admin operator cannot copy into SQL Server's protected backup directory, it can use the original absolute backup path after the operator grants that one file read access to the SQL Server service account. SQL Server still fails explicitly if access is absent.

The isolated databases, temporary object archive, temporary workspace backup, temporary SQL service ACL, and dedicated Docker backup were removed after evidence capture. The source database and object storage were not modified.

## Deployment and recovery decision

Deployment order is additive:

1. create a coordinated SQL/object backup and manifest;
2. apply migrations;
3. deploy API and worker code with the migration worker disabled if staged rollout is required;
4. verify health and accounting readiness;
5. enable the worker and start migrations with stable idempotency keys;
6. resolve evidence-backed conflicts and compare period cutover reports;
7. enable native accounting writes only for ready companies.

Do not roll back by dropping migration tables or renumbering/mutating posted journals. Disable the worker and forward-fix application behavior. A data rollback requires the coordinated pre-deployment SQL/object pair and an explicit accounting decision covering post-backup writes.

The operator procedure is [accounting-operations-runbook.md](accounting-operations-runbook.md). Automated checksum verification is provided by `verify-accounting-recovery.ps1` at the repository root.

## Residual risks and release boundary

- Country-neutral mode is bookkeeping infrastructure, not local tax or statutory certification. A jurisdiction-specific release requires a locally reviewed policy pack and retained validation evidence.
- An unvalidated policy pack, ambiguous provider outcome, unresolved migration conflict, non-zero blocking readiness signal, or recovery checksum mismatch is a release stop.
- EF model validation still reports existing required-navigation/global-filter warnings and the existing sales-meeting default-sentinel warning. They were present outside the Prompt 10 accounting changes and did not block the tested accounting paths.
- Repository-wide Mobile build and unrelated Web test health remain separate release gates as recorded above. No unresolved Prompt 10 critical/high accounting finding remained after the 105-test compatibility sweep.
