# Native customer invoice drafts

Release 2 Prompt 2 adds a company-scoped, versioned draft boundary for customer invoices. Drafts retain normalized lines, evidence document hashes, the selected accounting policy pack and definition hash, rounding configuration, per-line tax decisions, VAT boxes, totals, warnings, blockers, and an exact result hash.

## Lifecycle and authority

- Create, update, copy, preview, list, read, discard, readiness, and submit-for-approval operations are available under `internal/companies/{companyId}/finance/accounting/customer-invoice-drafts`.
- Create, update, copy, discard, and submit require Accounting Admin. Reads, preview, and readiness require Accounting View.
- Every mutation uses a company-scoped stable idempotency key. Reusing a key with different content returns a conflict.
- Updates use the expected draft version. An edit retains the prior approval evidence as stale, cancels a pending approval, increments the version, recalculates tax, and blocks issue readiness until the new version is approved.
- Evidence links reference existing company documents and retain their SHA-256 content hashes. No document binary is copied.
- Calculation and readiness are backend policy decisions. Swedish domestic VAT uses the selected policy pack and the retained company/customer statutory facts. Unsupported currency, tax, customer-credit, conflict, or statutory cases remain explicit blockers.

## Deliberate boundary

This capability does not create a `FinanceInvoice`, allocate a statutory number, post a journal, render a document, enqueue delivery, or call an external provider. Those actions remain unavailable until the separate issue and delivery boundaries are implemented.

## Operational checks

Investigate repeated `customer_invoice_draft_*` problem codes by comparing the returned current version, readiness blockers, result hash, policy pack identity, customer billing profile, and approval request. Do not bypass stale approval or tax blockers by editing persisted state. Correct the source configuration or draft and submit the resulting version for approval again.
