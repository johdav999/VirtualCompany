# Financial statements implementation and UAT

Date: 2026-09-30. Base revision: `6c5532d7`, plus uncommitted workspace changes. Workflow: installed `polish-uat-loop` skill. Scope: profit and loss and balance sheet only. No migrations or ledger writes were introduced.

## Verification results

| Check | Result |
|---|---|
| Web and API builds, .NET 9, normal workspace sandbox | Passed |
| Finance calculation/reporting tests | 18 passed |
| API snapshot/drill-down tests, including new statement workspace tests | 7 passed |
| Existing period reporting, period close and statement mapping/persistence integration tests | 26 passed |
| Web workspace interactions and existing accounting report surface tests | 14 passed |
| JavaScript CSV and print lifecycle tests | 2 passed |
| Responsive DOM geometry at 1536, 1440, 1024 and 390 pixels | No page-level horizontal overflow; all KPI values fit their cards |

API tests run against the repository's authenticated isolated `TestWebApplicationFactory` and real persistence/reporting services. They cover open and retained sources, exact date boundaries, comparison context, account sums, snapshot identity/checksum, wrong report type, unavailable snapshots, foreign-company access and foreign comparison periods. Existing tests cover retained mapping rules and opening-balance reconciliation. Component tests cover filtering, account expansion, row/account callbacks, source context, saved report selection, error/empty states, cancellation across companies and signed expense evidence, including a changed live amount. JavaScript tests verify exact CSV content and print cleanup after a browser error.

An unrelated concurrent Sales edit temporarily prevented a dependency build; later normal Web/API builds and complete focused test builds passed. No unrelated Sales files were changed for this feature.

## Browser evidence

The isolated read-only browser harness renders the actual production Blazor report component, its compiled scoped CSS, the application's Bootstrap/global CSS and existing Laura avatar. Its deterministic statements are calculated by the production layout service. A copy of the existing development app's canonical navigation is used as the shell. Fixture data is isolated from production.

Launch: export fixtures using `VC_STATEMENT_UAT_DIRECTORY` and the `Export_visual_fixtures_from_the_production_components` test, then run `node tests/scripts/Serve-FinancialStatementsUat.cjs`. Capture through the Codex browser with viewport overrides and device scale 1. Run `python tests/scripts/Compare-FinancialStatements.py` to compose unmodified references with browser screenshots.

| Flow | Evidence | Result |
|---|---|---|
| Profit and loss, reference-native viewport | [Screenshot](profit-loss-native.jpg), [side by side](profit-loss-side-by-side.png), [50% overlay](profit-loss-overlay.png) | Visually reviewed; core layout closely follows reference |
| Balance sheet, reference-native viewport | [Screenshot](balance-sheet-native.jpg), [side by side](balance-sheet-side-by-side.png), [50% overlay](balance-sheet-overlay.png) | Visually reviewed; core layout closely follows reference |
| Tablet layout | [Profit and loss](profit-loss-1024.jpg), [balance sheet](balance-sheet-1024.jpg) | Rail stacks below report; KPI values remain within cards |
| Mobile layout | [Profit and loss](profit-loss-390-full.jpg), [balance sheet](balance-sheet-390-full.jpg) | Cards and rail stack; statement table scrolls within its container |

[DOM geometry](geometry.json) records viewport, device scale, report/table/card/rail rectangles and overflow checks. Some browser-native captures exclude the scrollbar gutter and uniformly scale the result; their image dimensions are smaller than the requested viewport. Raw screenshots are preserved. Comparison compositions normalize those captures to a 1536×1024 canvas; no pixel-difference score is asserted. Browser clip captures were discarded after they produced a distorted rendering; the retained evidence uses normal screenshots.

At the native viewport, income-statement cards start at about 266px versus 265px in the reference; the table starts at about 421px versus 419px. Balance-sheet cards start at about 206px versus 206px; its table starts at about 387px versus 386px. These are selected geometry checks, not a whole-image pixel score or a claim that every boundary meets the strict tolerance.

## Intentional differences

- The canonical existing 250px sidebar, logo, navigation and mobile menu remain consistent across both reports. The references use different, narrower shells. Content therefore starts farther right, particularly on the balance sheet.
- Actual fiscal-period dates, a read-only source currency, truthful period/source badges and existing avatar replace illustrative controls and example branding. The product does not show an `Exempeldata` badge for real journal data.
- Negative expense baselines show an unavailable percentage rather than the reference's positive expense-growth percentage. Balance changes show absolute differences. Directional colours reflect signed values.
- The evidence rail shows real opening/journal/reconciliation information and retained provenance rather than a fabricated voucher or journal count. Its height differs from the reference. Controls do not assert a company-wide opening-balance check that has not been performed.
- Long retained equity labels preserve the meaning of other equity. Export and print are connected to actual output rather than decorative split-button options.

## Limits of verification

The already running development Web/API hosts use an older build; the new workspace endpoint returned 404 there. They were left running because other ongoing work was using them. The new reports were **not verified end to end in that live authenticated browser**, and native print-preview output was not captured. Authentication, persistence and report reads were instead exercised through the isolated integration host; browser visuals through production component fixtures; interactions and output through bUnit/JavaScript.

Normal local restart with the repository's Web/API run scripts is required to expose the new screens in the running development app. After restart, confirm both company-scoped deep links, account/journal navigation and print preview against the intended company. Strict pixel fidelity across all regions is not claimed; the saved overlays show the remaining documented differences.

## Issue ledger

| ID | Priority | Finding | Acceptance / evidence | Status |
|---|---|---|---|---|
| FS-01 | P2 | Generic hero inset displaced reports vertically | Remove only the statement hero inset; native geometry and screenshots | Fixed and verified in browser fixture |
| FS-02 | P2 | KPI amounts overlapped at 1024px | Stack rail below report and keep every value within its card; geometry | Fixed and verified in browser fixture |
| FS-03 | P1 | Expense evidence used a different sign from statement | Signed evidence and visible changed-source difference; behavioural component test | Fixed and verified |
| FS-04 | P1 | Late response could show an obsolete company report | Cancel/ignore stale response; controlled delayed response test | Verified |
| FS-05 | P1 | Mixed currencies could be aggregated misleadingly | Reject mixed source currencies and differing comparison currencies | Implemented; comparison/source validation is explicit |
| FS-06 | P2 | Saved-report version text did not interpolate | Select actual `v2` history button; callback test | Fixed and verified |
| FS-07 | P2 | Journal evidence deep link used unsupported parameter | Use existing `journalId` route contract | Fixed; live browser journey pending restart |
| FS-08 | P2 | Existing live hosts lack new endpoint | Restart and exercise authenticated company reports and print preview | Live verification pending |
