# Accounting Release 0 UAT evidence

This ledger records the Prompt 6 UAT work completed on 2026-08-24. It separates verified application behavior from browser-only checks that could not be executed in this workstation session.

## Environment and evidence level

- Release build with authenticated development-header identity and an isolated migrated SQL Server database.
- Production-shaped persisted company data; company `54d927ab-1f35-4899-9d9d-4132eff0a33a` existed only for the run.
- English (`en-US`) and Swedish (`sv-SE`) routes rendered through the live authenticated Web/API hosts. Both recorded host PIDs were stopped, and the exact `virtualcompany_prompt56_uat` database was dropped after verification.
- Component, Web contract, API authorization, tenant-isolation, localization, and full hermetic test evidence is retained under `artifacts/test-matrix/20260824-prompt56-hermetic-final/`. SQL Server migration and accounting-integrity evidence is under `artifacts/test-matrix/20260824-prompt56-sqlserver-final-5/`.
- Existing design references in `docs/design/references/` were used. No page required a new major visual language or a new reference asset.

The in-app browser could not start because its JavaScript kernel assets were unavailable (`failed to write kernel assets: The system cannot find the path specified`). Authenticated live HTTP rendering was therefore the strongest safe substitute. It verifies routing, authentication, localization, server rendering, API integration, and absence of route 500s, but it does **not** verify screenshots, browser console/network panels, keyboard traversal, screen-reader output, 200% zoom, or phone/tablet reflow. Those browser-only checks remain a release evidence gate.

## Route and locale matrix

Every route below returned HTTP 200 for both locales through the live authenticated host.

| Surface | Routes | States exercised in live rendering or retained regressions |
| --- | --- | --- |
| Accounting setup and administration | `/finance/accounting/setup`, `/finance/accounting/accounts`, `/finance/accounting/periods` | Not configured, empty, populated, protected account, close/lock guidance, read-only action gating |
| Journals and manual entry | `/finance/accounting/journals`, `/finance/accounting/journals/new` | Empty/populated, loading/error regions, approval/post/correction action semantics, evidence/source links |
| Reports and reconciliation | `/finance/accounting/reports`, `/finance/accounting/reconciliation` | Empty before setup, populated/drill-down, loading, selected row, failure/remediation, close/export paths |
| Authority, migration, and operations | `/finance/accounting/connections`, `/finance/finance-providers`, `/finance/finance-work` | Internal/provider authority, view-only, blocked/failed/stale/recovery actions, worker status announcements |
| Connected Finance overview | `/finance`, `/finance/cash-position`, `/finance/monthly-summary`, `/finance/settings` | Empty/populated summaries, company context, settings navigation |
| Receivables, payables, and payments | `/finance/invoices`, `/finance/reviews`, `/finance/supplier-bills`, `/finance/supplier-bills/review`, `/finance/supplier-subscriptions`, `/finance/payments` | Review/approval states, source accounting links, empty/populated, allowed actions |
| Bank and operating records | `/finance/transactions`, `/finance/issues`, `/finance/counterparties`, `/finance/mailbox`, `/finance/balances` | Empty/populated, attention/failure states, direct remediation/navigation |
| No-company operator route | `/system/admin/finance-work` | No selected company; renders a company-selection/forbidden state instead of throwing |

The Swedish supplier-subscription surface was checked explicitly and rendered `Leverantörsabonnemang`. The localization quality gate requires exact English/Swedish resource-key parity and rejects client-owned visible English on the accounting UAT pages.

## Role and authorization matrix

| Role/context | Expected UI capability | Server evidence |
| --- | --- | --- |
| Owner/admin/manager | View Finance; owner/admin and applicable managers receive mutation actions | `FinanceAccessResolverTests`, accounting administration/configuration/operations API integration tests |
| Finance approver/view-only tester | View reports and approval-relevant data; mutation controls remain role-specific | `FinanceAccessResolverTests`, approval and accounting integrity scenarios |
| Employee | Finance navigation hidden/forbidden; direct requests denied | `FinanceAccessResolverTests` and accounting configuration, administration, ledger, capacity, operations, and provider-switch API tests |
| Cross-company or inactive/pending membership | No company data or actions disclosed | API tenant-isolation and authorization integration tests |
| No company / multiple eligible companies | Plain selection state with available companies; no invalid component parameters | `AccountingR0UatSurfaceTests.Accounting_empty_and_access_states_do_not_require_unavailable_prerequisites` |

Disabled or hidden controls are not treated as authorization. API tests remain the authority for every sensitive action.

## Accessibility and responsive contract evidence

The accounting surfaces now use localized semantic headings, labelled controls, `role="alert"` validation/failure regions, `aria-live` status updates, `aria-busy` asynchronous regions, `aria-selected` selected rows, `aria-pressed` toggles, and keyboard-operable drill-down rows. Existing page CSS retains the repository media-query patterns. The following regressions enforce those contracts:

- `AccountingR0UatSurfaceTests`
- `AccountingAdministrationSurfaceTests`
- `ManualJournalSurfaceTests`
- `AccountingAuthoritySurfaceTests`
- `CustomerInvoiceAccountingSurfaceTests`
- Web localization and contract quality gates

These tests provide semantic and responsive source-contract coverage. Actual keyboard order/focus visibility, dialog escape/return focus, assistive-technology announcements, contrast, zoom, and reflow are not marked verified until the browser gate runs.

## Findings and disposition

| Priority | Finding | Fix | Verification |
| --- | --- | --- | --- |
| High | Reconciliation returned 500 before accounting setup because it requested fiscal years before handling an empty workspace. | Render the empty workspace first and safely handle unavailable prerequisites with localized remediation. | Both locale routes return 200; regression orders the empty-state guard before the fiscal-year request. |
| High | Finance worker operations passed a nonexistent `State` parameter to the company-selection component, causing route rendering failure. | Pass the localized message/company list explicitly and add the forbidden branch. | No-company route returns 200; regression rejects the invalid parameter form. |
| Medium | Supplier subscriptions leaked English in Swedish, including dynamic statuses and action text. | Localize markup and code-behind messages/statuses, use locale-aware number/date formatting, and add async/toggle semantics. | Swedish live title verified; English/Swedish key-parity and visible-English gates pass. |
| Evidence gate | Browser visual, console/network, manual accessibility, zoom, and narrow-layout capture unavailable because the browser kernel failed before execution. | No product workaround applied. Re-run on a workstation with the in-app browser assets available. | Open; this is an evidence gap, not a known product defect. |

No known critical/high product defect remains in the exercised routes. Release sign-off remains conditional on the browser-only evidence gate above and the separate Prompt 5 medium-capacity result.
