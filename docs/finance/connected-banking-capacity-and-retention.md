# Connected-banking capacity, service objectives, and retention

This is the operating contract for the connected-banking and treasury release. Counts are per company. The live, company-scoped assessment is available to `FinanceView` users at `GET /api/companies/{companyId}/finance/connected-banking-readiness?profile=small|medium`. A breached capacity or a blocked/not-measured readiness check prevents readiness.

## Supported-volume profiles

| Resource | Small | Medium candidate |
| --- | ---: | ---: |
| Active/historical bank connections | 25 | 100 |
| Feed account checkpoints | 100 | 500 |
| Normalized feed transactions | 250,000 | 2,500,000 |
| Open matching candidates | 50,000 | 500,000 |
| Payment batches | 25,000 | 250,000 |
| Provider webhook receipts | 250,000 | 2,500,000 |
| Open feed/payment worker items | 25,000 | 250,000 |
| Concurrent authenticated users | 25 | 100 |
| Concurrent feed jobs | 4 | 16 |
| Concurrent payment jobs | 4 | 16 |

The values are ceilings, not quotas. `small` is the intended first qualification profile. `medium` remains a candidate until its SQL Server, browser, and provider evidence is signed. Move a company before a resource sustains 80% of its ceiling; do not raise a limit to hide an integrity or backlog signal.

## Service objectives

| Operation or signal | Objective | Breach | Measurement |
| --- | ---: | ---: | --- |
| Feed page commit p95 | 1,500 ms | 3,000 ms | Provider page, protected source evidence, normalization, and atomic checkpoint commit |
| Interrupted feed recovery | 15 min | 30 min | Expired lease/cursor recovery to gap-free coverage |
| Matching candidates p95 | 1,500 ms | 3,000 ms | First 250 company candidates with rule evidence |
| Payment-batch validation p95 | 2,000 ms | 4,000 ms | 1,000 current instructions including approval, beneficiary, cash, and source-version checks |
| Signed webhook acceptance p95 | 500 ms | 1,000 ms | Signature, replay identity, acknowledgement, and durable continuation |
| Treasury workspace p95 | 1,500 ms | 3,000 ms | Bounded 50-account, 50-payment, 50-exception read model |
| Oldest feed/payment work | 15 min | 30 min | Queue age, including expired leases and reconciliation-required work |

## Repeatable matrix

Run all commands from the repository root. Every lane writes a manifest below `artifacts/test-matrix/`; a `not-run` result is a release stop, never a pass.

```powershell
./scripts/test-matrix.ps1 -Lane connected-banking-failure -NoRestore
./scripts/test-matrix.ps1 -Lane connected-banking-recovery -NoRestore

$env:VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION = '<dedicated disposable SQL Server>'
$env:VIRTUALCOMPANY_CONNECTED_BANKING_PERF_PROFILE = 'small'
./scripts/test-matrix.ps1 -Lane connected-banking-performance -NoRestore
```

The hermetic failure lane proves overlapping/repeated feed pages, interrupted-cursor recovery, pending-to-booked promotion, rate limiting, malformed provider responses, payment timeout ambiguity, webhook signature/replay behavior, recovery checksum determinism, tenant isolation, authorization, and UI truth boundaries. The SQL recovery lane adds real database concurrency, lease takeover, worker-restart ambiguity, and the unique webhook identity. The production-shaped performance lane must be backed by a dedicated volume generator and signed timings before either profile is qualified; this checkout deliberately records `test-fixture-not-configured` if only the environment variables are supplied.

## Retention and recovery invariants

| Evidence class | Policy |
| --- | --- |
| Imported normalized rows and stable identities | Preserve. Never delete or rewrite to make a retry pass. |
| Protected provider/source evidence | Retain ciphertext according to the provider/legal policy; after approved expiry, preserve SHA-256, normalized trace, source identity, and purge audit evidence. |
| Statement objects | Preserve object key, content length, SHA-256, import job, row identities, and import decisions. Coordinate object and database backups. |
| Payment instructions and approvals | Preserve immutable instruction-set version/hash, approval binding, actor, and beneficiary/account evidence. |
| Provider writes | Preserve request hash, provider identity when known, every attempt, acknowledgement, webhook receipt, and ambiguous/rejected state. |
| Payments, allocations, journals, and reconciliation | Preserve authoritative records and links. A rollback may not detach or rewrite them. |
| Audit and security evidence | Preserve safe reason codes, actors, correlation IDs, and signature/replay decisions; never preserve secrets or raw sensitive webhook bodies in logs. |
| Ephemeral caches | May expire only when reproducible from preserved company-scoped truth. |

Recovery uses `POST /api/companies/{companyId}/finance/connected-banking-readiness/recovery-verification`, restricted to `AccountingAdmin`. It produces a deterministic SHA-256 over company-scoped connections; feed source and normalized imported rows; statement jobs and row decisions; payment instructions, executions, acknowledgements, webhooks, settlements, payments and allocations; bank/payment journal links and their linked journals; reconciliation results; audit events; and statement-object identities. With `verifyObjectContent=true`, every retained statement object is re-read and compared with its stored hash and length. Duplicate identity or object mismatch is blocking.

Use `./scripts/verify-connected-banking-recovery.ps1` with a short-lived `VC_CONNECTED_BANKING_OPERATOR_TOKEN` before backup and after restore. Compare the checksums from the same coordinated SQL/object snapshot. A different checksum, missing object, duplicate identity, or invalid readiness result blocks deployment.
