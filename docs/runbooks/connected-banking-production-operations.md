# Connected-banking production operations

## Ownership and release boundary

Finance operations owns connection/feed/reconciliation health and ambiguous payments. Platform operations owns deployment, workers, database/object recovery, secret delivery, and telemetry. Security owns provider applications, certificates, callback/webhook trust, and compromise response. The release manager owns the matrix manifest and signed go/no-go record.

Do not enable a provider or payment initiation until the release record contains green hermetic, SQL Server, Docker restore, selected-profile performance, authenticated English/Swedish browser, and approved real-provider sandbox evidence. Repository contract tests are not provider proof.

## Deployment and feature controls

1. Take a coordinated SQL backup and object-storage manifest. Record both hashes and a connected-banking recovery checksum.
2. Apply the complete migration chain to an empty disposable database and a representative restored database. Run the no-pending-model check.
3. Deploy API/Web/workers with `EnableBanking:Enabled=false` and `EnableBanking:PaymentInitiationEnabled=false`.
4. Verify health, authorization, company isolation, readiness, and recovery checksum.
5. Enable account information for a small operator-owned cohort. Observe consent expiry, checkpoint lag/gaps, worker age, duplicate identities, unreconciled/suspense items, and control-account comparison.
6. Complete real-provider ingestion evidence, then expand the feed cohort.
7. Enable payment initiation only after successful and rejected sandbox submissions, signed webhook/poll acknowledgement evidence, ambiguity/replay recovery, and finance/security approval.

Disable new provider work first during an incident. Feature controls do not delete connections, rows, instructions, attempts, acknowledgements, payments, journals, reconciliation, audit, or object hashes.

## Credential and certificate rotation

Provider application IDs, access credentials, RSA private keys, webhook material, and bearer tokens belong in the deployment secret store. They must not appear in source, configuration files, database business rows, logs, screenshots, support tickets, or test artifacts.

For planned rotation, register the replacement certificate/provider key, deploy it to the secret mount, restart a canary, and prove institution discovery plus a non-production signed request before retiring the old key. Retain old ASP.NET Data Protection keys as decrypt-only until active consent/cursor envelopes have renewed. For suspected compromise, disable the provider and payment initiation, revoke affected consents, rotate provider and protection keys, invalidate callback sessions, require fresh consent, and open a security incident.

## Incident triage

| Signal | Immediate action | Safe recovery |
| --- | --- | --- |
| Expired consent/scope loss/ownership mismatch | Stop sync and payment use for that connection. | Renew consent and re-verify ownership/mapping. |
| Feed lag or provider outage | Stop manual retry loops; retain the checkpoint. | Honor provider backoff and resume bounded work. |
| Process death/expired lease | Do not edit cursor or rows. | Restart workers; allow lease takeover from the last atomic checkpoint. |
| Cursor regression/missing range | Mark coverage incomplete; stop repeated sync. | Recover the exact retained gap after provider investigation. |
| Stable identity with changed payload | Preserve both evidence hashes; do not overwrite the booked row. | Escalate and reconcile explicitly. |
| Provider timeout/connection loss on payment write | Freeze as `reconciliation_required`. | Search provider/bank evidence and attach the exact provider ID; never blind-retry. |
| Provider success/local failure | Stop resubmission immediately. | Recover by safe status read and retained request/provider evidence. |
| Webhook replay/conflicting payload | Reject conflict and retain hash/identity evidence. | Validate signature chain, provider event, and execution state. |
| Rejected instruction | Keep rejection visible. | Correct the source and create a new approved version. |
| Unsettled batch/control difference | Do not claim settlement or close the control. | Match an exact booked row, post through governed accounting, and reconcile the difference. |

Detailed procedures remain in [bank connectivity](bank-connectivity.md), [bank-feed synchronization](bank-feed-synchronization.md), [statement import](statement-import-center.md), [advanced reconciliation](advanced-reconciliation.md), [native payment batches](native-payment-batches.md), [payment execution](payment-execution.md), and the [daily treasury workspace](daily-treasury-workspace.md).

## Recovery and disaster rehearsal

1. Quiesce provider writes or record the exact cut time.
2. Capture SQL backup with checksum and a versioned object manifest/archive.
3. Run `verify-connected-banking-recovery.ps1 -RequireReady -VerifyObjectContent` and retain the JSON/checksum.
4. Restore SQL and the matching object snapshot into an isolated environment.
5. Apply forward migrations, run database integrity checks, and rerun the verifier with `-ExpectedChecksum`.
6. Exercise one interrupted feed lease and one interrupted payment submission. Confirm one transaction per stable identity, no duplicate provider write, and explicit reconciliation.
7. Compare readiness and accounting control accounts. A not-measured or blocking check fails the rehearsal.

Rollback is operational, not destructive: disable the affected worker/provider feature and forward-fix. Never drop new tables, edit cursors, remove ambiguous attempts, erase imported rows, renumber instructions, detach acknowledgements/payments/journals, or delete reconciliation/audit/object-hash evidence. Restoring a pre-deployment snapshot requires an explicit finance decision for every post-backup provider write and accounting record.

## Monitoring and evidence

Alert on consent expiry, feed gaps/lag, duplicate identity, aged unreconciled/suspense rows, stale approvals, ambiguous/rejected/unsettled executions, worker age, webhook conflict, control difference, and capacity breach. Correlate safe company/connection/execution/correlation IDs without logging tokens, private keys, authorization codes, callback state, continuation tokens, raw provider payloads, full account numbers, or webhook bodies.

Each release record must retain the matrix manifest/TRX paths, SQL/Docker migration and restore identities, capacity profile/timings, pre/post recovery checksums, browser screenshots and locale, provider environment/request identities/status facts, security review, issue ledger, approver names, decision, and timestamp.

