# Close and compliance production operations

## Purpose and release invariant

This runbook governs production operation of the combined accounting close, report suite, compliance calendar, audit package, external-accountant review, lock, recovery, and year-end rollover. The release invariant is simple: a release is **no-go** unless the company/period backend decision is `ready`, every automated proof lane passed, and every external evidence record is approved for the same company, fiscal period, evidence hash, and revision.

`GET /api/companies/{companyId}/finance/close-compliance-release-readiness?fiscalPeriodId={fiscalPeriodId}` is query-only, requires `AccountingAdmin` and active company context, and returns ten signals, source links, and a deterministic SHA-256 evidence hash. It must not be used as a substitute for the underlying workspace evidence or a qualified accountant’s review.

## Close calendar operating rhythm

| When | Owner | Required operation | Escalation condition |
| --- | --- | --- | --- |
| Daily from T-5 | Finance manager | Review task owners/due dates, reconciliation exceptions, report freshness, compliance obligations, provider/manual-submission boundary, and worker backlog. | Any unowned task, due-soon statutory ambiguity, or queue age over 15 minutes. |
| T-2 | Finance preparer | Generate current reports, attach task evidence, resolve reconciliation exceptions, prepare compliance evidence, and request the package. | Missing/corrupt source, inaccessible document, or package retry. |
| T-1 | Independent reviewer | Review authoritative readiness hash, report/source links, access grants, package manifest, and exceptions; sign the exact evidence. | Preparer/reviewer collision, stale hash, or anomalous grant. |
| T | Accounting administrator | Re-evaluate, lock only the approved hash, verify package, record filing/manual evidence, and start year-end when applicable. | Any release-stop signal or missing external lane. |
| T+1 | Finance owner | Confirm worker completion, provider/manual acknowledgement, retained proof, access expiry, and next-period opening balances. | Failed rollover, missing acknowledgement, or checksum drift. |

## Release-readiness checks and alerts

Alert with company and period in structured logs, not metric tags. Page the finance on-call for a failed rollover, corrupt final package, cross-company/access anomaly, statutory deadline breach, or evidence-hash mismatch. Create an operational ticket for overdue/blocked tasks, unresolved reconciliations, stale reports, missing sign-offs/evidence, incomplete packages, compliance ambiguity, or a worker queue older than 30 minutes.

The readiness meter is `VirtualCompany.Finance.CloseComplianceReleaseReadiness` with evaluation count/duration and release-stop count. Existing accounting objectives remain authoritative: close validation p95 3 seconds (breach 6 seconds), statements p95 2 seconds (breach 4 seconds), export acceptance 500 ms (breach 1 second), and worker age 15 minutes (breach 30 minutes). The small accounting profile is the supported launch envelope. Medium remains unqualified until its retained trial-balance breach is fixed and remeasured.

## Access review

1. Export company grants and open engagements for the selected period.
2. Confirm every active grant is independently approved, effective now, company-bound, scope-limited, and has an expiry appropriate to the engagement.
3. Confirm the preparer and assigned accountant are different users for every open engagement.
4. Revoke unused, expired, future-effective, orphaned, or wrong-company grants. Do not repair isolation by copying records between companies.
5. Re-run readiness and retain the access reviewer, time, decision, and evidence file checksum.

Cross-company inputs are counted only as an anomaly; names or record details are not disclosed by the readiness signal. A missing membership returns forbidden, preserving non-existence.

## Incident, recovery, and forward-fix

### Worker interruption or uncertain outcome

Stop new release actions for the company/period. Inspect durable operation receipts, leases, attempts, package state, source IDs, and journals before retrying. Allow an expired lease to be reclaimed with the same logical source/idempotency identity. Never create a replacement journal or package under a new identity while the first outcome is uncertain.

### Missing or corrupt object

Disable download, preserve SQL rows and audit history, restore the exact deterministic object key from the matching snapshot, and run server-side package verification. The restored bytes must match package, manifest, and item SHA-256 values. If not, retain the incident and generate a new versioned package; never overwrite a final object with different bytes.

### Coordinated SQL/object restore

1. Record the pre-incident readiness evidence hash and all source links.
2. Restore a database backup and its matching object-store snapshot into an isolated environment.
3. Apply forward migrations; do not run destructive migration rollback over retained accounting evidence.
4. Run fresh/upgrade discovery, SQL integrity/concurrency tests, package verification, and the deterministic year-end scenario.
5. Query readiness for the same company/period. The recovery record must contain the original hash and every original source link.
6. If evidence differs, remain no-go. Investigate snapshot mismatch or perform a governed forward correction that produces a new hash and independent approval.

### Subsequent event or accounting correction

Treat later evidence as a new governed fact. Disclose only, post an immutable correction in an open period, or use the approved reopen workflow. Refresh reports/readiness, regenerate the package, obtain independent sign-off, then re-lock or complete rollover. Do not edit posted journals, final snapshots, sign-offs, or package manifests.

## Evidence records and go/no-go

Run `scripts/test-matrix.ps1 -Lane close-compliance-proof`. Then create five JSON records for lanes `close-compliance-browser`, `close-compliance-recovery`, `close-compliance-capacity`, `close-compliance-provider-scope`, and `close-compliance-professional-review`. Each record requires `lane`, `outcome` (`passed` or `approved`), `companyId`, `fiscalPeriodId`, `reviewer`, and `generatedUtc`. Recovery additionally requires `evidenceHash` and `sourceLinks`; provider scope requires `capability: export_and_manual_evidence_only`.

Run `scripts/verify-close-compliance-release.ps1` with the matrix and those records. The script uses `VC_CLOSE_COMPLIANCE_OPERATOR_TOKEN`, validates the live admin-scoped readiness response, checks automated results, verifies restored hash/source links, hashes every evidence file, and writes the combined decision. Do not set `go` manually.

Go requires all of the following:

- no backend release-stop signal;
- hermetic, tenant/isolation, SQL fresh/upgrade/concurrency/rollback, and supported-volume proof passed;
- coordinated recovery preserved original hash and source links;
- authenticated English/Swedish finance and accountant UAT passed at desktop and narrow widths, including accessibility and Stockholm date boundaries;
- provider-scope owner approved the export/manual-evidence-only boundary; and
- a qualified Swedish accountant approved the frozen statutory and exception scope.

Any critical/high correctness, isolation, recovery, or statutory gap is a release stop. Missing evidence is a release stop. An engineering test result cannot replace provider, browser, recovery, or professional proof.
