# Complete financial report suite

## Scope and authority

The report suite reads posted immutable journal lines and the existing AR, AP, fixed-asset,
currency, tax, and accounting-dimension projections. It never posts, repairs, or reclassifies
accounting data. The suite is engineering reporting functionality; jurisdiction-specific output
must not be described as statutory-compliant until a qualified reviewer has approved the exact
layout and policy-pack version.

Supported report keys are `cash_flow`, `equity_changes`, `aged_receivables`, `aged_payables`,
`journal_register`, `fixed_asset_register`, `tax_detail`, `currency`, and `dimension`.
Cash flow supports governed `direct` and `indirect` calculation modes. Queries can include one
comparative fiscal period and 1–24 rolling periods.

## Reproducibility

Every response retains:

- calculation, mapping, and parameter versions;
- a deterministic SHA-256 checksum;
- report blockers and control totals;
- journal-line, source, document, subledger, dimension, and exchange-rate provenance;
- the supported-volume performance budget and measured request duration.

Financial-statement mapping updates create a new effective-dated version and retire the prior
row. They do not rewrite historical classifications. Missing or conflicting cash-flow/equity
mappings are explicit blockers.

A snapshot can be captured only after the period is closed and reporting-locked and only when
the report has no blockers. Snapshot capture is company-scoped and idempotent. Closed-period
reads return the matching stored snapshot when one exists. Use the drill-down endpoint with the
snapshot id to retain the exact source population.

## API and exports

- `GET /internal/companies/{companyId}/finance/accounting/report-suite/{reportKind}`
- `POST /internal/companies/{companyId}/finance/accounting/report-suite/snapshots`
- `GET /internal/companies/{companyId}/finance/accounting/report-suite/snapshots/{snapshotId}`
- `GET /internal/companies/{companyId}/finance/accounting/report-suite/{reportKind}/lines/{lineKey}/drilldown`

Typed Web clients are registered through the existing `FinanceApiClient`. Large downloads use
the existing durable accounting export worker with export type `financial_report_suite_json`.
The worker exports approved report snapshots only, verifies its checksum on download, and fails
permanently with `financial_report_suite_snapshot_missing` when no approved snapshot exists.

## Operations and performance

Small reports (up to 25,000 source journal lines) have a 2-second generation budget. Medium
supported volumes have an 8-second budget. Operators should investigate repeated budget misses,
unmapped-account blockers, failed export jobs, or checksum mismatches before close approval.
The `VirtualCompany.Finance.FinancialReports` meter records generation counts, duration, output
line counts, blockers, snapshot captures, replays, and budget compliance. Snapshot creation also
writes a business audit event containing the period, report kind, retained versions, hashes, and
checksum.

For recovery verification, validate that snapshot database rows, report JSON, checksums, mapping
versions, and any exported object-storage artifact are restored together. A checksum mismatch is
an integrity failure; do not regenerate over the stored evidence.
