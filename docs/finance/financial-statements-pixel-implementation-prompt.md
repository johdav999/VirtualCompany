# Implement Resultaträkning and Balansräkning to match the approved references

## 1. Title and outcome

Implement two fully functional Swedish financial report screens in Virtual Company: Resultaträkning and Balansräkning. Match the supplied reference images as closely as possible at pixel level, while using authoritative company accounting data. Deliver working UI, any necessary narrowly scoped reporting extensions, and verified screenshot evidence. Do not stop after analysis, a plan, or the first report.

## 2. Current context

Repository: `C:\Users\Johan\source\repos\Virtual Company`.

Mandatory visual targets, relative to the repository root:

- `/docs/design/references/resultatrakning-reference.png`
- `/docs/design/references/balansrakning-reference.png`
- Original image prompts: `/docs/design/references/financial-statements-reference-prompts.txt`.

Open and inspect both actual PNGs before implementing. The images are design references, not production assets to render as backgrounds. The screenshot-first workflow in `/docs/design.md` applies; these existing generated references and saved prompts fulfil its initial reference-generation step. Preserve them and implement against them.

Existing implementation to inspect and extend:

- `/src/VirtualCompany.Web/Pages/Finance/AccountingReportsPage.razor`, `.razor.cs`, and `.razor.css`; existing route `/finance/accounting/reports`.
- `/src/VirtualCompany.Web/Services/FinanceApiClient.AccountingReports.cs` and current financial statement clients/contracts.
- `/src/VirtualCompany.Infrastructure.Finance/Finance/CompanyFinanceReadService.Reporting.cs` and statement builders in `CompanyFinanceReadService.cs`.
- `/src/VirtualCompany.Infrastructure.Finance/Finance/AccountingReportingService.cs`.
- `/src/VirtualCompany.Api/Controllers/InternalFinanceController.AccountingReports.cs` and `FinancialStatementSnapshotsController.cs`.
- `/tests/VirtualCompany.Web.Tests/AccountingReportsSurfaceTests.cs` and existing statement mapping, snapshot, and drill-down integration tests.

Profit-and-loss and balance-sheet queries, account mappings, closed-period snapshots, general ledger reads, and evidence already exist. The current statement UI is basic. The reference's richer groupings, comparisons, filters, and actions must be connected to real capabilities; inspect their actual contracts before selecting implementation boundaries. In particular, current queries are fiscal-period based: do not label one month's data as January–August, or claim an arbitrary as-of date that the backend does not honour.

## 3. Dependencies

No preceding implementation prompt. Requires the existing project dependencies and a runnable authenticated test company for full browser verification. No new external service is required. Schema changes are not expected; use existing reporting facts and mappings. If a necessary persistence change is discovered, implement and verify it under the canonical migration rules.

Read and follow `/AGENTS.md`, `/docs/AGENTS.md`, `/src/AGENTS.md`, `/src/VirtualCompany.Web/AGENTS.md`, `/tests/AGENTS.md`, and any nearer instructions. Read `/production-implementation.md`, `/docs/architecture-rules.md`, and `/docs/design.md`; use `/ui-instructions.md` as a subordinate implementation companion. Invoke the installed `$polish-uat-loop` skill for screenshot-led implementation and browser verification.

## 4. Implementation requirements

### Shared presentation and pixel fidelity

Build the reference layout using native Blazor components and CSS within the current app shell. Match sidebar, breadcrumb, report tabs, main content width, right rail, toolbar, KPI cards, table, typography, row heights, border colours, shadows, radii, alignment, icons, and whitespace. Extract measurements from the actual image dimensions rather than assuming the dimensions requested in the image prompts. Target the PNGs' native viewport at browser zoom 100% and device scale 1.

The two generated images contain small shell/logo differences. Preserve the canonical existing product logo and app navigation, and use one consistent shell for both reports. Document that intentional exception; do not redesign global navigation or regress unrelated screens just to reproduce generated inconsistencies. Otherwise use each report's reference as its visual authority, subject to `/docs/design.md`.

Use shared report components where appropriate, with scoped CSS. Avoid approximate generic dashboard templates. Render all text, controls, and tables as accessible selectable UI, never as image slices. Use existing icon and avatar assets. Swedish labels and number/date formatting must match the references; preserve English localization. Keep numbers right-aligned with tabular numerals. Allow kr/tkr scale selection only if consistently applied to cards, table, totals, and export metadata; currency selection must not relabel SEK amounts as another currency without supported conversion.

At narrow widths stack the toolbar, KPI cards, table, and evidence rail in a usable order; contain horizontal scrolling within the table. Include explicit loading, no-data, unavailable-comparison, forbidden, and retryable-error states. Preserve the user's selected report, period, comparison, and account across appropriate interactions. Cancel or ignore stale responses when filters change quickly.

Expose stable deep links `/finance/accounting/reports?view=profit-loss` and `?view=balance-sheet`, retaining company context. Fix query-view handling as needed; the current page does not explicitly initialize these two views. Keep other accounting report views and period-close functionality reachable.

### Resultaträkning

Match the resultaträkning reference: heading/subtitle, period and comparison selectors, report-source badge, export and print controls; three cards for Nettoomsättning, Rörelseresultat, and Periodens resultat; expandable statement table with current, comparison, and change columns; Laura insight card, selected-post evidence, and report-source/history card.

Support Swedish statement groupings: Nettoomsättning, Övriga rörelseintäkter, Summa rörelseintäkter, Råvaror och handelsvaror, Övriga externa kostnader, Personalkostnader, Avskrivningar, Rörelseresultat, Finansiella intäkter, Finansiella kostnader, Resultat före skatt, Skatt, and Periodens resultat. Use authoritative classifications/mappings and applicable accounting policy, not account-name string matching or hard-coded demo-account rules. If current classification cannot distinguish required groups, add a bounded explicit mapping solution in the owning finance layer. Flag unmapped activity rather than silently omit it.

Calculate subtotals and cards from the same source population as the table. Show costs with the reference's negative display convention without changing stored accounting signs or double-negating existing DTO amounts. Compare identical date windows across years for the selected accumulated period, or explicitly label supported alternatives. Handle zero/negative comparison bases with a defined, tested rule; show unavailable percentage as an em dash with explanation. Increased costs must not appear green merely because their absolute magnitude increased: correct the reference's misleading cost-change colouring while preserving its geometry.

### Balansräkning

Match the balance-sheet reference: as-of/comparison controls, three cards for Tillgångar, Eget kapital, and Skulder; reconciliation strip; hierarchical table with TILLGÅNGAR, EGET KAPITAL, SKULDER; right-hand Laura insight, selected account movement, report checks, and saved-report access.

Support classifications for Anläggningstillgångar, Varulager, Kundfordringar, Kassa och bank; Aktiekapital, Balanserat resultat, Periodens resultat; Långfristiga skulder, Leverantörsskulder, and Övriga kortfristiga skulder. Preserve authoritative opening balances and current-earnings treatment, including preventing double-counting when earnings have already been transferred to equity. Compare balances at the displayed dates, not interval revenue/expense movements.

Calculate `Tillgångar − (Eget kapital + Skulder)` from authoritative totals with the existing currency precision policy. Show green 'Balansen stämmer' only when the actual control passes; otherwise display the amount and a clear review state. Do not display successful opening-balance or reconciliation checks merely because the reference shows green ticks. The selected-account panel must reconcile opening balance plus signed movements to closing balance for the selected scope, and distinguish that scope from the comparison date when they differ.

### Real data, evidence, and actions

Use company-scoped authenticated APIs. Add necessary period-range/as-of/comparison support in Application contracts and owning Finance services when existing endpoints cannot satisfy the displayed scope. Keep controllers transport-only and avoid accounting calculations inside Razor. Preserve existing routes/contracts through additive compatible extensions.

Closed-period reads and drill-down must retain the exact snapshot/version and source population. Do not silently mix historical snapshots with regenerated current mappings. Show open/closed/reporting-locked states accurately and expose retained report history through existing capabilities.

Expand/collapse reveals actual accounts; selecting a row updates the evidence rail without switching away from the statement. 'Visa huvudbok' opens the matching company/account/period context. Handle synthetic earnings/subtotal rows appropriately: trace their contributing journals instead of inventing an account ID.

Laura's recommendations must be grounded in the current report and actual checks, using existing deterministic insight services where possible. Show a truthful neutral fallback when evidence is insufficient; no fabricated commentary.

Implement working print styles and exports for the selected report, filters, comparison, currency/scale, and retained version. Reuse the durable export workflow when applicable; extend supported export types if required. Exported totals and provenance must match displayed data, with clear pending/failure/download states and existing authorization. Do not show a decorative Exportera button that exports an unrelated generic payload.

The references' company name, amounts, voucher numbers, and 'Exempeldata' badge are illustrative. Never hard-code them into production. Reproduce those data shapes only in isolated deterministic visual-test fixtures; show 'Exempeldata' only when the actual source mode warrants it. Do not introduce statutory-validation claims.

## 5. Constraints and preservation rules

Follow canonical architecture, tenant isolation, report immutability, authorization, audit, and workflow rules by reference. Preserve trial balance, ledger, VAT, tax, revaluation, dimensions, schedules, assets, close/reopen, exports, existing snapshots, and unrelated local changes. Do not expand this task to cash flow or a general KPI dashboard.

Do not write or reclassify ledger facts to make the layout fit. Do not regenerate the visual references to resemble an easier implementation. Any necessary departure from the images must be narrowly justified by accounting correctness, accessibility, actual data, or the canonical design system, and documented with its visual impact.

## 6. Acceptance criteria

- Given an authorized company with posted journals, when each report opens, then its cards, groups, account lines, and totals reconcile to the corresponding authoritative source for the displayed scope.
- Given selected January–August and a previous-year comparison, then both periods cover the displayed dates exactly; otherwise the UI explicitly offers only supported scopes.
- Given balance-sheet dates, then both columns are balances at those dates, including opening balances and equity/earnings treatment.
- Given a closed-period snapshot, then displayed data, history, drill-down, and exports retain the same snapshot identity and totals after later mapping changes.
- Given an unmapped account, missing baseline, zero denominator, failed control, or service error, then the corresponding limitation is visible and no invented amount or success badge appears.
- Given report row selection, then the evidence rail updates while the active report remains unchanged; ledger links retain company and scope.
- Given a different company's identifiers, then APIs and exports reject access and no data leaks through filters, caches, histories, or comparison reads.
- Given each isolated visual fixture at its reference-native viewport, then the screenshot closely matches its PNG. Major column/card/table boundaries should be within 4 CSS pixels and repeated spacing/row heights within 2 CSS pixels after viewport alignment, except explicitly documented shell differences. Do not claim a whole-image pixel score as sufficient evidence: review overlays and individual regions for geometry, fonts, hierarchy, and controls.
- Given 1440px, 1024px, and 390px viewport widths, then controls remain usable, no page-level horizontal overflow occurs, and all report data/actions remain accessible.
- Given Exportera or Skriv ut, then the output contains the selected report and matches its displayed totals and context.

## 7. Verification

Run focused reporting calculation and integration tests for any backend extensions, covering dates, comparison arithmetic, signs, grouping, opening balances, earnings, snapshot retention, authorization, and tenant isolation. Add interaction/component coverage for report selection, filters, expansion, drill-down, state handling, and export. Prefer behavioural checks over new source-string assertions.

Build affected projects and run existing focused report tests first, then one appropriate broader validation. Verify migration/model consistency if persistence changes. Follow Web process-lifecycle instructions when launching the app.

Invoke `$polish-uat-loop`, exercise both authenticated reports, capture screenshots at each PNG's native dimensions, produce side-by-side and 50%-opacity overlay comparisons, record defects, correct them, and recapture until the acceptance criteria pass. Inspect the images visually, including table alignment, toolbar wrapping, cards, rail spacing, and print layout. Save screenshots, comparisons, issue ledger, and verification results under `/docs/verification/financial-statements/`.

Use an isolated seeded test company to make visual fixtures reproducible. Also exercise a real authorized test-company source to verify actual data integration. If authentication or a required runtime is unavailable, complete independent implementation/build/test work and document browser verification as blocked; do not claim pixel fidelity from static source or component tests alone.

## 8. Definition of done

Both reports are implemented and functional, including comparisons, evidence, truthful statuses, print/export, and responsive/error states. No mock production data, scaffolding, silent failures, decorative dead controls, or deferred in-scope TODOs remain. Screenshot comparison evidence demonstrates the visual match, or any unavoidable verification blocker is stated explicitly. Report changed files, test/build results, screenshots, intentional visual exceptions, and remaining external blockers. Continue until both reports and their required verification are complete.
