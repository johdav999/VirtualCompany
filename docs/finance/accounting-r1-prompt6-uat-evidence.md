# Accounting R1 Prompt 6 UAT evidence

## Session profile

- Date: 2026-08-25
- Product type: authenticated Blazor web application
- Revision baseline: `fd4d268` plus the Prompt 6 working-tree implementation
- Environment: local Release API and Web builds against the existing SQL Server development database
- Browser surface: Codex in-app browser
- Role exercised: company owner for company `VC`
- Desktop viewport: 1280 px browser viewport; captured page width 1265 px
- Narrow viewport: 390 × 844 px; captured page width 375 px

## Reference baseline

The screenshot-first comparison used:

- [`swedish-accounting-setup-reference.png`](../design/references/swedish-accounting-setup-reference.png)
- [`swedish-vat-statutory-reporting-reference.png`](../design/references/swedish-vat-statutory-reporting-reference.png)
- [`swedish-accounting-reference-prompts.md`](../design/references/swedish-accounting-reference-prompts.md)

The implementation follows the references' restrained accounting-workspace hierarchy: explicit readiness states, compact period controls, plain-language action cards, evidence/source drill-down, and a visible but bounded Laura advisory surface. It intentionally uses the repository's existing Finance shell and token system instead of copying the references literally.

## Flow results

| Flow | Evidence | Result |
| --- | --- | --- |
| Authenticated owner opens Reports & close with `view=vat` | Live API/Web, desktop screenshot | Pass. The VAT KPI, statutory workspace, period selector, four workspace views, explicit empty state, and safe next action render from the current builds. |
| Desktop visual hierarchy and reference comparison | [`prompt6-vat-desktop.png`](uat-evidence/prompt6-vat-desktop.png) | Pass after stylesheet-cache fix. The active VAT view, statutory heading, period control, tabs, and empty-state action are legible without false compliance claims. |
| Narrow responsive layout | [`prompt6-vat-narrow.png`](uat-evidence/prompt6-vat-narrow.png) | Pass after grid fix. `documentElement.scrollWidth` was 375 px at a 390 px viewport; chip rows retain intentional local horizontal scrolling. |
| Swedish setup readiness states | bUnit `Readiness_keeps_format_attestation_and_independent_review_distinct` plus source contract tests | Pass by component substitute. The available live company is already configured with the country-neutral policy, so the Swedish incomplete-setup fixture was not mutated into the development database. |
| VAT role/action, stale, blocked, correction, and export transitions | Five Prompt 6 bUnit component tests | Pass by component substitute. Server `AllowedActions` and accounting role gates control mutations; viewer-safe read visibility remains. |
| Swedish localization | Swedish bUnit render plus localization quality gate | Pass. English and Swedish resource files each contain 2,149 unique, parity-matched keys. |

## Issue ledger

| ID | Severity | Finding | Resolution | Regression guard |
| --- | --- | --- | --- | --- |
| P6-UAT-001 | P1 | The first live render reused the prior isolated-CSS asset URL and showed the new VAT view tabs as unstyled browser buttons. | Bumped the isolated stylesheet revision to `20260825-swedish-statutory-reporting`. | `AccountingReportsSurfaceTests` asserts the new asset revision. |
| P6-UAT-002 | P1 | At 390 px, the implicit Finance content grid expanded to 649 px from child min-content sizing. | Constrained the content grid to `minmax(0, 1fr)` and its direct children to `min-width: 0`; also revised the application stylesheet URL. | Source contract assertions plus the final measured 375 px document width. |
| P6-UAT-003 | Constraint | The in-app browser loaded authenticated prerendered pages and screenshots, but its Blazor negotiation request reported `Failed to fetch`, so live tab-transition clicks could not be claimed. | No product-code workaround was added for a browser-control transport constraint. Interactive behavior is covered by bUnit click tests and API integration tests. | Re-run live interaction UAT when the browser transport accepts the local Blazor negotiation channel. |

## Verification record

- API Release build: pass.
- Web Release build: pass.
- Full Web test project: 386 passed, 0 failed.
- Focused statutory API integration tests: 4 passed, 0 failed.
- Prompt 6 bUnit component tests: 5 passed, 0 failed.
- Localization resource parity and duplicate check: pass.
- `git diff --check`: pass; only existing line-ending conversion warnings were reported.

## Residual constraints

- The local company fixture had no VAT filing period, so the live capture correctly shows the recoverable empty state. Calculated, stale, approved, finalized, correction, package, retry, and expiry variants are verified through deterministic component and API tests.
- The live company uses a country-neutral configured policy, so the six-step Swedish setup flow was not exercised by mutating persistent development data. Its rendered statutory states and role behavior are covered by the Prompt 6 test suite.
