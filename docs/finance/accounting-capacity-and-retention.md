# Accounting capacity, service objectives, and retention

This document is the Release 0 operating contract for company-scoped accounting capacity. It defines the supported launch envelopes, the measurements used to protect them, and the only cleanup currently authorized by the application.

## Supported-volume profiles

Counts are per company. They are supported ceilings, not quotas and not production seed sizes. The API exposes both profiles and a live company-scoped count snapshot at `GET /api/companies/{companyId}/finance/accounting-capacity?profile=small|medium`.

| Resource | Small | Medium candidate |
| --- | ---: | ---: |
| Accounts | 300 | 1,000 |
| Fiscal periods | 120 | 240 |
| Posted/draft journals | 100,000 | 1,000,000 |
| Journal lines | 400,000 | 5,000,000 |
| Customer invoices | 100,000 | 1,000,000 |
| Supplier bills | 100,000 | 1,000,000 |
| Payments | 100,000 | 1,000,000 |
| Allocations | 200,000 | 2,000,000 |
| Bank rows | 250,000 | 2,500,000 |
| Evidence links/documents | 500,000 | 5,000,000 |
| Audit records | 1,000,000 | 10,000,000 |
| Provider references | 500,000 | 5,000,000 |
| Export jobs | 10,000 | 100,000 |
| Worker backlog | 25,000 | 250,000 |
| Concurrent authenticated users | 25 | 100 |
| Concurrent accounting/Finance jobs | 10 | 30 |

The small profile is release-qualified. The medium values are the next launch envelope, but the synchronous trial-balance objective is not yet qualified; see **Measured qualification** below. Do not advertise or assign the medium profile until that blocking result is resolved.

The SQL Server performance generator is test-only. It creates a unique migrated database, a real company/membership/period/account topology, deterministic source identities, and the selected profile's full journal/line target. The wider accounting integrity scenario supplies invoices, bills, approvals, payments, allocations, bank imports, reconciliation, source evidence, exports, close/lock, and recovery behavior through production services. No capacity fixture is registered in production DI or startup.

## Service objectives

Objectives are p95 unless the measurement is a current backlog or age. API acceptance includes authorization and JSON serialization.

| Operation or signal | Objective | Warning/breach threshold | Scope |
| --- | ---: | ---: | --- |
| Posting | 750 ms | 1,500 ms | One idempotent committed journal |
| Common Finance list, first page | 500 ms | 1,000 ms | Bounded company page |
| Accounting detail | 250 ms | 500 ms | One company-owned object |
| General-ledger account page | 1,200 ms | 2,500 ms | 25–1,000 lines, default 100 |
| Trial balance | 1,500 ms | 3,000 ms | One fiscal period |
| Statements | 2,000 ms | 4,000 ms | One fiscal period |
| Close validation | 3,000 ms | 6,000 ms | One fiscal period and its controls |
| Export request | 500 ms | 1,000 ms | Durable idempotent acceptance |
| Export completion | 5 min | 15 min | Request time through durable completion |
| Provider sync lag | 15 min | 60 min | Oldest active Finance connection |
| Ambiguous reconciliation backlog | 0 | 1 | Possible provider successes requiring reconciliation |
| Oldest worker queue age | 15 min | 30 min | Pending Finance execution |

`VirtualCompany.Accounting` records `accounting.operation.duration` with bounded operation/outcome/status tags; `accounting.slo.breaches`; worker queue age; provider sync lag; reconciliation backlog; expired export bytes; object failures; and cleanup outcomes/items/bytes. Company identifiers remain in structured logs, not metric tags, to keep metric cardinality safe. The capacity endpoint returns plain-English remediation for every active signal.

## Query shapes and indexes

General-ledger summary groups prior and in-period movement in SQL and materializes only one bounded page of lines. Account drill-down returns a correct running balance that includes movement before the page. Evidence is loaded only for returned entries. Trial balance consumes grouped totals and total line counts rather than an unbounded line collection. Export lists are capped at 200 items and period history at 200 events.

Migration `20260824044010_EstablishAccountingCapacityIndexes` repairs the previously model-only `trial_balance_snapshots` table so a fresh SQL Server migration chain can close and lock periods, and adds:

- `ledger_entries(company_id, fiscal_period_id, status, entry_at, entry_number)` for company/period/status report selection and deterministic ordering.
- `ledger_entry_lines(company_id, finance_account_id) INCLUDE (ledger_entry_id, debit_amount, credit_amount)` for tenant/account aggregation without reading wide journal-line rows.
- `accounting_export_jobs(company_id, status, expires_at)` for company-scoped retention eligibility.
- `background_executions(company_id, status, created_at)` for queue age/backlog reads.

The performance lane captures SQL Server's actual operator profile for the tenant/period/status ledger aggregate and requires both tenant-leading ledger indexes to appear in the plan. Migration SQL contains ordinary SQL Server indexes with supported included columns only: no provider-specific filtered predicate or new cascade path.

## Repeatable measurement

Use a dedicated disposable local or Docker SQL Server. Never point the lane at a production or shared database; each test creates and drops one uniquely named catalog.

```powershell
$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION = 'Server=localhost,1433;User Id=sa;Password=<local-secret>;TrustServerCertificate=True;Encrypt=False'
$env:VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE = 'small'
./scripts/test-matrix.ps1 -Lane accounting-performance -NoRestore
```

Repeat with `medium` before claiming the medium envelope. The test warms each operation, records seven samples, asserts p95 budgets, writes timings and the failing plan into test output, and always removes its isolated database through the test host. A missed budget blocks the selected profile; it must not be hidden by a larger timeout.

## Measured qualification

Measurements below were taken on 2026-08-24 against SQL Server LocalDB on the development workstation. Each run applied the complete migration chain to a unique database, generated the stated profile, captured the actual tenant-scoped plan, warmed the endpoints, recorded seven samples, and removed the database.

| Profile | Journals / lines | General-ledger account page p95 | Trial-balance p95 | Result |
| --- | ---: | ---: | ---: | --- |
| Small | 100,000 / 400,000 | 58.8 ms | 1,025.5 ms | Qualified: both are within 1,200/1,500 ms objectives. |
| Medium candidate | 1,000,000 / 5,000,000 | 330.9 ms | 17,931.9 ms | **Blocked:** ledger is within objective; trial balance breaches the 1,500 ms objective. |

The medium actual plan uses `IX_ledger_entries_company_id_fiscal_period_id_status_entry_at_entry_number` and the covering `IX_ledger_entry_lines_company_id_finance_account_id`; the remaining cost is the exact aggregation of five million posted lines. The failure is retained in `artifacts/test-matrix/20260824-accounting-performance-medium-covering-index/`. Resolving it requires an architecture-approved, company/period/account-scoped balance projection with transactional update, rebuild, reconciliation, freshness, and recovery contracts, or an explicit product decision to make the medium report asynchronous. The Release 0 implementation does not introduce that projection speculatively and does not weaken the objective.

The passing small evidence is in `artifacts/test-matrix/20260824-accounting-performance-small-covering-index/`. The final LocalDB SQL Server lane passed 8/8 tests in `artifacts/test-matrix/20260824-prompt56-sqlserver-final-5/`; its generated migration SQL contains the snapshot table and covering ledger-line index, and the pending-model check passed. Docker was unavailable in this workstation session, so the Docker migration/restore lane remains a release prerequisite rather than a passing result.

For migration compatibility, run the ordinary SQL Server lane against both local SQL Server and the repository Docker SQL Server restore flow. Apply migrations to a fresh catalog and a restored representative catalog, then run the pending-model check and accounting integrity scenario. The migration assembly remains `VirtualCompany.Persistence.Migrations`, so Docker restore/run uses the same migration chain as local SQL Server.

## Retention policy

| Class | Policy |
| --- | --- |
| Immutable accounting truth | Preserve posted journals/lines, voucher identity, closed snapshots, finalized report/return evidence, and source links. |
| Source/statutory evidence | Preserve documents, hashes, evidence links, and return evidence under the applicable legal policy. |
| Approval and audit evidence | Preserve decisions and explanations required to reconstruct an accounting action. |
| Provider/reconciliation evidence | Preserve acknowledgements, references, ambiguous outcomes, and reconciliation decisions. |
| Generated export content | Binary content may be removed after expiry; checksum, file manifest, request, period, content length, and policy metadata remain. Regeneration uses preserved accounting truth. |
| Operational attempts/failures | Preserve for Release 0. A shorter policy requires a separately approved archive design. |
| Explicit Simulation Lab data | Isolated and never targeted by production accounting cleanup. |
| Ephemeral caches | May expire under their owning company-scoped cache policy and must be reproducible. |

Generated export content is the only implemented accounting cleanup. It is an `AccountingAdmin` operation with this sequence:

1. `POST .../retention/preview` returns an ordered, bounded target list, aggregate eligible count/bytes, preserved-evidence statement, and SHA-256 preview token.
2. `POST .../retention/run` repeats the company-scoped eligibility query and rejects a changed target set with `409 accounting_retention_preview_stale`.
3. The bounded transaction nulls only expired completed export binary content. It retains file name/media type/checksum/content length, period, requester, request time, attempts, and expiry.
4. A successful non-empty run writes `accounting.export.content_expired` with actor, reason, correlation, target IDs, count, bytes, and preview token. Replaying the cleanup is safe and processes zero records.

Cleanup never selects another company, an unexpired export, a non-completed job, or an export whose content is already absent. It never targets journals, source/evidence identity, reports/returns, approvals, audits, provider references, reconciliation evidence, or Simulation Lab records.

## Scaling triggers and response

Move a company to the medium profile before any small-profile resource reaches 80%, or when sustained concurrency exceeds the small envelope. Investigate before increasing limits when a capacity alert, SLO breach, old queue, stale provider sync, reconciliation backlog, or failed export appears.

Scale in this order: verify query plan and tenant predicate; reduce requested page/range; resolve worker/provider backlog; add measured tenant-leading indexes; then scale SQL/app/worker resources. Do not use retention, speculative caches, denormalized balances, or disabled evidence loading as a performance shortcut. Any new projection must define company scope, source/drill-down preservation, freshness, invalidation, and recovery.
