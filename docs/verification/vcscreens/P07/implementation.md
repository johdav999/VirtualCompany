# P07 implementation

P07 connects existing accounting evidence and close actions while preserving separate income-statement movement and balance-sheet period-end semantics. All P01–P06 working-tree changes remain required in this checkout.

## Production changes

- `AccountingJourneyRoutes` carries company, fiscal period, operational/provider source, exact Overview origin and validated accounting return context. One source origin with a bounded close parent prevents recursive return URLs. Statement-to-journal and statement-to-general-ledger links restore the same selected statement. Work, documents and accountant portfolio retain their canonical routes; Work receives the existing Finance return parameter.
- Shared AccountingNavigation accepts the selected period. Accounting report/close breadcrumbs and links reuse that context. Finance Today already supplies canonical close, income, balance and reconciliation entries from P06; its accounting destinations now have durable inner navigation.
- Retained close/reports/accounts/periods/journals execute their existing controls using InteractiveServer. Close period selection persists in the URL; source timestamps use the shared formatter. Close task evidence links are actionable. Pending tasks contribute to the not-started count. Successful readiness checks are excluded from attention warnings, without altering readiness/waiver/approval/lock policy.
- Close and report reads guard late company/route responses and clear unavailable or departing-company evidence. Invalid requested fiscal periods fail explicitly instead of silently displaying another period. Journal links use the actual `journalId` parameter, and journal filtering uses the requested accounting period. A missing period cannot broaden the journal read.
- Statement selection persists period, comparison, current/comparison snapshot IDs and selected account. Retained comparison evidence pins its exact snapshot. Reload restores account drill-down. URL persistence skips identical URLs, including prerender, preventing self-redirect loops.
- Statement export rereads current authorized scope before the existing CSV Blob handoff. Closed reports use their selected retained snapshot; denied reads never download cached rows. The owning CSV, layout, printing module and calculations remain unchanged.

## Ownership and preservation

The existing FinancialStatementWorkspaceService, statement mappings, ledger/report owners, accounting-close services, reporting lock/reopen history, audit, approval and provider boundaries remain authoritative. There is no dashboard-derived ledger calculation, new permission role, schema, migration or provenance store. Opening a source is a read. Completing one task does not imply a ready/locked period or statutory approval. Read-only external accountant access still requires an effective independently approved company grant.

The fixture adds only isolated P07 records and initialization; P01–P06 fixture rows and evidence files are retained. Statement and close references retain their separate hierarchy. The country-neutral bank mapping appears under Other current assets and carries a report-review warning; it does not claim a Swedish BAS classification or signed statutory opinion.

## Verification

Final focused Web checks: 63 passed; Finance: 24 passed; authenticated API: 52 passed, plus 18 close API checks repeated after the readiness projection change. No failures or skips in these selected suites. API/Web/UAT builds pass. Existing nullable/analyzer and unavailable vulnerability-feed warnings in earlier/full build logs are recorded; no new schema or contract shape changed.

See `verification.json`, `uat.md`, `browser-checks.json`, `issue-ledger.md`, TRX/build logs and screenshots for the specific evidence and independent gates. `implementation-status.md` records the concrete P08 handoff; `screen-report-register.md` distinguishes retained utilities from browser-certified statement/close journeys.
