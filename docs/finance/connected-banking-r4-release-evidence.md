# Connected Banking and Treasury release evidence and go/no-go

Candidate date: 2026-08-29 (Europe/Stockholm)  
Decision: **NO-GO — required external evidence is missing**

## Delivered release controls

- Company-scoped readiness endpoint with capacity snapshots and twelve checks: consent expiry, feed gaps, feed lag, duplicate identity, unreconciled aging, suspense, stale approvals, ambiguous submissions, rejected instructions, unsettled batches, worker backlog, and control-account differences.
- `small` and `medium` connected-banking profiles plus explicit feed, recovery, matching, batch, webhook, treasury, and queue service objectives.
- AccountingAdmin recovery verification with deterministic checksums spanning imported rows, statement row decisions, payment instructions/executions/acknowledgements/payments/allocations, linked journals, reconciliation, audit, and object identities; duplicate identity checks; and optional statement-object SHA-256/length verification.
- Dedicated matrix lanes for hermetic failure injection, SQL recovery, production-shaped capacity, Docker migration/restore, owned browser, and approved real-provider proof. External prerequisites are emitted as `not-run`, never passed.
- Deployment, feature control, credential rotation, incident, reconciliation, forward-fix, retention, and disaster-recovery procedures that preserve imported rows, instructions, provider identities, acknowledgements, payments, journals, reconciliation, audit, and object hashes.

## Evidence obtained in this implementation session

| Check | Result |
| --- | --- |
| Finance readiness + recovery focused tests | PASS: 6 passed, 0 failed. Covers company scope, all readiness checks, capacity profiles, deterministic checksum, retained object validation, and cross-tenant rejection. |
| Readiness/recovery API integration | PASS: 1 passed, 0 failed. Covers owner route, cross-company forbidden response, profile response, and AccountingAdmin recovery result. |
| Production API compilation through focused API test | PASS. API and dependency assemblies compiled; existing repository warnings remain. |
| Standalone PowerShell parse | PASS for `test-matrix.ps1` and `verify-connected-banking-recovery.ps1`. |
| Full solution Release build | PASS: 0 errors; 153 existing warnings, primarily MAUI compiled-binding and offline NuGet audit warnings. |
| Broad API build attempt | TOOLCHAIN FAILURE on first retry: Roslyn `System.AccessViolationException` while compiling the migrations project; subsequent focused API and full solution builds compiled the same dependency graph successfully. |
| Connected-banking failure lane | PASS: Finance 34, API 12, Web 14; 60 passed, 0 failed. Manifest: `artifacts/test-matrix/20260829-prompt9-connected-banking-failure-final2/matrix-manifest.json`. |
| Connected-banking recovery lane | PARTIAL: 4 hermetic passed; SQL Server sub-result is `not-run` because its connection prerequisite is absent. Manifest: `artifacts/test-matrix/20260829-prompt9-connected-banking-recovery-final2/matrix-manifest.json`. |
| Full hermetic compatibility lane | PASS: 2,951 passed, 0 failed, 1 intentional opt-in accounting performance skip. All eight projects are green. Manifest: `artifacts/test-matrix/20260829-prompt9-hermetic-final/matrix-manifest.json`. |
| EF pending-model check | PASS: `No changes have been made to the model since the last migration.` Existing required-navigation/filter and sales default-sentinel warnings remain. |

The first broad hermetic run found an outdated hosted-service architecture allowlist for the already intentional bank consent/feed workers. The allowlist was corrected, its focused test passed, and the uninterrupted final no-build matrix then passed all eight projects. Sales source and Support grounding were executed from the full-solution-built candidate to avoid an unnecessary NuGet signature lookup. A passing internal lane cannot override an external `not-run` result.

## Required evidence and current gate

| Gate | Required proof | Current status | Promotion condition |
| --- | --- | --- | --- |
| Hermetic failure matrix | Feed/process/cursor/provider/payment/replay/security/UI automated results and TRX manifest | PASS: dedicated Prompt 9 lane, 60/60 | Retain manifest; broad compatibility rerun must also clear its allowlist/network items |
| Dedicated SQL Server | Fresh migration, representative upgrade, expired lease/process death, cursor recovery, ambiguous payment, webhook replay | NOT RUN — `VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION` absent | Run both SQL and connected-banking recovery lanes against an isolated database |
| Docker migration/restore | Fresh install plus coordinated representative SQL/object restore | NOT RUN — Docker restore environment not supplied | Attach migration history, DB integrity, object manifest, and pre/post checksum |
| Production-shaped capacity | Selected small/medium volumes and p95 results | NOT RUN — SQL/profile/generator absent | Supply dedicated fixture; meet every selected-profile objective |
| Authenticated browser EN/SV | Desktop/narrow/keyboard flows with screenshots and issue ledger | BLOCKED — owned host and identity absent | Complete every flow in `connected-banking-prompt9-uat-evidence.md` |
| Real-provider sandbox | Ingestion, payment submission, signed webhook/poll acknowledgement, rejection and ambiguity evidence | BLOCKED — provider app/key absent | Attach provider-owned IDs/status facts and finance/security review |
| Beneficiary settlement | Exact booked debit row and governed reconciliation/posting | NOT CLAIMED | Optional claim only if actually observed; provider acceptance alone is insufficient |
| Security review | Tokens, callbacks, webhook/certificate trust, logs, exports, tenant boundaries | Automated contract and authorization scope PASS in the 60-test lane; source scan found no connected-banking logger call containing token/credential/private-key/certificate/callback/payload/account values. Environment secret/cert inspection remains blocked. | Attach deployment security approval |

## Security conclusions from the implemented boundary

- Readiness is `FinanceView`; recovery is `AccountingAdmin`; both require resolved company context. Service-level tenant context rejects cross-company calls before queries.
- Recovery enumerates only explicitly company-filtered evidence, including when global filters are bypassed for verification.
- Callback state, credentials, cursors, raw provider evidence, private keys, and webhook bodies are not returned by readiness/recovery DTOs or included in the checksum output.
- Statement object verification reports only stable reason code and import-job identity, not object content or credentials.
- Provider/webhook contract tests must remain part of final evidence. Deployment security must verify secret mounts, certificate lifecycle, allowed callback URIs, HTTPS trusted webhook hosts, log sinks, export access, and retention policy.

## Go/no-go rule

Go requires every row above to be green or a documented, authorized non-applicable gate. No critical/high defect may remain, recovery checksums must match, readiness may have no blocked or not-measured check, and all named approvers must sign the exact artifact set. Missing external evidence is not waivable by an automated test pass.

Current decision is **NO-GO** because SQL/Docker recovery, production-shaped capacity, authenticated English/Swedish browser UAT, real-provider sandbox evidence, deployment security review, and signatures are absent.

## Sign-off

| Role | Name | Decision | Timestamp | Evidence signature / link |
| --- | --- | --- | --- | --- |
| Engineering owner | Pending | UNSIGNED | — | — |
| Finance operations owner | Pending | UNSIGNED | — | — |
| Security owner | Pending | UNSIGNED | — | — |
| Release manager | Pending | NO-GO | 2026-08-29 | This document; external gates above remain open |

Operational procedure: [connected-banking-production-operations.md](../runbooks/connected-banking-production-operations.md). Capacity and retention: [connected-banking-capacity-and-retention.md](connected-banking-capacity-and-retention.md). UAT ledger: [connected-banking-prompt9-uat-evidence.md](connected-banking-prompt9-uat-evidence.md).
