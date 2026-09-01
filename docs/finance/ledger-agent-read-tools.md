# Ledger and financial-report agent reads

Prompt 2 introduces nine versioned, read-only Finance tools under the `finance.ledger.*` namespace. They expose chart/account lookup, fiscal-period state, journal search, general-ledger detail, trial balance, supported statements and registers, report definitions and versions, immutable report snapshots, and report-line source drill-down.

The tools are adapters over the existing accounting administration, immutable journal, accounting reporting, financial statement, report suite, and report-definition services. They do not calculate balances, refresh reports, create exports, post journals, repair mappings, or mutate report definitions.

## Contract and authority

- Contract version: `finance-ledger-agent-read-v1`.
- Registry version: `1.0.0` per tool.
- Action class: `read` only.
- Required role permission: `FinanceView` through the Prompt 0 authority projection.
- Catalogue owner: `ledger_and_financial_reporting` in the Prompt 1 Finance coverage catalogue.
- Account and report-definition lookups are capped at 100 items. Journal pages are capped at 100 items. Ledger, statement, and drill-down pages are capped at 200 items. Applied `skip`, `take`, and `totalCount` values are returned in response metadata for every paged path. No agent export tool is registered.

Every successful result carries authoritative DTO data plus contract version, generation time, freshness semantics, truncation state, source identifiers, and currently allowed follow-up actions. Source metadata reports both the complete distinct-source count and whether the returned identifier list was capped at 2,000 entries. Native report DTOs preserve period and currency identity, dimensions and as-of parameters, report mapping/calculation versions, authoritative control totals, checksums, snapshot identity, and source provenance. Snapshot-backed native and suite reports are explicitly labelled `immutable_snapshot` rather than live.

## Failure semantics

The adapter returns actionable non-success states for uninitialized accounting, non-read actions, invalid or unbounded requests, unsupported report variants, ambiguous account/period/report references, stale snapshot checksums or definition versions, and missing or cross-company source identifiers. Profit-and-loss and balance-sheet reads reject suite-only parameters such as as-of, comparison, rolling, dimension, or definition-version selectors instead of silently ignoring them. Snapshot reads never regenerate the report: repeated reads return the persisted definition version and checksum.

## Observability

`VirtualCompany.Finance.LedgerAgentReads` records request outcomes, rejection reason codes, tool names, and duration. These metrics distinguish manifest/routing failures, validation and ambiguity states, source lookup failures, and successful authoritative reads without recording ledger payloads.
