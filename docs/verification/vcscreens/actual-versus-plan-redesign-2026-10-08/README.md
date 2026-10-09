# Actual versus plan — business UI review

Verified 2026-10-08 against the local application, with the existing uncommitted work preserved. Base revision: `9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b`.

## Product profile

- Product: Virtual Company, Blazor web SaaS.
- Surface: `/finance/reports/variance`, entered from Cash forecast with company, dashboard, and Finance return context.
- Environment: local Web `http://localhost:5062`, API `http://localhost:5301`. Existing development Alice Admin context; company VC `43e6a825-d1b7-429a-8608-7e668087d005`.
- Build: focused Web tests below compile Web, API and dependencies. Run: `dotnet <snapshot>/VirtualCompany.Web.dll` in `src/VirtualCompany.Web`. Snapshot and owned PID are recorded in [web-host.json](web-host.json). API PID 29816 was retained. Final Web PID 19324 includes the follow-up card polish and remains running.
- Adapter: headless Edge / Playwright with the real local company, desktop 1857×837, intermediate widths 1366 and 1024, phone 390×844. Interactive browser automation was unavailable because of the existing sandbox launcher ACL failure. Native browser screenshots are the safe substitute.
- Flows: initial report and absent baseline; coverage keyboard disclosure; CSV saving; filters and reload; bounded-query recovery; recorded versions and sources; parent return; posted-journal navigation; responsive layout and table keyboard scrolling.
- Verification reads existing business data. No planning revisions, journals, variance explanations, agent requests or provider operations were created.

## Changes and reference

Created the required reference with built-in OpenAI image generation before implementation: [reference](../../../design/references/finance-actual-versus-plan-reference.png), [written generation brief](../../../design/references/finance-actual-versus-plan-reference-prompt.md). The native Razor/CSS implementation follows its light cards, version summary, grouped filters, clear empty state, wide table, and compact context rail. The existing application shell is preserved; the reference image is not a runtime asset.

The page now distinguishes applied report context from pending filter edits, shows an explicit missing budget baseline, moves export and refresh into the header, and provides useful source-coverage and next-step panels. Populated measures retain their signs and separate currencies. Missing amounts remain unavailable; a recorded plan value of zero remains zero. Table headers are sticky, amounts align right, and the table scrolls within its card on small screens. Source inspection and retained monthly reviews keep their existing behavior.

Full-year selection in this company exceeded the backend's existing 2,000-record bound. Recoverable errors now retain the current company's previously authorized filter choices while withholding report rows and export. Those choices clear on company changes and access loss. First-load errors initialize controls from the query so users can correct the range. Financial rules and API contracts are unchanged.

## Issue ledger

| ID | Severity | Flow / owner | Finding and acceptance | Evidence / regression | Status |
|---|---|---|---|---|---|
| AVP-01 | P2 | Initial report / FinanceVariance | Flat filters and weak hierarchy. Given the report, show clear version and period context, grouped controls, discoverable actions and an intentional no-source state. | [Before](before-desktop.png), [after](after-desktop.png), applied-filter and empty-state component tests | Verified |
| AVP-02 | P2 | Evidence / FinancePlanningEvidence | Compressed comparison measures and source actions. Given recorded versions, display all 11 measures with correct signs, unavailable values, source inspection, and contained keyboard scrolling on a phone. | [Recorded versions](after-selected-versions-desktop.png), [sources](after-source-evidence.png), [phone](after-selected-versions-narrow.png), populated comparison test | Verified |
| AVP-03 | P2 | Error recovery / FinanceVariance | After a bounded query fails, available account choices disappeared. Given the 2,000-record error, withhold stale report/export, keep scoped choices, and recover by selecting an account. Clear choices when access or company changes. | [Range error](after-range-error.png), browser recovery, error/access/company component tests | Verified |
| AVP-04 | P2 | Responsive report / scoped CSS | Filters and wide measures must remain usable at smaller widths. Given widths 1366, 1024 and 390, no document overflow; controls remain visible and the table scrolls independently. | [Phone empty state](after-narrow.png), populated phone capture, browser geometry checks | Verified |

## Validation

```powershell
dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter 'FullyQualifiedName~FinanceRollingPlanningJourneyTests|FullyQualifiedName~FinanceOperationalJourneyTests' --logger 'trx;LogFileName=planning-web.trx' --results-directory docs/verification/vcscreens/actual-versus-plan-redesign-2026-10-08 -m:1 -p:UseSharedCompilation=false
```

30 passed, 0 failed, 0 skipped: [TRX](planning-web.trx). Compilation succeeded with six existing Web nullable warnings. Covered refreshed authorized export and revoked-access denial, retained reviews, signed/unavailable values, source drill-down, applied filter context, empty baselines, query-error recovery, initial error controls and company-scoped choice clearance. Adjacent cash and aging report regression tests also passed. No backend or schema change required a separate database suite.

[verify-browser.mjs](verify-browser.mjs) performs the original flow against the running app. Final [browser results](after-browser.json): nine groups passed, no browser runtime errors.

1. Original October report: zero rows and no selected versions, with the same recorded budget/forecast choices as before.
2. Keyboard Enter opens all seven real source-coverage notes.
3. CSV physically saved as [variance.csv](variance.csv), 163 bytes. This initial empty report exports the column header; populated CSV handoff and access denial are covered by focused component tests. SHA-256: `76AEA42DBB5D871F964677B3015F6C4EACAE7EA1F88E55AFF8AF3F9E5DC64436`.
4. Two-month JPY filters apply, update context, and survive reload.
5. Twelve-month budget/forecast selection reaches the 2,000-record guard; selecting recorded account 1000 recovers without stale export.
6. The scoped recorded baseline yields two November/December rows and four source records. Source inspection opens real evidence; phone keyboard scrolling works. Actuals and variances remain unavailable for these future plan rows.
7. Back navigation returns to Cash forecast with company and dashboard return values intact, checked semantically through URL query values.
8. Posted-journal navigation retains the company and return to this report.
9. Widths 1366, 1024 and 390 remain contained; phone filter inputs remain visible.

The intermediate `browser-failure.*` packet records the earlier automation selector mismatch and is superseded by the final passing results. The original `before-browser.json` contains the native account/version choices and report baseline. Visual comparison against the reference was performed using the desktop and populated/empty phone captures. These results establish this local report UI acceptance; deployed authentication and retained-review editing were not exercised in the live company.

## Follow-up — reference icons and comparison basis

The user's four browser comments requested icons in the three summary cards, Source coverage and Next steps, plus a more professional Comparison basis card. This small refinement reuses the generated reference above. Calendar, document and chart icons now sit in pale blue summary badges; Source coverage and Next steps have blue database and checklist icons. Comparison basis has an information icon, structured Actuals / Variance / Scope rows, and a keyboard-accessible Limits and interpretation disclosure. The month caption now reads `1 month` or the plural count.

All icons are native SVG outlines, using the existing product's stroke conventions; they are decorative, hidden from accessibility names and excluded from keyboard focus. No icon font or external asset dependency was introduced. Comparison text was checked against `FinanceRollingPlanningCalculation.Difference` and `Percentage` and the backend coverage notes: absent values stay unavailable, variance requires both actual and budget, and variance percentage is unavailable for a zero or missing budget.

| ID | Severity | Flow / owner | Acceptance | Evidence / regression | Status |
|---|---|---|---|---|---|
| AVP-05 | P3 | Summary and rail cards / FinanceVariance | Three blue icon badges match the reference; Source coverage and Next steps have visible blue outline icons. Decorative icons do not affect accessibility names; all cards remain contained at 1366, 1024 and 390 px. | [Before](cards-before-desktop.png), [after](cards-after-desktop.png), rendered geometry and accessibility checks | Verified |
| AVP-06 | P2 | Comparison basis / FinanceVariance | Clear explanation rows and an expandable limits section retain missing-evidence, zero-budget, exclusion and sign interpretation meaning; keyboard Enter opens the limits. | [Card](cards-after-basis.png), [expanded card](cards-after-basis-expanded.png), [phone](cards-after-narrow.png) | Verified |

Validation: `dotnet test tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FinanceRollingPlanningJourneyTests --logger 'trx;LogFileName=card-polish-web.trx' --results-directory docs/verification/vcscreens/actual-versus-plan-redesign-2026-10-08 -m:1 -p:UseSharedCompilation=false` — **20 passed, 0 failed, 0 skipped**; Web and dependencies built successfully with the same six existing Web nullable warnings. [TRX](card-polish-web.trx).

[verify-cards.mjs](verify-cards.mjs) repeats the user's original entry with its nested reconciliation and journal return context. [Final browser results](cards-after-browser.json): **six groups passed, zero runtime errors**. Verified all six SVG icons are visible blue outlines, original summary values and row count are unchanged, structured meaning and disclosure work, three smaller widths remain contained, and Back returns to reconciliation with company, accounting period and nested journal return intact. The tested Web snapshot was refreshed only after verifying the recorded old host's exact process command and port ownership.
