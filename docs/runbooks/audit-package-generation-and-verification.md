# Audit package generation and verification operations

## Supported boundary

Audit packages are immutable, period-scoped accounting evidence archives for a closed fiscal period. The implementation snapshots scope and definition identities, collects only evidence the requesting company can access, and produces a human-readable `index.html` plus a machine-readable `manifest.json`. It does not broaden document access, copy credentials or provider secrets, or convert a missing required item into a final package.

The current scope is `period_close` version `audit-package-v1`. Requesting the same company, period, scope, version, and source snapshot returns the same logical package. A source change produces a different scope hash and therefore a new logical package.

## Request and approval

1. Confirm the fiscal period is closed and reporting locked.
2. Request `POST /internal/companies/{companyId}/finance/accounting/audit-packages` with a unique idempotency key, fiscal-period id, scope key, and scope version. Accounting administration permission is required.
3. A different authorized finance approver must approve the request at `POST .../{packageId}/approve`. The requester cannot approve their own request.
4. The background worker claims approved packages in bounded batches. The default lease is 300 seconds; an expired generation lease is recoverable by a later worker pass.
5. Cancellation is accepted before finalization at `POST .../{packageId}/cancel`. The worker checks cancellation immediately before and after object storage. If cancellation wins after a write, the just-written object is deleted.

The default retry budget is four attempts with exponential delay from 15 seconds. Failures retain safe summaries and attempt history. Do not manually change status rows to bypass approval, retries, or finalization.

## Evidence and finality

The collector includes the period trial balance, general ledger, financial statements, tax and VAT-return evidence, reconciliations, significant journals, approval/signoff evidence, close history, provider exception summaries, active accounting policy-pack identity, and accessible company documents. Collection is bounded by configured ledger pages, document count, document size, and total package size.

Every included item has a deterministic path, sequence, source reference, source version or definition version where available, content length, and SHA-256. The manifest has its own checksum and the ZIP has a package checksum. ZIP entry ordering and timestamps are deterministic.

If a required item is missing, inaccessible, corrupt, or exceeds a bound, the manifest records the finding and the package is `incomplete`; it is never marked `final`. Correct the source evidence and request a package against the new source snapshot. Do not edit an existing archive or its manifest.

## Download and verification

Listing and detail reads require accounting-view permission:

- `GET /internal/companies/{companyId}/finance/accounting/audit-packages`
- `GET /internal/companies/{companyId}/finance/accounting/audit-packages/{packageId}`

Before download, create a ten-minute one-time authorization with `POST .../{packageId}/download-authorizations`. Only the token hash is retained. Supply the returned token once to `GET .../{packageId}/download?token=...`. The response is private/no-store and carries `X-Content-SHA256` and `X-Manifest-SHA256` headers.

Run server-side verification with `POST .../{packageId}/verify`. Verification reads the stored object and checks:

- the stored object against the recorded package SHA-256;
- `manifest.json` against its recorded SHA-256;
- every manifest item against the corresponding ZIP entry and per-item SHA-256;
- manifest metadata against persisted artifact records; and
- missing and corrupt item counts.

Verification results are retained with actor, time, result code, safe summary, and observed hashes. A failed verification blocks trust in the archive; do not re-label it final or distribute it as verified evidence.

## Object recovery and incidents

Storage keys are deterministic: `audit-packages/{companyId}/{fiscalPeriodId}/{scopeHash}.zip`. If object storage is temporarily unavailable, leave the package in its bounded retry lifecycle. If a worker stops during generation, wait for the lease to expire and allow another worker pass to reclaim it.

For a missing or corrupt object:

1. Stop downloads for the affected package.
2. Preserve the database package, manifest, attempts, approval, audit, and verification records.
3. Restore the exact storage object for the deterministic key from an approved backup.
4. Run server-side verification and compare both package and manifest SHA-256 values with the persisted records.
5. Release the restored object only when verification is valid. Otherwise retain the failure evidence and generate a new package from an explicitly versioned source snapshot.

Do not overwrite a final archive with different bytes under the same scope hash.

## Retention and monitoring

The default retention period is seven years. Expiry removes the storage object and marks the package expired; the accounting metadata, approval, attempt, audit, and verification trail remains available according to database retention and legal-hold policy. Review retention settings before production rollout and suspend automated expiry when a legal hold applies.

Monitor meter `VirtualCompany.Finance.AuditPackages`:

- `audit_packages.requests`
- `audit_packages.generations` by outcome
- `audit_packages.generation.duration_ms` by outcome
- `audit_packages.verifications` by validity

Audit actions include `audit_package_requested`, `audit_package_approved`, `audit_package_cancelled`, `audit_package_generated`, `audit_package_downloaded`, and `audit_package_verified`.

## Configuration and rollout

Configuration is under `AuditPackages` in API settings. Validate poll interval, claim batch, retry budget, lease, retention, token lifetime, evidence bounds, and package-size bound for the deployment. Apply migration `20260830220000_AddImmutableAuditPackages` after the Prompt 5 migrations and verify that EF reports no pending model changes.

This runbook is engineering guidance, not an accountant’s opinion or statutory approval.
