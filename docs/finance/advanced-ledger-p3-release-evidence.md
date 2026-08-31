# Advanced ledger P3 release evidence

Evidence date: 2026-08-30  
Scope: `financial-app-p3-prompts.md`, Prompts 1–10  
Decision: **No-go until environment-gated evidence is completed**

Release classification: `blocked_by_missing_release_evidence`  
Technical decision: Prompt 10 hermetic, SQL Server fresh/upgrade, concurrency/rollback/replay, full-build, EF-model, and tenant-filter evidence is verified. The complete release candidate remains `not_verified` until the named Docker recovery, capacity, authenticated browser/provider, and qualified-review lanes are reproduced.  
Human decision: `pending`  
Statutory approval: `false`

This document is engineering release evidence. It is not statutory approval or a signed professional opinion.

## Frozen scope

| Scope item | Frozen identity |
|---|---|
| Repository HEAD used for this implementation | `25d68a4a38e668d49b75740053da00c92505748b` |
| Working-tree state | Dirty multi-prompt implementation baseline; 247 paths were changed/untracked at capture, so this is not yet an immutable release revision. |
| Prompt specification | `financial-app-p3-prompts.md`; SHA-256 `b2d108f97a807a9035d9a1d7dd13e840b60068fe10651cfb9631be93773515ea` |
| Golden scenario source | `AdvancedLedgerGoldenScenarioTests.cs`; SHA-256 `c0b47ac89af15146b509f1978d167cfe5a18112a8363b3e8a5e2b3b33f451741` |
| Golden calculation result | SHA-256 `24b966266cac456a72d441dfcf85ead000696b02ad88a49754e5583e004a2212` |
| Operations runbook | `advanced-ledger-p3-operations.md`; SHA-256 `f1e89bf05096c3211bae31b83880aabb9b7af654ff5666d58c567b0cdce64025` |
| Close orchestration migration | `20260829201630_AddAccountingCloseOrchestration.cs`; SHA-256 `0fd19b107e2cccced8a80f22c7dcf440af4fdda1a863e73d24834317499c266f` |
| Close governance migration | `20260830074937_AddAccountingCloseGovernance.cs`; SHA-256 `ae647b7eaee99de67148b4cd32b99d6983b7164f916053aecc19b8af48a19639` |
| Advanced matrix manifest | `artifacts/test-matrix/prompt10-final-20260830/matrix-manifest.json`; SHA-256 `df8fe245eed766cabfc3d75a70fcf42484479fba1dd265a4a420e40d39cec876` |
| Evaluation date | 2026-08-30, Europe/Stockholm |
| Swedish candidate pack | `sweden-statutory-candidate` / `1.4.0`; runtime definition hash `f7dd2403535ebd51e5e97137cff2aa629da09768cc45cc6a37fbf667d53b3eb6` |
| BAS catalogue | `bas-2026` / `1.1`; catalogue SHA-256 `2ed4f76eca5655bb62d77be4b30dbc6f511afa67d6470656b24a75b441672efc` |

Any source, migration, fixture, golden checksum, policy-pack, catalogue, supported limit, or runbook change invalidates this capture and requires a fresh immutable evidence version.

## Evidence audit

| Checkpoint | Evidence reviewed | Technical status | Human decision | Finding/action |
|---|---|---|---|---|
| Advanced golden calculations | Advanced-ledger Finance lane; 64 passed, 0 failed; golden checksum above | `verified` | `pending` | Currency settlement, revaluation/reversal, allocation, schedule, asset, worker replay, series/inventory governance, and aggregate close calculations reproduced. |
| Prompt 10 API/Web surface | Advanced-ledger API/Web lanes; 50 API and 2 Web tests passed | `verified` | `pending` | Authorization, close/governance endpoints, migration boundary, typed surface, and advanced workspace checks passed. |
| Prompt 10 solution compilation | Full `VirtualCompany.sln` Debug build | `verified` | `pending` | Build completed with 0 errors; existing nullable/analyzer/Razor/MAUI warnings remain visible. |
| Advanced readiness controls | `AccountingReadinessService`; focused readiness test | `verified` | `pending` | Rate, revaluation, dimension, schedule, asset, series, and explicit inventory-boundary signals are company-scoped and operator-visible. |
| Advanced recovery checksum | `AccountingRecoveryVerificationService`; recovery replay test | `verified` | `pending` | Seven advanced controls are included in the deterministic checksum; a replay reproduced the same overall and per-control hashes. |
| Swedish evidence package | `verify_virtual_company_evidence.py`; 13 verified, 0 failed | `verified` | `pending` | BAS, manifest, VAT fixtures, and review-gate hashes match; this remains engineering evidence only. |
| Fresh/upgrade SQL Server migrations | Disposable SQLEXPRESS databases; Finance/API TRX plus representative Prompt 8-to-latest upgrade TRX | `verified` | `pending` | Fresh latest-schema migration, representative upgrade, and both close layers passed. Governance did not replay `is_reportable`, account-lifecycle, or close-orchestration schema. |
| SQL concurrency, rollback, and replay | Finance SQL lane; 11 passed, 0 failed; API SQL lane; 2 passed, 0 failed | `verified` | `pending` | Atomic voucher allocation, failed-transaction rollback, concurrent idempotency replay, tenant keys, full API accounting/recovery, and migration compatibility passed. Hermetic worker tests retain stable revaluation, schedule, asset, posting, and reversal identities. |
| Docker SQL/object recovery | Docker recovery lane and `verify-accounting-recovery.ps1` manifest | `not_verified` | `pending` | Restore the coordinated pair and compare database/object plus advanced-control checksums. |
| Production-shaped reporting capacity | Small or medium profile with timings/query evidence | `not_verified` | `pending` | Supply supported-volume evidence; absence is a release stop. |
| Authenticated EN/SV browser UAT | Desktop and narrow runtime evidence | `not_verified` | `pending` | Exercise investigation and recovery paths against an owned host and retain screenshots. |
| Full solution/model/security review | Solution build, pending-model command, tenant/authorization review | `verified` | `pending` | Full build passed, EF reports no pending model changes, and all six close-governance entities now have company query filters; focused tenant/auth tests passed. |
| Qualified jurisdiction-specific review | Exact approval record and hashes | `not_verified` | `pending` | A qualified reviewer must decide the exact scope; engineering must not activate it. |

Blocking remediation is to freeze the candidate revision, run every `not_verified` lane, attach its immutable output, recalculate the scope hashes, and obtain attributable human review for any jurisdiction-specific claim. Missing evidence is not a pass and cannot be averaged into a readiness percentage.

## Implemented production scope

The release candidate contains company-scoped exchange-rate authority and refresh, dual-currency document/open-item/journal facts, realized FX settlement, period-end revaluation and reversal, governed dimensions and allocations, accounting schedules and workers, a fixed-asset book subledger, account/series governance, an explicit unsupported inventory boundary, advanced Web workspaces, typed clients, audit/telemetry, close checks, EF migrations, and English/Swedish operator surfaces. All native postings use `IAccountingPostingService`.

The deterministic golden scenario is `AdvancedLedgerGoldenScenarioTests.P3_golden_scenario_reproduces_currency_dimension_schedule_asset_and_close_controls`. Its canonical SHA-256 is `24b966266cac456a72d441dfcf85ead000696b02ad88a49754e5583e004a2212`. It retains the following exact controls:

| Control | Expected |
|---|---:|
| Foreign document / functional issue amount | EUR 100.00 / SEK 1,000.00 |
| Two settlement bank legs / net realized FX | SEK 980.00 / SEK -20.00 |
| Foreign supplier bill / functional amount | EUR 50.00 / SEK 500.00 |
| Supplier settlement bank leg / realized FX | SEK 510.00 / SEK -10.00 |
| Bank reconciliation difference | SEK 0.00 |
| Period-end revaluation / next-period reversal | SEK 30.00 / SEK -30.00 |
| Governed dimension allocation | SEK 600.00 + SEK 400.00 |
| Monthly accrual and prepayment release | SEK 100.00 each |
| First-period asset depreciation / closing NBV | SEK 516.13 / SEK 11,483.87 |
| Disposal proceeds / gain | SEK 12,000.00 / SEK 516.13 |
| Reported operating result / report control difference | SEK 400.00 / SEK 0.00 |
| Aggregate close difference | SEK 0.00 |
| Governed voucher-series result | Provider series `A` |
| Inventory capability boundary | `accounting_inventory_unsupported` |

## Required verification record

| Lane | Required evidence | Current disposition |
|---|---|---|
| Hermetic Finance/API/Web | Complete test counts and TRX or console result | Advanced-ledger matrix: Finance 64, API 50, Web 2 passed; 0 failed and 0 skipped. TRX SHA-256: Finance `dae19c789f266a843ab2dbab3dc1160783c6fca9fe8d04b9cdce1aee7d57826c`; API `24e7d4b45dfbfe61bc65950eaaea59f6bc75b16cf8ce5956fa49003f4fc722a7`; Web `40c32137c1b20eaa23b13fca6fec124f9ba2ca34cc8bf3d1082eea915c0722b5`. Paths are recorded by the matrix manifest. |
| Solution build | Zero source errors | Passed with 0 errors; nullable/analyzer/Razor/MAUI warnings remain visible. |
| EF model | `has-pending-model-changes` exits cleanly | Passed: `No changes have been made to the model since the last migration.` Existing relationship/default-value model warnings remain visible. |
| SQL Server fresh and representative upgrade | Migration IDs, database identity, integrity result | Verified on isolated local SQLEXPRESS databases. Finance SQL: 11 passed (`9b8a3de45ec157416d5b83416f5ca05bc688f6bb21a1f93a7d3be7fdaeb5fedc`). API SQL: 2 passed (`31bb5b8a766a2742ef027e45701cfec1c71f2377aef92fc707bd38be8e7e4799`). Representative close upgrade: 1 passed (`ae2c01aa0b2ddec0b62ae757bf9ed802b6cde2afed088409b5442337309aadba`). |
| SQL concurrency/worker restart | Revaluation, schedule, asset and posting replay identities | SQL posting concurrency/rollback/replay passed; hermetic advanced worker restart/replay tests are included in the 64-test Finance lane. |
| Docker migration/restore | Backup identity, restore identity, DBCC/integrity result | Environment-gated; not yet evidenced. |
| Recovery checksum | SQL plus external evidence manifest before/after | Environment-gated; not yet evidenced. |
| Browser UAT | Authenticated English/Swedish desktop and narrow screenshots | Stored design references exist; runtime comparison is not yet evidenced. |
| Capacity | Supported-volume timings and query plans | Environment-gated SQL lane is not yet evidenced. |
| Professional scope | Swedish tax/currency/asset assertions | Candidate functionality remains non-statutory until qualified review. |

## Go/no-go rule

Release is no-go if any required lane is missing, any company-isolation or authorization test fails, the EF model has pending changes, any subledger/control difference is non-zero, replay creates a duplicate journal or reversal, recovery changes the golden checksum, browser recovery paths are inaccessible, or professionally reviewed prerequisites are absent for a claimed jurisdiction-specific capability.

Use `docs/finance/advanced-ledger-p3-operations.md` for deployment, forward-fix, rate-outage, schedule-recovery, asset-correction, and retention procedures. Replace this no-go decision only with a dated, evidence-linked release-manager decision; do not infer approval from a green subset of tests.
